using Microsoft.AspNetCore.Identity;
using Services.Notifications;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;

namespace webapi.Security
{
    // Shared POS-code authorization. One salted hash per person on users.pos_pin_hash serves two
    // jobs: for managers/admins it is the PIN that approves comps, voids and gate overrides
    // (VerifyAsync, which only probes managers); for everyone who works the register it is the
    // staff code that signs them in at a shared terminal (VerifyStaffCodeAsync, Script0298, which
    // probes every counter staff member). Both enforce the same brute-force lockout keyed by the
    // account doing the entering (the terminal), and codes are unique across ALL staff at the
    // tenant so a code always names exactly one person.
    public interface IManagerPinService
    {
        // Verify a manager PIN entered by `requestingUserId`. Resets the failure counter on success;
        // increments and eventually locks out on failure. Managers/admins only.
        Task<ManagerPinVerifyResult> VerifyAsync(Guid tenantId, Guid requestingUserId, string? pin);
        // Verify a staff code entered at the register by `requestingUserId` (the terminal account),
        // against every active counter staff member who holds one. Same lockout.
        Task<ManagerPinVerifyResult> VerifyStaffCodeAsync(Guid tenantId, Guid requestingUserId, string? code);
        // True if no OTHER user at the tenant (any role, active or not) already uses this code.
        Task<bool> IsCodeAvailableAsync(Guid tenantId, Guid excludeUserId, string code);
        Task<bool> HasPinAsync(Guid userId);
    }

    public class ManagerPinVerifyResult
    {
        public bool Authorized { get; set; }
        public Guid? AuthorizedUserId { get; set; }
        public string? AuthorizedName { get; set; }
        // Whether the matched person holds a manager/admin role (comps and voids).
        public bool IsManager { get; set; }
        public bool Locked { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
        public string? Error { get; set; }
    }

    public class ManagerPinService : IManagerPinService
    {
        private const int MaxFailures = 5;
        private const int LockoutMinutes = 15;

        private readonly IUserRepository _users;
        private readonly IPasswordHasher<User> _hasher;
        private readonly INotificationService _notifications;

        public ManagerPinService(IUserRepository users, IPasswordHasher<User> hasher, INotificationService notifications)
        {
            _users = users;
            _hasher = hasher;
            _notifications = notifications;
        }

        public Task<ManagerPinVerifyResult> VerifyAsync(Guid tenantId, Guid requestingUserId, string? pin) =>
            VerifyAgainst(tenantId, requestingUserId, pin, "manager PIN", () => _users.ListTenantManagerPins(tenantId));

        public Task<ManagerPinVerifyResult> VerifyStaffCodeAsync(Guid tenantId, Guid requestingUserId, string? code) =>
            VerifyAgainst(tenantId, requestingUserId, code, "staff code",
                () => _users.ListTenantStaffPins(tenantId, TenantPermissions.CounterStaffRoles));

        private async Task<ManagerPinVerifyResult> VerifyAgainst(Guid tenantId, Guid requestingUserId, string? pin, string noun,
            Func<Task<List<TenantManagerPin>>> candidates)
        {
            var now = DateTime.UtcNow;
            var attempt = await _users.GetPinAttempt(tenantId, requestingUserId);
            if (attempt?.LockedUntilUtc is { } until && until > now)
                return Locked(until);

            if (string.IsNullOrWhiteSpace(pin))
                return new ManagerPinVerifyResult { Authorized = false, Error = $"Enter a {noun}." };

            // Salted hashes can't be queried by value, so each candidate is checked in code.
            var probe = new User();
            foreach (var c in await candidates())
            {
                if (_hasher.VerifyHashedPassword(probe, c.PinHash, pin) != PasswordVerificationResult.Failed)
                {
                    await _users.ResetPinAttempt(tenantId, requestingUserId);
                    return new ManagerPinVerifyResult
                    {
                        Authorized = true,
                        AuthorizedUserId = c.Id,
                        AuthorizedName = $"{c.FirstName} {c.LastName}".Trim(),
                        IsManager = IsManagerOrAdmin(c),
                    };
                }
            }

            // Atomic increment: concurrent wrong guesses each get a distinct, correctly-advanced count
            // rather than all reading the same stale value (which would blunt the lockout).
            var failed = await _users.IncrementPinFailure(tenantId, requestingUserId);
            if (failed >= MaxFailures)
            {
                var lockUntil = now.AddMinutes(LockoutMinutes);
                await _users.UpsertPinAttempt(tenantId, requestingUserId, 0, lockUntil);
                await NotifyLockout(tenantId, requestingUserId);
                return Locked(lockUntil);
            }
            var left = MaxFailures - failed;
            return new ManagerPinVerifyResult
            {
                Authorized = false,
                Error = $"That {noun} wasn't recognized. {left} attempt{(left == 1 ? "" : "s")} left before a temporary lockout.",
            };
        }

        public async Task<bool> IsCodeAvailableAsync(Guid tenantId, Guid excludeUserId, string code)
        {
            // Every holder at the tenant, whatever their role or status: a disabled account that is
            // re-enabled, or a cashier promoted to manager, must not suddenly share a code.
            var probe = new User();
            foreach (var c in await _users.ListTenantStaffPins(tenantId, roles: null, activeOnly: false))
            {
                if (c.Id == excludeUserId) continue;
                if (_hasher.VerifyHashedPassword(probe, c.PinHash, code) != PasswordVerificationResult.Failed)
                    return false;
            }
            return true;
        }

        public Task<bool> HasPinAsync(Guid userId) => _users.HasPosPin(userId);

        private static bool IsManagerOrAdmin(TenantManagerPin c) =>
            TenantPermissions.IsManagerOrAdmin(c.Role, c.Roles);

        private static ManagerPinVerifyResult Locked(DateTime untilUtc)
        {
            var mins = Math.Max(1, (int)Math.Ceiling((untilUtc - DateTime.UtcNow).TotalMinutes));
            return new ManagerPinVerifyResult
            {
                Authorized = false,
                Locked = true,
                LockedUntilUtc = untilUtc,
                Error = $"Too many incorrect PIN attempts. Try again in about {mins} minute{(mins == 1 ? "" : "s")}.",
            };
        }

        private async Task NotifyLockout(Guid tenantId, Guid requestingUserId)
        {
            try
            {
                var who = await _users.GetById(requestingUserId);
                var name = who is null ? "A staff member" : $"{who.FirstName} {who.LastName}".Trim();
                await _notifications.EmitToTenantRoles(tenantId, new[] { "tenant_manager", "tenant_admin" },
                    NotificationKinds.PinLockout, "Manager PIN locked out",
                    $"{name} hit too many incorrect manager-PIN attempts and is locked out for {LockoutMinutes} minutes.",
                    "/Admin/Concessions");
            }
            catch { /* best-effort; never block the verify path */ }
        }
    }
}
