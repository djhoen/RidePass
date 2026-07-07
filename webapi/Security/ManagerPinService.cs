using Microsoft.AspNetCore.Identity;
using Services.Notifications;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace webapi.Security
{
    // Shared manager-PIN authorization used by both the F&B POS and the gate (admission) overrides, so a
    // single per-manager PIN identifies who authorized any gated action. Salted PIN hashes live on
    // users.pos_pin_hash; this verifies an entered PIN against the tenant's managers/admins and enforces a
    // brute-force lockout keyed by the staff member doing the entering.
    public interface IManagerPinService
    {
        // Verify a PIN entered by `requestingUserId`. Resets the failure counter on success; increments and
        // eventually locks out on failure.
        Task<ManagerPinVerifyResult> VerifyAsync(Guid tenantId, Guid requestingUserId, string? pin);
        // True if no OTHER manager at the tenant already uses this PIN (so attribution stays unambiguous).
        Task<bool> IsPinAvailableAsync(Guid tenantId, Guid excludeUserId, string pin);
        Task<bool> HasPinAsync(Guid userId);
    }

    public class ManagerPinVerifyResult
    {
        public bool Authorized { get; set; }
        public Guid? AuthorizedUserId { get; set; }
        public string? AuthorizedName { get; set; }
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

        public async Task<ManagerPinVerifyResult> VerifyAsync(Guid tenantId, Guid requestingUserId, string? pin)
        {
            var now = DateTime.UtcNow;
            var attempt = await _users.GetPinAttempt(tenantId, requestingUserId);
            if (attempt?.LockedUntilUtc is { } until && until > now)
                return Locked(until);

            if (string.IsNullOrWhiteSpace(pin))
                return new ManagerPinVerifyResult { Authorized = false, Error = "Enter a manager PIN." };

            // Salted hashes can't be queried by value, so each candidate manager is checked in code.
            var probe = new User();
            foreach (var c in await _users.ListTenantManagerPins(tenantId))
            {
                if (_hasher.VerifyHashedPassword(probe, c.PinHash, pin) != PasswordVerificationResult.Failed)
                {
                    await _users.ResetPinAttempt(tenantId, requestingUserId);
                    return new ManagerPinVerifyResult
                    {
                        Authorized = true,
                        AuthorizedUserId = c.Id,
                        AuthorizedName = $"{c.FirstName} {c.LastName}".Trim(),
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
                Error = $"That manager PIN wasn't recognized. {left} attempt{(left == 1 ? "" : "s")} left before a temporary lockout.",
            };
        }

        public async Task<bool> IsPinAvailableAsync(Guid tenantId, Guid excludeUserId, string pin)
        {
            var probe = new User();
            foreach (var c in await _users.ListTenantManagerPins(tenantId))
            {
                if (c.Id == excludeUserId) continue;
                if (_hasher.VerifyHashedPassword(probe, c.PinHash, pin) != PasswordVerificationResult.Failed)
                    return false;
            }
            return true;
        }

        public Task<bool> HasPinAsync(Guid userId) => _users.HasPosPin(userId);

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
