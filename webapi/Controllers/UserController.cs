using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.User;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordResetRepository _resetTokens;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantRepository _tenants;
        private readonly IJwtIssuer _jwtIssuer;
        private readonly ISmtpEmailer _emailer;
        private readonly ILogger<UserController> _logger;

        public UserController(
            IUserRepository userRepository,
            IPasswordResetRepository resetTokens,
            IPasswordHasher<User> passwordHasher,
            ITenantContext tenantContext,
            ITenantRepository tenants,
            IJwtIssuer jwtIssuer,
            ISmtpEmailer emailer,
            ILogger<UserController> logger)
        {
            _userRepository = userRepository;
            _resetTokens = resetTokens;
            _passwordHasher = passwordHasher;
            _tenantContext = tenantContext;
            _tenants = tenants;
            _jwtIssuer = jwtIssuer;
            _emailer = emailer;
            _logger = logger;
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Global pool first (rider / super_admin — one account, many tenants).
            var user = await _userRepository.GetGlobalByEmail(request.Email);

            // Fall back to tenant-scoped (tenant_admin / tenant_staff) — only if a tenant is resolved.
            if (user is null && _tenantContext.IsResolved)
            {
                user = await _userRepository.GetByEmail(_tenantContext.TenantId, request.Email);
            }

            if (user is null || user.Status != "active")
            {
                return new ApiResponses().BadRequestResult("Invalid email or password.");
            }

            // Apex login (no tenant resolved) is only for super_admins. Riders at apex get a nudge
            // to use a tenant subdomain; tenant admins similarly.
            if (!_tenantContext.IsResolved && user.Role != "super_admin")
            {
                return new ApiResponses().BadRequestResult("Please log in from your tenant's subdomain.");
            }

            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verifyResult == PasswordVerificationResult.Failed)
            {
                return new ApiResponses().BadRequestResult("Invalid email or password.");
            }

            if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
                // TODO: persist re-hashed password.
            }

            var token = _jwtIssuer.IssueForUser(user);

            return new ApiResponses().OkResult(new LoginResponse
            {
                Token = token,
                UserId = user.Id,
                TenantId = user.TenantId ?? (_tenantContext.IsResolved ? _tenantContext.TenantId : Guid.Empty),
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role
            });
        }

        [HttpPost("CreateAccount")]
        public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("Account creation must happen on a tenant subdomain.");
            }

            // Public signup creates a GLOBAL rider account (tenant_id NULL) — the same login
            // works at every tenant subdomain. Tenant admins/staff are provisioned separately.
            var existing = await _userRepository.GetGlobalByEmail(request.Email);
            if (existing is not null)
            {
                return new ApiResponses().BadRequestResult("An account with this email already exists — please log in.");
            }

            if (!IsValidBirthdate(request.Birthdate))
            {
                return new ApiResponses().BadRequestResult("Please enter a valid birthdate.");
            }
            var contactName = request.EmergencyContactName.Trim();
            var contactPhone = request.EmergencyContactPhone.Trim();
            if (contactName.Length == 0 || DigitsOnly(contactPhone).Length < 7)
            {
                return new ApiResponses().BadRequestResult("Please enter a valid emergency contact name and phone number.");
            }
            var riderPhone = request.Phone.Trim();
            if (DigitsOnly(riderPhone).Length < 7)
            {
                return new ApiResponses().BadRequestResult("Please enter a valid phone number — we use it for waitlist and event alerts.");
            }

            var user = new User
            {
                TenantId = null,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Role = "rider",
                Status = "active",
                Phone = riderPhone,
                Birthdate = request.Birthdate.Date,
                EmergencyContactName = contactName,
                EmergencyContactPhone = contactPhone,
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            var id = await _userRepository.Create(user);
            user.Id = id;

            return new ApiResponses().OkResult(new { user.Id, user.Email, user.FirstName, user.LastName, user.Role });
        }

        [Authorize]
        [HttpGet("Profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var user = await _userRepository.GetById(userId);
            if (user is null)
            {
                return new ApiResponses().NotFoundResult("User not found.");
            }

            // Defence-in-depth: tenant-scoped users must match the resolved subdomain's tenant.
            // Global users (rider, super_admin) have TenantId = NULL and may access any tenant.
            if (_tenantContext.IsResolved && user.TenantId.HasValue && user.TenantId != _tenantContext.TenantId)
            {
                return Forbid();
            }

            return new ApiResponses().OkResult(new
            {
                user.Id,
                user.TenantId,
                user.Email,
                user.FirstName,
                user.LastName,
                user.Role,
                user.Status,
                user.Phone,
                user.Birthdate,
                user.EmergencyContactName,
                user.EmergencyContactPhone,
                user.AddressLine,
                user.AddressLine2,
                user.City,
                user.State,
                user.PostalCode,
                user.Country,
                user.Bike,
                user.RaceNumber,
            });
        }

        [Authorize]
        [HttpPut("Profile/EmergencyContact")]
        public async Task<IActionResult> UpdateEmergencyContact([FromBody] UpdateEmergencyContactRequest request)
        {
            if (!TryGetSelfId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var name = request.Name?.Trim() ?? string.Empty;
            var phone = request.Phone?.Trim() ?? string.Empty;
            if (name.Length == 0 || DigitsOnly(phone).Length < 7)
            {
                return new ApiResponses().BadRequestResult("Please enter a valid emergency contact name and phone number.");
            }
            await _userRepository.UpdateEmergencyContact(userId, name, phone);
            return new ApiResponses().OkResult(new { name, phone });
        }

        [Authorize]
        [HttpPut("Profile/Phone")]
        public async Task<IActionResult> UpdatePhone([FromBody] UpdatePhoneRequest request)
        {
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var phone = request.Phone?.Trim() ?? string.Empty;
            if (DigitsOnly(phone).Length < 7)
            {
                return new ApiResponses().BadRequestResult("Please enter a valid phone number.");
            }
            await _userRepository.UpdatePhone(userId, phone);
            return new ApiResponses().OkResult(new { phone });
        }

        [Authorize]
        [HttpPut("Profile/RacerInfo")]
        public async Task<IActionResult> UpdateRacerInfo([FromBody] UpdateRacerInfoRequest request)
        {
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var bike = string.IsNullOrWhiteSpace(request.Bike) ? null : request.Bike.Trim();
            var raceNumber = string.IsNullOrWhiteSpace(request.RaceNumber) ? null : request.RaceNumber.Trim();
            // Race numbers stay alphanumeric — we keep them as freeform text since formats
            // vary by class (e.g. "21", "07B", "X22"), but cap length so nothing absurd.
            if (bike is { Length: > 100 })
            {
                return new ApiResponses().BadRequestResult("Bike name is too long.");
            }
            if (raceNumber is { Length: > 16 })
            {
                return new ApiResponses().BadRequestResult("Race number is too long.");
            }
            await _userRepository.UpdateRacerInfo(userId, bike, raceNumber);
            return new ApiResponses().OkResult(new { bike, raceNumber });
        }

        [Authorize]
        [HttpPut("Profile/Address")]
        public async Task<IActionResult> UpdateAddress([FromBody] UpdateAddressRequest request)
        {
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            await _userRepository.UpdateAddress(userId,
                addressLine: Norm(request.AddressLine),
                addressLine2: Norm(request.AddressLine2),
                city: Norm(request.City),
                state: Norm(request.State),
                postalCode: Norm(request.PostalCode),
                country: Norm(request.Country) ?? "US");
            return new ApiResponses().OkResult();
        }

        [Authorize]
        [HttpPut("Profile/Birthdate")]
        public async Task<IActionResult> UpdateBirthdate([FromBody] UpdateBirthdateRequest request)
        {
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (!IsValidBirthdate(request.Birthdate))
            {
                return new ApiResponses().BadRequestResult("Please enter a valid birthdate.");
            }
            await _userRepository.UpdateBirthdate(userId, request.Birthdate.Date);
            return new ApiResponses().OkResult(new { birthdate = request.Birthdate.Date });
        }

        private bool TryGetSelfId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }

        // ── Tenant user management ────────────────────────────────────────────────

        private static readonly HashSet<string> AssignableRoles = new()
        {
            "tenant_admin", "tenant_manager", "tenant_cashier", "tenant_scanner", "tenant_accountant",
        };

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpGet("Tenant")]
        public async Task<IActionResult> ListTenantUsers()
        {
            var users = await _userRepository.ListByTenant(_tenantContext.TenantId);
            var items = users.Select(u => new TenantUserListItem
            {
                Id = u.Id,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Role = u.Role,
                Status = u.Status,
                CreatedAtUtc = DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc),
            });
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPost("Tenant")]
        public async Task<IActionResult> CreateTenantUser([FromBody] CreateTenantUserRequest request)
        {
            if (!AssignableRoles.Contains(request.Role))
            {
                return new ApiResponses().BadRequestResult($"Role '{request.Role}' is not assignable.");
            }
            var email = request.Email.Trim();

            var existingTenant = await _userRepository.GetByEmail(_tenantContext.TenantId, email);
            if (existingTenant is not null)
            {
                return new ApiResponses().BadRequestResult("A user with that email already exists on this tenant.");
            }
            var existingGlobal = await _userRepository.GetGlobalByEmail(email);
            if (existingGlobal is not null)
            {
                return new ApiResponses().BadRequestResult(
                    "That email is already registered as a rider on RidePass. Use a different email for this tenant account.");
            }

            var tempPassword = GenerateTemporaryPassword();
            var user = new User
            {
                TenantId = _tenantContext.TenantId,
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Role = request.Role,
                Status = "active",
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);
            user.Id = await _userRepository.Create(user);

            if (_emailer.IsConfigured)
            {
                var loginUrl = $"{Request.Scheme}://{Request.Host.Value}/Login";
                var resetUrl = $"{Request.Scheme}://{Request.Host.Value}/ResetPassword";
                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(user.FirstName)},</p>
<p>You've been added as a <strong>{System.Net.WebUtility.HtmlEncode(user.Role)}</strong> on RidePass.</p>
<p><strong>Sign in:</strong> <a href=""{loginUrl}"">{loginUrl}</a><br/>
<strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(user.Email)}<br/>
<strong>Temporary password:</strong> <code>{tempPassword}</code></p>
<p>Please <a href=""{resetUrl}"">reset your password</a> after your first sign-in.</p>";
                await _emailer.Send(user.Email, "Welcome to RidePass", html);
            }

            return new ApiResponses().OkResult(new CreateTenantUserResponse
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role,
                TemporaryPassword = tempPassword,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPut("Tenant/{id:guid}/Role")]
        public async Task<IActionResult> UpdateTenantUserRole(Guid id, [FromBody] UpdateTenantUserRoleRequest request)
        {
            if (!AssignableRoles.Contains(request.Role))
            {
                return new ApiResponses().BadRequestResult($"Role '{request.Role}' is not assignable.");
            }
            var target = await _userRepository.GetById(id);
            if (target is null || target.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().NotFoundResult("User not found on this tenant.");
            }
            if (SelfId() == id && request.Role != "tenant_admin")
            {
                return new ApiResponses().BadRequestResult("You can't demote your own admin role.");
            }
            await _userRepository.UpdateRole(id, request.Role);
            return new ApiResponses().OkResult(new { id, role = request.Role });
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPut("Tenant/{id:guid}/Status")]
        public async Task<IActionResult> UpdateTenantUserStatus(Guid id, [FromBody] UpdateTenantUserStatusRequest request)
        {
            if (request.Status is not ("active" or "disabled"))
            {
                return new ApiResponses().BadRequestResult("Status must be 'active' or 'disabled'.");
            }
            var target = await _userRepository.GetById(id);
            if (target is null || target.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().NotFoundResult("User not found on this tenant.");
            }
            if (SelfId() == id && request.Status == "disabled")
            {
                return new ApiResponses().BadRequestResult("You can't disable your own account.");
            }
            await _userRepository.UpdateStatus(id, request.Status);
            return new ApiResponses().OkResult(new { id, status = request.Status });
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPost("Tenant/{id:guid}/ResetPassword")]
        public async Task<IActionResult> ResetTenantUserPassword(Guid id)
        {
            var target = await _userRepository.GetById(id);
            if (target is null || target.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().NotFoundResult("User not found on this tenant.");
            }
            var tempPassword = GenerateTemporaryPassword();
            var hash = _passwordHasher.HashPassword(target, tempPassword);
            await _userRepository.UpdatePasswordHash(id, hash);

            if (_emailer.IsConfigured)
            {
                var loginUrl = $"{Request.Scheme}://{Request.Host.Value}/Login";
                var resetUrl = $"{Request.Scheme}://{Request.Host.Value}/ResetPassword";
                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(target.FirstName)},</p>
<p>An administrator has reset your RidePass password.</p>
<p><strong>Sign in:</strong> <a href=""{loginUrl}"">{loginUrl}</a><br/>
<strong>Temporary password:</strong> <code>{tempPassword}</code></p>
<p>Please <a href=""{resetUrl}"">change it</a> after your next sign-in.</p>";
                await _emailer.Send(target.Email, "Your RidePass password was reset", html);
            }

            return new ApiResponses().OkResult(new ResetTenantUserPasswordResponse
            {
                TemporaryPassword = tempPassword,
            });
        }

        // ── Self-serve password reset ────────────────────────────────────────────

        /// <summary>
        /// Public endpoint: request a reset email. Always returns 200 to avoid revealing
        /// which addresses have accounts. If a matching account is found and SMTP is
        /// configured, a one-time token is emailed.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("ResetPassword")]
        public async Task<IActionResult> RequestPasswordReset([FromBody] ResetPasswordRequest request)
        {
            var email = request.Email.Trim();

            // Mirror Login resolution: prefer a global account; fall back to tenant-scoped.
            var user = await _userRepository.GetGlobalByEmail(email);
            if (user is null && _tenantContext.IsResolved)
            {
                user = await _userRepository.GetByEmail(_tenantContext.TenantId, email);
            }

            if (user is not null && user.Status == "active")
            {
                var token = GenerateResetToken();
                var hash = HashToken(token);
                await _resetTokens.Insert(new PasswordResetToken
                {
                    UserId = user.Id,
                    TokenHash = hash,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(60),
                });

                var resetUrl = await BuildResetUrl(user, token);
                if (_emailer.IsConfigured)
                {
                    var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(user.FirstName)},</p>
<p>We received a request to reset the password for your RidePass account.</p>
<p><a href=""{resetUrl}"">Click here to set a new password</a>. This link expires in 60 minutes and can only be used once.</p>
<p>If you didn't request this, you can safely ignore this email.</p>";
                    await _emailer.Send(user.Email, "Reset your RidePass password", html);
                }
                else
                {
                    _logger.LogWarning("Password reset requested for {Email} but SMTP is not configured. Reset URL: {Url}", user.Email, resetUrl);
                }
            }

            // Always 200, regardless of whether a match was found.
            return new ApiResponses().OkResult(new { message = "If that email exists, a reset link has been sent." });
        }

        /// <summary>
        /// Public endpoint: consume a reset token and set the user's new password.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("ResetPassword/Confirm")]
        public async Task<IActionResult> ConfirmPasswordReset([FromBody] ConfirmPasswordResetRequest request)
        {
            var hash = HashToken(request.Token);
            var token = await _resetTokens.GetByTokenHash(hash);
            if (token is null || token.UsedAtUtc is not null || token.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return new ApiResponses().BadRequestResult("This reset link is invalid or has expired. Please request a new one.");
            }

            var user = await _userRepository.GetById(token.UserId);
            if (user is null || user.Status != "active")
            {
                return new ApiResponses().BadRequestResult("This reset link is invalid or has expired. Please request a new one.");
            }

            var newHash = _passwordHasher.HashPassword(user, request.NewPassword);
            await _userRepository.UpdatePasswordHash(user.Id, newHash);
            await _resetTokens.MarkUsed(token.Id);

            return new ApiResponses().OkResult(new { message = "Password updated. You can now sign in." });
        }

        private async Task<string> BuildResetUrl(User user, string token)
        {
            // Tenant-scoped users (tenant_admin, tenant_staff) need the link on their tenant subdomain.
            // Global users (rider, super_admin) get a link on whichever host the request came in on.
            var scheme = Request.Scheme;
            var host = Request.Host.Value;
            if (user.TenantId.HasValue)
            {
                var tenant = await _tenants.GetById(user.TenantId.Value);
                if (tenant is not null)
                {
                    var apex = ApexHostFromCurrent(host);
                    host = $"{tenant.Subdomain}.{apex}";
                }
            }
            return $"{scheme}://{host}/ResetPassword?token={Uri.EscapeDataString(token)}";
        }

        private static string ApexHostFromCurrent(string currentHost)
        {
            // Current host may be `tenant.ridepass.io`, `ridepass.io`, or `localhost:5070`.
            // Strip a leading subdomain only if there are 3+ labels and no port (i.e. real apex like ridepass.io).
            var hostOnly = currentHost.Split(':')[0];
            var parts = hostOnly.Split('.');
            if (parts.Length >= 3) return string.Join('.', parts.Skip(1));
            return currentHost;
        }

        private static string GenerateResetToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private Guid? SelfId()
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }

        private static string GenerateTemporaryPassword()
        {
            Span<byte> bytes = stackalloc byte[12];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes);
        }

        internal static bool IsValidBirthdate(DateTime b)
        {
            var today = DateTime.UtcNow.Date;
            return b.Date < today && b.Year >= 1900 && (today.Year - b.Year) <= 130;
        }

        internal static string DigitsOnly(string s) => new string(s.Where(char.IsDigit).ToArray());
    }
}
