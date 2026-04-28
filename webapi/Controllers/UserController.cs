using System.Security.Claims;
using System.Security.Cryptography;
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
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ITenantContext _tenantContext;
        private readonly IJwtIssuer _jwtIssuer;

        public UserController(
            IUserRepository userRepository,
            IPasswordHasher<User> passwordHasher,
            ITenantContext tenantContext,
            IJwtIssuer jwtIssuer)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tenantContext = tenantContext;
            _jwtIssuer = jwtIssuer;
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

            var user = new User
            {
                TenantId = null,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Role = "rider",
                Status = "active"
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
                user.Status
            });
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
            return new ApiResponses().OkResult(new ResetTenantUserPasswordResponse
            {
                TemporaryPassword = tempPassword,
            });
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
    }
}
