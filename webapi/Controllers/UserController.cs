using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Email;
using Services.Helpers;
using Services.Repositories.Data.EventData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Storage;
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
        private readonly IImageStorage _imageStorage;
        private readonly IEventSubscriptionRepository _eventSubs;
        private readonly INewsletterRepository _newsletter;
        private readonly ILogger<UserController> _logger;

        public UserController(
            IUserRepository userRepository,
            IPasswordResetRepository resetTokens,
            IPasswordHasher<User> passwordHasher,
            ITenantContext tenantContext,
            ITenantRepository tenants,
            IJwtIssuer jwtIssuer,
            ISmtpEmailer emailer,
            IImageStorage imageStorage,
            IEventSubscriptionRepository eventSubs,
            INewsletterRepository newsletter,
            ILogger<UserController> logger)
        {
            _userRepository = userRepository;
            _resetTokens = resetTokens;
            _passwordHasher = passwordHasher;
            _tenantContext = tenantContext;
            _tenants = tenants;
            _jwtIssuer = jwtIssuer;
            _emailer = emailer;
            _imageStorage = imageStorage;
            _eventSubs = eventSubs;
            _newsletter = newsletter;
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
                // The hasher's parameters strengthened since this hash was written; recompute
                // and persist so the stronger hash actually sticks (not just this request).
                user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
                await _userRepository.UpdatePasswordHash(user.Id, user.PasswordHash);
            }

            // Riders must confirm their email before signing in. Only the 'rider' role
            // is gated (admins/staff are provisioned by trusted admins and never go
            // through public signup), and every pre-existing account was grandfathered
            // verified, so this only affects new public signups.
            if (user.Role == "rider" && !user.EmailVerified)
            {
                return new ApiResponses().BadRequestResult(
                    "Please verify your email before signing in. Check your inbox for the verification link.");
            }

            // "Remember me" = a longer-lived token, not a stored credential. NOTE: there is no
            // refresh/revocation mechanism today, so a remembered token stays valid for its full
            // life even after a password change or deactivation. 21 days is the agreed balance
            // between not re-authenticating a bench phone daily and limiting that exposure.
            var token = _jwtIssuer.IssueForUser(
                user, request.RememberMe ? TimeSpan.FromDays(21) : null);

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

        // Bootstrap for an authenticated client (the operator app calls this right after
        // login): returns identity plus the server-computed permission set so the app never
        // re-implements the role->permission map. Reads the user fresh by the JWT's UserId,
        // so roles reflect any change since the token was minted.
        [Authorize]
        [HttpGet("Me")]
        public async Task<IActionResult> Me()
        {
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
            {
                return new ApiResponses().BadRequestResult("No authenticated user.");
            }

            var user = await _userRepository.GetById(userId);
            if (user is null || user.Status != "active")
            {
                return new ApiResponses().BadRequestResult("Account not found or inactive.");
            }

            var roles = user.Roles is { Length: > 0 } ? user.Roles : new[] { user.Role };
            // Super admins implicitly hold every capability; everyone else gets the union
            // of their roles' permission sets.
            var permissions = roles.Contains("super_admin")
                ? TenantPermissions.All.ToArray()
                : TenantPermissions.ForRoles(roles).ToArray();

            return new ApiResponses().OkResult(new MeResponse
            {
                UserId = user.Id,
                TenantId = user.TenantId,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role,
                Roles = roles,
                Permissions = permissions,
                RequireIdAtCheckin = _tenantContext.IsResolved && _tenantContext.Tenant.RequireIdAtCheckin,
            });
        }

        // Inline-checkout helper: does an account already exist for this email? The unified
        // event checkout uses it to decide whether to offer a "log in" prompt (known email)
        // vs. proceed as guest, without bouncing the buyer to a login page.
        [AllowAnonymous]
        [HttpGet("EmailExists")]
        public async Task<IActionResult> EmailExists([FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new ApiResponses().OkResult(new { exists = false });
            }
            var trimmed = email.Trim();
            // Global pool first (riders / super admins), then the resolved tenant's staff.
            var exists = await _userRepository.GetGlobalByEmail(trimmed) is not null;
            if (!exists && _tenantContext.IsResolved)
            {
                exists = await _userRepository.GetByEmail(_tenantContext.TenantId, trimmed) is not null;
            }
            return new ApiResponses().OkResult(new { exists });
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

            // Persist the rider's notification choices for THIS track (signup is tenant-scoped).
            // Best-effort: a preference write shouldn't fail an otherwise-successful signup. SMS
            // only sticks when the phone normalizes to E.164; otherwise it falls back to email-only.
            try
            {
                if ((request.NotifyEventEmail || request.NotifyEventSms) && _tenantContext.Tenant.AllowEventSubscriptions)
                {
                    var smsPhone = request.NotifyEventSms ? TwilioSmsSender.NormalizeE164(riderPhone) : null;
                    await _eventSubs.Upsert(new EventSubscription
                    {
                        TenantId = _tenantContext.TenantId,
                        UserId = user.Id,
                        Email = user.Email,
                        Phone = smsPhone,
                        NotifyEmail = request.NotifyEventEmail,
                        NotifySms = request.NotifyEventSms && smsPhone is not null,
                    });
                }
                if (request.SubscribeNewsletter)
                {
                    await _newsletter.UpsertFromSignup(_tenantContext.TenantId, user.Email,
                        $"{user.FirstName} {user.LastName}".Trim(), "signup");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist signup notification preferences for {Email}.", user.Email);
            }

            // Email verification: when SMTP is configured the rider starts unverified and
            // must click a link before they can sign in. When SMTP is NOT configured we
            // can't deliver the link, so auto-verify rather than lock the account out.
            bool emailVerificationSent = false;
            if (_emailer.IsConfigured)
            {
                var verifyToken = GenerateResetToken();
                await _userRepository.SetEmailVerificationToken(
                    user.Id, HashToken(verifyToken), DateTime.UtcNow.AddDays(7));
                await SendVerificationEmail(user, verifyToken);
                emailVerificationSent = true;
            }
            else
            {
                await _userRepository.MarkEmailVerified(user.Id);
            }

            return new ApiResponses().OkResult(new
            {
                user.Id, user.Email, user.FirstName, user.LastName, user.Role,
                emailVerificationSent,
            });
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
                user.ImageUrl,
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

        // Single endpoint behind the "My Profile" form's Save — updates the editable identity
        // fields together. Email is deliberately not updated here (auth/verification concerns).
        [Authorize]
        [HttpPost("UpdateProfile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var first = request.FirstName?.Trim() ?? string.Empty;
            var last = request.LastName?.Trim() ?? string.Empty;
            if (first.Length == 0 || last.Length == 0)
            {
                return new ApiResponses().BadRequestResult("First and last name are required.");
            }
            var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            await _userRepository.UpdateProfile(userId, first, last, phone);
            // The "My Profile" form is one Save, so emergency contact + photo persist here too.
            // Both optional: blank values clear them (no purchase-time gate is enforced here).
            await _userRepository.UpdateEmergencyContact(userId,
                request.EmergencyContactName?.Trim() ?? string.Empty,
                request.EmergencyContactPhone?.Trim() ?? string.Empty);
            await _userRepository.UpdateImageUrl(userId,
                string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim());
            return new ApiResponses().OkResult(new
            {
                firstName = first,
                lastName = last,
                phone,
                emergencyContactName = request.EmergencyContactName?.Trim(),
                emergencyContactPhone = request.EmergencyContactPhone?.Trim(),
                imageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
            });
        }

        // Profile photo upload. Returns the stored public URL; the "My Profile" Save then
        // persists it via UpdateProfile. Avatars aren't tenant-specific, so they live under the
        // platform image folder (works for global riders + super admins with no tenant context).
        [Authorize]
        [HttpPost("Profile/Photo")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadProfilePhoto(IFormFile file, CancellationToken ct)
        {
            if (!TryGetSelfId(out _)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > 5 * 1024 * 1024)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported image type: {file.ContentType}. Use PNG, JPEG, or WebP.");

            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SavePlatformAsync(stream, "avatar", ext, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
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
            var user = await _userRepository.GetById(userId);
            if (user is null) return new ApiResponses().NotFoundResult("User not found.");

            // Birthdate drives the minor / parent-guardian waiver requirement, so it is
            // set-once via self-serve. A rider could otherwise sign a waiver as an adult
            // and then flip their DOB to a minor (or vice versa) to dodge the guardian
            // signature. First-time set is allowed; corrections after that go through
            // track staff, who can update it on the rider's behalf.
            if (user.Birthdate.HasValue)
            {
                return new ApiResponses().BadRequestResult(
                    "Your date of birth is already on file and can't be changed here. Contact the track if it needs correcting.");
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
            "tenant_admin", "tenant_manager", "tenant_cashier", "tenant_shop_cashier", "tenant_scanner", "tenant_accountant",
        };

        // Resolve a request that may carry Roles[] (preferred) and/or a single Role into a
        // validated, de-duplicated set plus its derived primary. Returns false with a message
        // if the set is empty or any role isn't assignable.
        private static bool TryResolveRoles(string? singleRole, string[]? roles,
            out string[] resolved, out string primary, out string? error)
        {
            var set = (roles ?? System.Array.Empty<string>())
                .Concat(string.IsNullOrWhiteSpace(singleRole) ? System.Array.Empty<string>() : new[] { singleRole })
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .Distinct()
                .ToArray();
            resolved = set;
            primary = "";
            error = null;
            if (set.Length == 0) { error = "At least one role is required."; return false; }
            foreach (var r in set)
            {
                if (!AssignableRoles.Contains(r)) { error = $"Role '{r}' is not assignable."; return false; }
            }
            primary = TenantPermissions.PrimaryRole(set);
            return true;
        }

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
                Roles = u.Roles,
                Status = u.Status,
                CreatedAtUtc = DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc),
            });
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPost("Tenant")]
        public async Task<IActionResult> CreateTenantUser([FromBody] CreateTenantUserRequest request)
        {
            if (!TryResolveRoles(request.Role, request.Roles, out var newRoles, out var primaryRole, out var roleError))
            {
                return new ApiResponses().BadRequestResult(roleError!);
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
                Role = primaryRole,
                Roles = newRoles,
                Status = "active",
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);
            user.Id = await _userRepository.Create(user);

            if (_emailer.IsConfigured)
            {
                var loginUrl = $"{Request.Scheme}://{Request.Host.Value}/Login";
                var resetUrl = $"{Request.Scheme}://{Request.Host.Value}/ResetPassword";
                // Staff are added BY a track, so the invite comes from that track.
                var tenant = _tenantContext.IsResolved ? _tenantContext.Tenant : null;
                var org = tenant is null ? "RidePass" : Html(tenant.DisplayName);
                var html = $@"<p>Hi {Html(user.FirstName)},</p>
<p>You've been added as a <strong>{Html(string.Join(", ", user.Roles))}</strong> at <strong>{org}</strong>.</p>
<p><strong>Sign in:</strong> <a href=""{loginUrl}"">{loginUrl}</a><br/>
<strong>Email:</strong> {Html(user.Email)}<br/>
<strong>Temporary password:</strong> <code>{tempPassword}</code></p>
<p>Please <a href=""{resetUrl}"">reset your password</a> after your first sign-in.</p>";
                await _emailer.Send(user.Email,
                    tenant is null ? "Welcome to RidePass" : $"Welcome to {tenant.DisplayName}",
                    html, null, TenantEmailIdentity.For(tenant));
            }

            return new ApiResponses().OkResult(new CreateTenantUserResponse
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role,
                Roles = user.Roles,
                TemporaryPassword = tempPassword,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPut("Tenant/{id:guid}/Role")]
        public async Task<IActionResult> UpdateTenantUserRole(Guid id, [FromBody] UpdateTenantUserRoleRequest request)
        {
            if (!TryResolveRoles(request.Role, request.Roles, out var newRoles, out var primaryRole, out var roleError))
            {
                return new ApiResponses().BadRequestResult(roleError!);
            }
            var target = await _userRepository.GetById(id);
            if (target is null || target.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().NotFoundResult("User not found on this tenant.");
            }
            if (SelfId() == id && !newRoles.Contains("tenant_admin"))
            {
                return new ApiResponses().BadRequestResult("You can't remove your own admin role.");
            }
            await _userRepository.UpdateRoles(id, primaryRole, newRoles);
            return new ApiResponses().OkResult(new { id, role = primaryRole, roles = newRoles });
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
                var tenant = _tenantContext.IsResolved ? _tenantContext.Tenant : null;
                var org = tenant is null ? "RidePass" : Html(tenant.DisplayName);
                var html = $@"<p>Hi {Html(target.FirstName)},</p>
<p>An administrator at <strong>{org}</strong> has reset your password.</p>
<p><strong>Sign in:</strong> <a href=""{loginUrl}"">{loginUrl}</a><br/>
<strong>Temporary password:</strong> <code>{tempPassword}</code></p>
<p>Please <a href=""{resetUrl}"">change it</a> after your next sign-in.</p>";
                await _emailer.Send(target.Email,
                    tenant is null ? "Your RidePass password was reset" : $"Your {tenant.DisplayName} password was reset",
                    html, null, TenantEmailIdentity.For(tenant));
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
                    // Name the track the rider actually clicked "forgot password" on. They know
                    // Motoland; a bare "RidePass" reset email is one they'd ignore or report as
                    // phishing. The account itself is still their platform-wide login, which the
                    // body says plainly so a rider isn't surprised it works at other tracks.
                    var tenant = _tenantContext.IsResolved ? _tenantContext.Tenant : null;
                    var who = tenant is null ? "your RidePass account" : Html(tenant.DisplayName);
                    var scopeNote = tenant is null ? string.Empty
                        : "<p style=\"color:#666; font-size:13px\">This is the same account you use at any track on RidePass.</p>";
                    var html = $@"<p>Hi {Html(user.FirstName)},</p>
<p>We received a request to reset the password for your account at <strong>{who}</strong>.</p>
<p><a href=""{resetUrl}"">Click here to set a new password</a>. This link expires in 60 minutes and can only be used once.</p>
<p>If you didn't request this, you can safely ignore this email.</p>
{scopeNote}";
                    await _emailer.Send(user.Email,
                        tenant is null ? "Reset your RidePass password" : $"Reset your {tenant.DisplayName} password",
                        html, null, TenantEmailIdentity.For(tenant));
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

        // ── Email verification ────────────────────────────────────────────────────

        /// <summary>Public: consume a verification token and mark the rider's email verified.</summary>
        [AllowAnonymous]
        [HttpPost("VerifyEmail")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return new ApiResponses().BadRequestResult("Missing verification token.");
            }
            var user = await _userRepository.GetByEmailVerificationTokenHash(HashToken(request.Token.Trim()));
            if (user is null)
            {
                return new ApiResponses().BadRequestResult(
                    "This verification link is invalid or has expired. Request a new one and try again.");
            }
            await _userRepository.MarkEmailVerified(user.Id);
            return new ApiResponses().OkResult(new { message = "Email verified. You can now sign in." });
        }

        /// <summary>
        /// Public: re-send a verification link. Always returns 200 so it never reveals
        /// which addresses have accounts or their verification state.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("ResendVerification")]
        public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
        {
            var email = request.Email?.Trim() ?? string.Empty;
            var user = await _userRepository.GetGlobalByEmail(email);
            if (user is not null && user.Role == "rider" && user.Status == "active"
                && !user.EmailVerified && _emailer.IsConfigured)
            {
                var verifyToken = GenerateResetToken();
                await _userRepository.SetEmailVerificationToken(
                    user.Id, HashToken(verifyToken), DateTime.UtcNow.AddDays(7));
                await SendVerificationEmail(user, verifyToken);
            }
            return new ApiResponses().OkResult(new { message = "If that account needs verification, we've sent a new link." });
        }

        // HTML-escape any value interpolated into an email body.
        private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

        private async Task SendVerificationEmail(User user, string token)
        {
            if (!_emailer.IsConfigured) return;
            // Riders are global, so the link works on whichever host they signed up from.
            var url = $"{Request.Scheme}://{Request.Host.Value}/VerifyEmail?token={Uri.EscapeDataString(token)}";
            // Signed up on a track's site, so the confirmation comes from that track.
            var tenant = _tenantContext.IsResolved ? _tenantContext.Tenant : null;
            var welcome = tenant is null
                ? "Welcome to RidePass!"
                : $"Welcome to {Html(tenant.DisplayName)}!";
            var html = $@"<p>Hi {Html(user.FirstName)},</p>
<p>{welcome} Please confirm your email to activate your account.</p>
<p><a href=""{url}"">Verify my email</a>. This link expires in 7 days and can only be used once.</p>
<p>If you didn't create an account, you can safely ignore this email.</p>";
            await _emailer.Send(user.Email,
                tenant is null ? "Verify your RidePass email" : $"Verify your email for {tenant.DisplayName}",
                html, null, TenantEmailIdentity.For(tenant));
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
