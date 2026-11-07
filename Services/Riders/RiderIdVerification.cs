using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Riders
{
    /// <summary>
    /// Single source of truth for "has this rider's ID and age been verified?". The gate screen
    /// that DISPLAYS the tick and the wristband gate that ENFORCES it both come through here, so
    /// they can never disagree about who counts as verified.
    ///
    /// Why the answer isn't simply a column on users: the account that bought a credential is not
    /// necessarily the person it admits. SeasonPassCheckInContext says it outright: "a parent
    /// buys passes for their kids". The admitted person lives in the holder_*/rider_* columns on
    /// the credential, often with no account at all. So a check is stored on the credential and,
    /// when there is a buying account, under that account FOR THAT RIDER (rider_id_verification,
    /// keyed by the normalized rider name), and resolved here.
    /// </summary>
    public interface IRiderIdVerification
    {
        Task<RiderIdVerificationStatus> StatusForPass(SeasonPassPurchase pass, Guid tenantId);
        Task<RiderIdVerificationStatus> StatusForTicket(EventTicketPurchase ticket, Guid tenantId);

        /// <summary>Records a staff ID check against a pass holder. Returns the resulting status,
        /// whose Scope tells the caller whether it stuck to the rider's account or only this pass.</summary>
        Task<RiderIdVerificationStatus> RecordForPass(SeasonPassPurchase pass, Guid tenantId,
            Guid? staffUserId, DateTime? verifiedDob);

        Task<RiderIdVerificationStatus> RecordForTicket(EventTicketPurchase ticket, Guid tenantId,
            Guid? staffUserId, DateTime? verifiedDob);

        /// <summary>One rider's tickets (the anchor first, then e.g. the rest of their registrant
        /// group): every paid / redeemed one stamped in one statement, the account's row for that
        /// rider written once.</summary>
        Task<RiderIdVerificationStatus> RecordForRiderTickets(EventTicketPurchase anchor,
            IReadOnlyCollection<Guid> ticketIds, Guid tenantId, Guid? staffUserId, DateTime? verifiedDob);

        /// <summary>Undoes a verification recorded in error, clearing BOTH the credential and the
        /// account it may have propagated to. Clearing only one would leave the other still
        /// answering "verified", which is the whole failure this class exists to prevent.</summary>
        Task ClearForPass(SeasonPassPurchase pass, Guid tenantId);
    }

    /// <summary>Where a verification is recorded, which is what the UI wording turns on.</summary>
    public static class IdVerificationScope
    {
        /// <summary>Nothing recorded.</summary>
        public const string None = "none";
        /// <summary>On the rider's account: carries to every future purchase they make.</summary>
        public const string Rider = "rider";
        /// <summary>On this credential only, because the holder has no account of their own.</summary>
        public const string Credential = "credential";
    }

    /// <summary>
    /// The yearly birthdate check (tenant.birthdate_verify_mode / birthdate_verify_valid_days).
    /// A verification is "current" when it was recorded on a tenant-local day within
    /// <c>validDays</c> of the check-in day: recorded today is day 0, and it lapses on day
    /// <c>validDays</c> (so 365 means "once a year": a check from exactly a year ago is due again).
    /// A verification dated after the check-in day can't be trusted (clock or data error) and is
    /// not current, so a 'required' tenant fails closed.
    /// </summary>
    public static class IdVerificationWindow
    {
        public const string ModeOff = "off";
        public const string ModePrompt = "prompt";
        public const string ModeRequired = "required";

        public static bool IsValidMode(string? mode) => mode is ModeOff or ModePrompt or ModeRequired;

        /// <summary>The tenant-local calendar day a verification was recorded on.</summary>
        public static DateOnly? VerifiedLocalDate(DateTime? verifiedAtUtc, TimeZoneInfo tenantZone) =>
            verifiedAtUtc is { } at
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), tenantZone))
                : null;

        public static bool IsCurrent(DateTime? verifiedAtUtc, DateOnly checkInDate, int validDays, TimeZoneInfo tenantZone)
        {
            if (VerifiedLocalDate(verifiedAtUtc, tenantZone) is not { } day) return false;
            var elapsed = checkInDate.DayNumber - day.DayNumber;
            return elapsed >= 0 && elapsed < Math.Max(1, validDays);
        }
    }

    /// <summary>One recorded ID check: when, by whom, what DOB the document showed, where it lives.</summary>
    public sealed record IdCheck(DateTime VerifiedAtUtc, Guid? VerifiedByUserId, DateTime? VerifiedDob, string Scope);

    /// <summary>
    /// Pure rules shared by RiderIdVerification and the waiver gate: whose check a stored row is
    /// (the rider key), and which of several stored checks counts (the most recent one, so a fresh
    /// check on the credential is never hidden by an older one on the account, or vice versa; its
    /// DOB is the age evidence).
    /// </summary>
    public static class IdCheckRules
    {
        /// <summary>Lowercased, inner whitespace collapsed: "Sam  Boyd" and "sam boyd" are one person.</summary>
        public static string Normalize(string? name) =>
            string.Join(' ', (name ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToLowerInvariant();

        /// <summary>
        /// The rider a credential admits, as a key under its buying account: the credential's
        /// rider / holder name, or, when it names nobody, the account owner's own name.
        /// </summary>
        public static string RiderKey(string? riderFirst, string? riderLast, string? accountFirst, string? accountLast)
        {
            var rider = Normalize($"{riderFirst} {riderLast}");
            return rider.Length > 0 ? rider : Normalize($"{accountFirst} {accountLast}");
        }

        /// <summary>The account IS the rider: the credential names nobody, or names the account owner.</summary>
        public static bool AccountIsRider(string? riderFirst, string? riderLast, string? accountFirst, string? accountLast)
        {
            var rider = Normalize($"{riderFirst} {riderLast}");
            return rider.Length == 0 || rider == Normalize($"{accountFirst} {accountLast}");
        }

        /// <summary>The most recently recorded of the given checks (nulls ignored), or null.</summary>
        public static IdCheck? Latest(params IdCheck?[] checks) =>
            checks.Where(c => c is not null).OrderByDescending(c => c!.VerifiedAtUtc).FirstOrDefault();
    }

    public class RiderIdVerificationStatus
    {
        public bool Verified { get; init; }
        public DateTime? VerifiedAtUtc { get; init; }
        public Guid? VerifiedByUserId { get; init; }
        public string? VerifiedByName { get; init; }
        /// <summary>The date of birth read off the document, which is the age evidence.</summary>
        public DateTime? VerifiedDob { get; init; }
        public string Scope { get; init; } = IdVerificationScope.None;

        /// <summary>Age from the VERIFIED date of birth, never the self-reported one. Null when
        /// unverified, so a caller can't accidentally present a typed-in age as a checked one.</summary>
        public int? VerifiedAge => VerifiedDob is null ? null : AgeOn(VerifiedDob.Value, DateTime.UtcNow);

        internal static int AgeOn(DateTime dob, DateTime asOf)
        {
            var age = asOf.Year - dob.Year;
            if (asOf.Date < dob.Date.AddYears(age)) age--;
            return age < 0 ? 0 : age;
        }

        public static readonly RiderIdVerificationStatus Unverified = new();
    }

    public class RiderIdVerification : IRiderIdVerification
    {
        private readonly IUserRepository _users;
        private readonly ISeasonPassRepository _passes;
        private readonly IEventTicketPurchaseRepository _tickets;

        public RiderIdVerification(IUserRepository users, ISeasonPassRepository passes,
            IEventTicketPurchaseRepository tickets)
        {
            _users = users;
            _passes = passes;
            _tickets = tickets;
        }

        // ── Reading ──────────────────────────────────────────────────────────────
        // Candidates: the account's row for THIS rider (any account: a parent's row for their
        // child counts for that child), the legacy users.id_verified_* stamp of a tenant's own
        // account when that account is the rider, and the credential's own stamp. The most
        // recently recorded one wins.

        public async Task<RiderIdVerificationStatus> StatusForPass(SeasonPassPurchase pass, Guid tenantId)
        {
            var account = await BuyerAccount(pass.PurchaserUserId, tenantId);
            return await Resolve(tenantId, account, pass.HolderFirstName, pass.HolderLastName,
                pass.IdVerifiedAt, pass.IdVerifiedByUserId, pass.IdVerifiedDob);
        }

        public async Task<RiderIdVerificationStatus> StatusForTicket(EventTicketPurchase ticket, Guid tenantId)
        {
            var account = ticket.PurchaserUserId is Guid id ? await BuyerAccount(id, tenantId) : null;
            return await Resolve(tenantId, account, ticket.RiderFirstName, ticket.RiderLastName,
                ticket.IdVerifiedAt, ticket.IdVerifiedByUserId, ticket.IdVerifiedDob);
        }

        private async Task<RiderIdVerificationStatus> Resolve(Guid tenantId, User? account,
            string? riderFirst, string? riderLast,
            DateTime? credentialAt, Guid? credentialBy, DateTime? credentialDob)
        {
            IdCheck? row = null, legacy = null;
            if (account is not null)
            {
                var key = IdCheckRules.RiderKey(riderFirst, riderLast, account.FirstName, account.LastName);
                if (await _users.GetRiderIdVerification(account.Id, tenantId, key) is { } r)
                    row = new IdCheck(DateTime.SpecifyKind(r.VerifiedAt, DateTimeKind.Utc), r.VerifiedByUserId,
                        r.VerifiedDob, IdVerificationScope.Rider);
                if (account.TenantId == tenantId && account.IdVerifiedAt is { } la
                    && IdCheckRules.AccountIsRider(riderFirst, riderLast, account.FirstName, account.LastName))
                    legacy = new IdCheck(DateTime.SpecifyKind(la, DateTimeKind.Utc), account.IdVerifiedByUserId,
                        account.IdVerifiedDob, IdVerificationScope.Rider);
            }
            var credential = credentialAt is { } ca
                ? new IdCheck(DateTime.SpecifyKind(ca, DateTimeKind.Utc), credentialBy, credentialDob, IdVerificationScope.Credential)
                : null;

            var best = IdCheckRules.Latest(row, legacy, credential);
            if (best is null) return RiderIdVerificationStatus.Unverified;
            return new RiderIdVerificationStatus
            {
                Verified = true,
                VerifiedAtUtc = best.VerifiedAtUtc,
                VerifiedByUserId = best.VerifiedByUserId,
                VerifiedByName = await NameOf(best.VerifiedByUserId, tenantId),
                VerifiedDob = best.VerifiedDob,
                Scope = best.Scope,
            };
        }

        // ── Writing ──────────────────────────────────────────────────────────────
        // The credential is always stamped. The buying account gets a row FOR THIS RIDER, so a
        // parent's own check and their child's never stand in for each other; a tenant's own
        // account that IS the rider is also stamped on users, as before.

        public async Task<RiderIdVerificationStatus> RecordForPass(SeasonPassPurchase pass, Guid tenantId,
            Guid? staffUserId, DateTime? verifiedDob)
        {
            // The credential write is guarded (tenant-scoped, paid only), so it can come back
            // having changed nothing, because the pass was refunded between the caller's check and this
            // write. Stop there rather than stamping the account and reporting a success the
            // credential doesn't actually carry.
            var stamped = await _passes.SetIdVerified(pass.Id, tenantId, staffUserId, verifiedDob);
            if (stamped == 0) return RiderIdVerificationStatus.Unverified;

            var account = await BuyerAccount(pass.PurchaserUserId, tenantId);
            return await StampAccountAndReport(account, tenantId, pass.HolderFirstName, pass.HolderLastName,
                staffUserId, verifiedDob);
        }

        public Task<RiderIdVerificationStatus> RecordForTicket(EventTicketPurchase ticket, Guid tenantId,
            Guid? staffUserId, DateTime? verifiedDob) =>
            RecordForRiderTickets(ticket, new[] { ticket.Id }, tenantId, staffUserId, verifiedDob);

        public async Task<RiderIdVerificationStatus> RecordForRiderTickets(EventTicketPurchase anchor,
            IReadOnlyCollection<Guid> ticketIds, Guid tenantId, Guid? staffUserId, DateTime? verifiedDob)
        {
            var ids = ticketIds.Contains(anchor.Id) ? ticketIds : ticketIds.Append(anchor.Id).ToArray();
            var stamped = await _tickets.SetIdVerifiedMany(ids, tenantId, staffUserId, verifiedDob);
            if (stamped == 0) return RiderIdVerificationStatus.Unverified;

            var account = anchor.PurchaserUserId is Guid id ? await BuyerAccount(id, tenantId) : null;
            return await StampAccountAndReport(account, tenantId, anchor.RiderFirstName, anchor.RiderLastName,
                staffUserId, verifiedDob);
        }

        private async Task<RiderIdVerificationStatus> StampAccountAndReport(User? account, Guid tenantId,
            string? riderFirst, string? riderLast, Guid? staffUserId, DateTime? verifiedDob)
        {
            if (account is not null)
            {
                var key = IdCheckRules.RiderKey(riderFirst, riderLast, account.FirstName, account.LastName);
                await _users.SetRiderIdVerification(account.Id, tenantId, key, staffUserId, verifiedDob);
                if (account.TenantId == tenantId
                    && IdCheckRules.AccountIsRider(riderFirst, riderLast, account.FirstName, account.LastName))
                    await _users.SetIdVerified(account.Id, tenantId, staffUserId, verifiedDob);
            }
            return new RiderIdVerificationStatus
            {
                Verified = true,
                VerifiedAtUtc = DateTime.UtcNow,
                VerifiedByUserId = staffUserId,
                VerifiedByName = await NameOf(staffUserId, tenantId),
                VerifiedDob = verifiedDob,
                Scope = account is not null ? IdVerificationScope.Rider : IdVerificationScope.Credential,
            };
        }

        public async Task ClearForPass(SeasonPassPurchase pass, Guid tenantId)
        {
            await _passes.ClearIdVerified(pass.Id, tenantId);
            var account = await BuyerAccount(pass.PurchaserUserId, tenantId);
            if (account is not null)
            {
                await _users.ClearRiderIdVerification(account.Id, tenantId,
                    IdCheckRules.RiderKey(pass.HolderFirstName, pass.HolderLastName, account.FirstName, account.LastName));
                if (account.TenantId == tenantId
                    && IdCheckRules.AccountIsRider(pass.HolderFirstName, pass.HolderLastName, account.FirstName, account.LastName))
                    await _users.ClearIdVerified(account.Id, tenantId);
            }
        }

        // ── Identity resolution ──────────────────────────────────────────────────

        /// <summary>The buying account when it may carry this tenant's checks: a platform-wide rider
        /// account (no tenant) or this tenant's own account, never another tenant's.</summary>
        private async Task<User?> BuyerAccount(Guid userId, Guid tenantId)
        {
            // The shared rule (BuyerAccounts.ForTenant), as the gate and the photo recorder use it.
            if (userId == Guid.Empty) return null;
            return BuyerAccounts.ForTenant(await _users.GetById(userId), tenantId);
        }

        private async Task<string?> NameOf(Guid? userId, Guid tenantId)
        {
            if (userId is not Guid id) return null;
            var u = await _users.GetById(id);
            if (u is null || u.TenantId != tenantId) return null;
            return $"{u.FirstName} {u.LastName}".Trim();
        }
    }
}
