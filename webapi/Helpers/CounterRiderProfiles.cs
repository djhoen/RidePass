using Microsoft.AspNetCore.Identity;
using Services.Helpers;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Riders;
using Services.Waivers;
using webapi.Controllers.API.Data.Counter;
using webapi.Multitenancy;

namespace webapi.Helpers
{
    /// <summary>
    /// A checked "another rider" entry (Script0309): who the rider is, which existing profiles it uses, what
    /// the sale will create or fill. Building one reads only; <see cref="ICounterRiderProfiles.Save"/>
    /// writes it, inside the sale.
    /// </summary>
    public sealed class OtherRiderPlan
    {
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public DateOnly Birthdate { get; init; }
        public int Age { get; init; }
        public bool IsMinor { get; init; }
        /// <summary>The rider's own account write (existing or new), or null (a minor without an email).</summary>
        public RiderAccountWrite? Rider { get; init; }
        public string? RiderEmail { get; init; }
        /// <summary>A minor's guardian account write (existing or new).</summary>
        public RiderAccountWrite? Guardian { get; init; }
        public string? GuardianName { get; init; }
        public string? GuardianEmail { get; init; }
        public string? GuardianPhone { get; init; }
        public Guid? ExistingDependentId { get; init; }
        public string? EmergencyContactName { get; init; }
        public string? EmergencyContactPhone { get; init; }
        public bool MatchedExistingRider { get; init; }
        public bool MatchedExistingGuardian { get; init; }

        public CounterOtherRiderResponse ToDraft() => new()
        {
            RiderUserId = Rider?.ExistingId,
            RiderDependentId = ExistingDependentId,
            FirstName = FirstName,
            LastName = LastName,
            Birthdate = Birthdate,
            Age = Age,
            IsMinor = IsMinor,
            Email = RiderEmail,
            GuardianUserId = Guardian?.ExistingId,
            GuardianName = GuardianName,
            GuardianEmail = GuardianEmail,
            GuardianPhone = GuardianPhone,
            EmergencyContactName = EmergencyContactName,
            EmergencyContactPhone = EmergencyContactPhone,
            MatchedExistingRider = MatchedExistingRider,
            MatchedExistingGuardian = MatchedExistingGuardian,
            WillCreateRider = Rider is { ExistingId: null },
            WillCreateGuardian = Guardian is { ExistingId: null },
            WillCreateDependent = IsMinor && ExistingDependentId is null,
        };
    }

    /// <summary>A planned rider once the sale has written their profiles.</summary>
    public sealed record OtherRiderSaved(OtherRiderPlan Plan, Guid? RiderUserId, Guid? DependentId, Guid? GuardianUserId);

    public interface ICounterRiderProfiles
    {
        /// <summary>Checks an "another rider" entry and matches existing profiles. Reads only.</summary>
        Task<(OtherRiderPlan? Plan, string? Error)> Plan(CounterOtherRiderInput input, User payer);

        /// <summary>Writes the planned profiles of one sale in one transaction (new accounts, empty fields of
        /// existing rider accounts, dependent rows). Same order as <paramref name="plans"/>.</summary>
        Task<List<OtherRiderSaved>> Save(IReadOnlyList<OtherRiderPlan> plans, Guid? staffUserId);
    }

    /// <summary>
    /// The one place a Gate Sale "another rider" profile is decided, shared by the add-to-cart check and the
    /// sale (which runs it again: the cart is never trusted). Rules:
    /// - only rider accounts are reused by email or picked (a staff / admin email is refused, and nothing is
    ///   read back from or written to it);
    /// - a guardian must be 18 or over by the gate's birthdate source (ID-verified, else self-reported),
    ///   else by the birthdate staff enter;
    /// - an existing dependent keeps its name, birthdate and account (an empty one is filled);
    /// - existing accounts only get EMPTY phone / birthdate / emergency contact filled, at sale time;
    /// - when the tenant requires an emergency contact, every rider has one (a minor: their guardian, by default).
    /// </summary>
    public class CounterRiderProfiles : ICounterRiderProfiles
    {
        private const string StaffEmailMessage = "That email belongs to a staff account. Use a different email.";

        private readonly IUserRepository _users;
        private readonly IRiderDependentRepository _dependents;
        private readonly ITenantRepository _tenants;
        private readonly IPasswordHasher<User> _hasher;
        private readonly ITenantContext _tenantContext;

        public CounterRiderProfiles(IUserRepository users, IRiderDependentRepository dependents,
            ITenantRepository tenants, IPasswordHasher<User> hasher, ITenantContext tenantContext)
        {
            _users = users;
            _dependents = dependents;
            _tenants = tenants;
            _hasher = hasher;
            _tenantContext = tenantContext;
        }

        public async Task<(OtherRiderPlan? Plan, string? Error)> Plan(CounterOtherRiderInput input, User payer)
        {
            var tenantId = _tenantContext.TenantId;
            var today = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);
            // Read fresh, not from the 5-minute resolution cache, so a just-changed setting applies.
            var requireEc = (await _tenants.GetById(tenantId))?.RequireEmergencyContact
                ?? _tenantContext.Tenant.RequireEmergencyContact;
            var typedDob = input.Birthdate is { } b ? DateOnly.FromDateTime(b) : (DateOnly?)null;
            var typedGuardianDob = input.GuardianBirthdate is { } gb ? DateOnly.FromDateTime(gb) : (DateOnly?)null;
            var typedEc = Ec(input.EmergencyContactName, input.EmergencyContactPhone);
            if (typedEc.Error is not null) return (null, typedEc.Error);

            // ── A child already linked to a guardian here: the same row, as stored ──────────────────
            if (input.ExistingDependentId is Guid depId)
            {
                var dep = await _dependents.GetById(depId, tenantId);
                if (dep is null) return (null, "That rider isn't on file here. Add them again.");
                var g = BuyerAccounts.ForTenant(await _users.GetById(dep.GuardianUserId), tenantId);
                if (g is null) return (null, "That rider's guardian account is no longer available.");
                var dob = GuardianWaiverRule.ToDateOnly(dep.Birthdate) ?? typedDob;
                if (dob is null)
                    return (null, "Enter the rider's birthdate: it decides whether a parent or guardian has to sign.");
                if (!CounterRiderRules.IsMinor(dob.Value, today))
                    return (null, $"{dep.FirstName} is 18 or over now and signs their own waiver: add them as an adult rider with their own email.");
                var (guardianWrite, guardianError) = GuardianWrite(g, typedGuardianDob, today, matched: true);
                if (guardianError is not null) return (null, guardianError);
                var ec = DependentEc(dep, typedEc, g.FirstName + " " + g.LastName, Phone(g, input.GuardianPhone));
                if (requireEc && ec.Phone is null)
                    return (null, $"This track requires an emergency contact: enter one for {dep.FirstName}, or the guardian's phone.");
                var own = dep.RiderUserId is Guid oid ? BuyerAccounts.ForTenant(await _users.GetById(oid), tenantId) : null;
                return (new OtherRiderPlan
                {
                    FirstName = dep.FirstName, LastName = dep.LastName, Birthdate = dob.Value,
                    Age = GuardianWaiverRule.AgeOn(dob.Value, today), IsMinor = true,
                    Rider = own is null ? null : new RiderAccountWrite { ExistingId = own.Id },
                    RiderEmail = own is { Role: "rider" } ? own.Email : null,
                    Guardian = guardianWrite, GuardianName = $"{g.FirstName} {g.LastName}".Trim(),
                    GuardianEmail = g.Email, GuardianPhone = g.Phone,
                    ExistingDependentId = dep.Id,
                    EmergencyContactName = ec.Name, EmergencyContactPhone = ec.Phone,
                    MatchedExistingRider = own is not null, MatchedExistingGuardian = true,
                }, null);
            }

            // ── The rider: a picked profile, an account with the typed email, or new ───────────────
            var shape = new OtherRiderInput(input.FirstName, input.LastName, typedDob, input.Email, input.Phone,
                input.GuardianIsPayer, input.GuardianUserId is not null,
                input.GuardianFirstName, input.GuardianLastName, input.GuardianEmail, input.GuardianPhone);
            User? account = null;
            if (input.ExistingUserId is Guid existingId)
            {
                account = BuyerAccounts.ForTenant(await _users.GetById(existingId), tenantId);
                if (account is null) return (null, "That profile isn't available here.");
                if (account.Role != "rider") return (null, "That is a staff account. Pick or enter the rider's own profile.");
            }
            else
            {
                var invalid = CounterRiderRules.Validate(shape, today, existingProfile: false, payer.Email);
                if (invalid is not null) return (null, invalid);
                if (!string.IsNullOrWhiteSpace(input.Email))
                {
                    var (found, staff) = await FindRiderByEmail(input.Email);
                    if (staff) return (null, StaffEmailMessage);
                    account = found;
                }
            }
            if (account is not null && account.Id == payer.Id)
                return (null, "That is the paying customer. Add the ticket for them instead of another rider.");

            // An existing account is the rider: its name and (ID-verified, else self-reported) birthdate win.
            var first = account?.FirstName ?? input.FirstName!.Trim();
            var last = account?.LastName ?? input.LastName!.Trim();
            var riderDob = AccountWaiverStatus.Birthdate(account) ?? typedDob;
            var recheck = CounterRiderRules.Validate(shape with { Birthdate = riderDob }, today, existingProfile: true, payer.Email);
            if (recheck is not null) return (null, recheck);
            var isMinor = CounterRiderRules.IsMinor(riderDob!.Value, today);
            var age = GuardianWaiverRule.AgeOn(riderDob.Value, today);

            if (!isMinor)
            {
                // Adult: their own account. Emergency contact: theirs on file, else what staff entered.
                var hasEc = account is not null && RiderAccounts.EmergencyContactError(
                    account.EmergencyContactName, account.EmergencyContactPhone) is null;
                if (requireEc && !hasEc && typedEc.Phone is null)
                    return (null, $"This track requires an emergency contact: enter one for {first}.");
                return (new OtherRiderPlan
                {
                    FirstName = first, LastName = last, Birthdate = riderDob.Value, Age = age, IsMinor = false,
                    Rider = AccountWrite(account, input.Email, first, last, riderDob, input.Phone, typedEc.Name, typedEc.Phone),
                    RiderEmail = account?.Email ?? input.Email!.Trim(),
                    EmergencyContactName = hasEc ? account!.EmergencyContactName : typedEc.Name,
                    EmergencyContactPhone = hasEc ? account!.EmergencyContactPhone : typedEc.Phone,
                    MatchedExistingRider = account is not null,
                }, null);
            }

            // ── A minor: their guardian ─────────────────────────────────────────────────────────────
            User? guardian = null;
            bool guardianMatched;
            if (input.GuardianIsPayer)
            {
                guardian = payer;
                guardianMatched = true;
            }
            else if (input.GuardianUserId is Guid gid)
            {
                guardian = BuyerAccounts.ForTenant(await _users.GetById(gid), tenantId);
                if (guardian is null) return (null, "That guardian profile isn't available here.");
                if (guardian.Role != "rider" && guardian.Id != payer.Id) return (null, StaffEmailMessage);
                guardianMatched = true;
            }
            else
            {
                var (found, staff) = await FindRiderByEmail(input.GuardianEmail!);
                if (staff) return (null, StaffEmailMessage);
                guardian = found;
                guardianMatched = found is not null;
            }
            if (account is not null && guardian is not null && guardian.Id == account.Id)
                return (null, "The guardian can't be the rider. Enter the parent or guardian's own details.");

            RiderAccountWrite guardianWriteNew;
            string guardianName, guardianEmail;
            string? guardianPhone;
            if (guardian is not null)
            {
                var (gw, ge) = GuardianWrite(guardian, typedGuardianDob, today, matched: guardianMatched);
                if (ge is not null) return (null, ge);
                guardianWriteNew = gw!;
                guardianName = $"{guardian.FirstName} {guardian.LastName}".Trim();
                guardianEmail = guardian.Email;
                guardianPhone = Phone(guardian, input.GuardianPhone);
                if (guardian.Role == "rider" && string.IsNullOrWhiteSpace(guardian.Phone) && RiderAccounts.IsValidPhone(input.GuardianPhone))
                    guardianWriteNew = new RiderAccountWrite
                    {
                        ExistingId = guardian.Id, FillBirthdate = gw!.FillBirthdate, FillPhone = input.GuardianPhone!.Trim(),
                    };
            }
            else
            {
                guardianName = $"{input.GuardianFirstName!.Trim()} {input.GuardianLastName!.Trim()}";
                var ageError = CounterRiderRules.GuardianAgeError(guardianName, typedGuardianDob, today);
                if (ageError is not null) return (null, ageError);
                guardianEmail = input.GuardianEmail!.Trim();
                guardianPhone = RiderAccounts.IsValidPhone(input.GuardianPhone) ? input.GuardianPhone!.Trim() : null;
                guardianWriteNew = new RiderAccountWrite
                {
                    NewAccount = RiderAccounts.NewRider(_hasher, guardianEmail, input.GuardianFirstName!, input.GuardianLastName!,
                        typedGuardianDob?.ToDateTime(TimeOnly.MinValue), guardianPhone, null, null),
                };
            }

            // The same child under the same guardian is the same dependent row: it keeps its name, birthdate
            // and account (a new account only fills an empty one), so re-adding never disagrees with the sale.
            var existingDep = guardian is null ? null
                : await _dependents.FindByKey(tenantId, guardian.Id, IdCheckRules.Normalize($"{first} {last}"));
            if (existingDep is not null)
            {
                first = existingDep.FirstName;
                last = existingDep.LastName;
                if (GuardianWaiverRule.ToDateOnly(existingDep.Birthdate) is { } storedDob) riderDob = storedDob;
                if (existingDep.RiderUserId is Guid storedOwn)
                {
                    var own = BuyerAccounts.ForTenant(await _users.GetById(storedOwn), tenantId);
                    account = own;
                }
            }
            var depEc = existingDep is not null
                ? DependentEc(existingDep, typedEc, guardianName, guardianPhone)
                : typedEc.Phone is not null ? typedEc : (Name: (string?)guardianName, Phone: guardianPhone, Error: (string?)null);
            if (requireEc && depEc.Phone is null)
                return (null, $"This track requires an emergency contact: enter one for {first}, or the guardian's phone.");

            return (new OtherRiderPlan
            {
                FirstName = first, LastName = last, Birthdate = riderDob!.Value,
                Age = GuardianWaiverRule.AgeOn(riderDob.Value, today), IsMinor = true,
                // A minor's own account: the dependent's stored one, else a matched / new one from their email.
                Rider = account is not null || !string.IsNullOrWhiteSpace(input.Email)
                    ? AccountWrite(account, input.Email, first, last, riderDob, input.Phone, depEc.Name, depEc.Phone)
                    : null,
                RiderEmail = account?.Email ?? (string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim()),
                Guardian = guardianWriteNew,
                GuardianName = guardianName, GuardianEmail = guardianEmail, GuardianPhone = guardianPhone,
                ExistingDependentId = existingDep?.Id,
                EmergencyContactName = depEc.Name, EmergencyContactPhone = depEc.Phone,
                MatchedExistingRider = account is not null, MatchedExistingGuardian = guardian is not null,
            }, null);
        }

        public async Task<List<OtherRiderSaved>> Save(IReadOnlyList<OtherRiderPlan> plans, Guid? staffUserId)
        {
            if (plans.Count == 0) return new List<OtherRiderSaved>();
            var writes = plans.Select(p => new OtherRiderProfileWrite
            {
                Rider = p.Rider,
                Guardian = p.IsMinor ? p.Guardian : null,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Birthdate = p.Birthdate.ToDateTime(TimeOnly.MinValue),
                EmergencyContactName = p.EmergencyContactName,
                EmergencyContactPhone = p.EmergencyContactPhone,
                CreatedByUserId = staffUserId,
            }).ToList();
            var ids = await _dependents.SaveOtherRiderProfiles(_tenantContext.TenantId, writes);
            return plans.Select((p, i) => new OtherRiderSaved(p, ids[i].RiderUserId, ids[i].DependentId, ids[i].GuardianUserId)).ToList();
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────

        /// <summary>The account for this email usable as a rider here (a platform rider, or this tenant's own
        /// rider), and whether the email belongs to a staff / admin account instead.</summary>
        private async Task<(User? Account, bool Staff)> FindRiderByEmail(string email)
        {
            var e = email.Trim();
            var found = await _users.GetGlobalByEmail(e) ?? await _users.GetByEmail(_tenantContext.TenantId, e);
            if (found is null) return (null, false);
            return found.Role == "rider" ? (found, false) : (null, true);
        }

        /// <summary>An existing account (fill only what is empty, and only on a rider account) or a new one.</summary>
        private RiderAccountWrite AccountWrite(User? account, string? email, string first, string last,
            DateOnly? birthdate, string? phone, string? ecName, string? ecPhone)
        {
            var dob = birthdate?.ToDateTime(TimeOnly.MinValue);
            var usablePhone = RiderAccounts.IsValidPhone(phone) ? phone!.Trim() : null;
            if (account is null)
                return new RiderAccountWrite
                {
                    NewAccount = RiderAccounts.NewRider(_hasher, email!, first, last, dob, usablePhone, ecName, ecPhone),
                };
            if (account.Role != "rider") return new RiderAccountWrite { ExistingId = account.Id };
            return new RiderAccountWrite
            {
                ExistingId = account.Id,
                FillPhone = string.IsNullOrWhiteSpace(account.Phone) ? usablePhone : null,
                FillBirthdate = account.Birthdate is null ? dob : null,
                FillEmergencyContactName = string.IsNullOrWhiteSpace(account.EmergencyContactPhone) ? ecName : null,
                FillEmergencyContactPhone = string.IsNullOrWhiteSpace(account.EmergencyContactPhone) ? ecPhone : null,
            };
        }

        /// <summary>An existing guardian: 18 or over by their account (ID-verified, else self-reported)
        /// birthdate, else by the one staff entered (then filled on a rider account with none).</summary>
        private static (RiderAccountWrite? Write, string? Error) GuardianWrite(User guardian, DateOnly? typed,
            DateOnly today, bool matched)
        {
            var onFile = AccountWaiverStatus.Birthdate(guardian);
            var name = $"{guardian.FirstName} {guardian.LastName}".Trim();
            var error = CounterRiderRules.GuardianAgeError(name, onFile ?? typed, today);
            if (error is not null) return (null, error);
            return (new RiderAccountWrite
            {
                ExistingId = guardian.Id,
                FillBirthdate = onFile is null && guardian.Role == "rider" ? typed?.ToDateTime(TimeOnly.MinValue) : null,
            }, null);
        }

        private static string? Phone(User guardian, string? typed) =>
            !string.IsNullOrWhiteSpace(guardian.Phone) ? guardian.Phone
            : RiderAccounts.IsValidPhone(typed) ? typed!.Trim() : null;

        private static (string? Name, string? Phone, string? Error) Ec(string? name, string? phone)
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone)) return (null, null, null);
            var error = RiderAccounts.EmergencyContactError(name, phone);
            return error is not null ? (null, null, error) : (name!.Trim(), phone!.Trim(), null);
        }

        /// <summary>A dependent's emergency contact: the one on file, else what staff entered, else the guardian.</summary>
        private static (string? Name, string? Phone, string? Error) DependentEc(RiderDependent dep,
            (string? Name, string? Phone, string? Error) typed, string guardianName, string? guardianPhone)
        {
            if (!string.IsNullOrWhiteSpace(dep.EmergencyContactPhone)) return (dep.EmergencyContactName, dep.EmergencyContactPhone, null);
            if (typed.Phone is not null) return typed;
            return (guardianPhone is null ? null : guardianName.Trim(), guardianPhone, null);
        }
    }
}
