using Services.Repositories.Data.PaymentData;
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
    /// the credential, often with no account at all. So verification is stored in both places and
    /// resolved here.
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
        // The account wins when it carries a verification, because it is the durable record: a
        // rider verified last season is still verified on a pass bought today. The credential is
        // the fallback for a holder who has no account.

        public async Task<RiderIdVerificationStatus> StatusForPass(SeasonPassPurchase pass, Guid tenantId) =>
            await Resolve(tenantId, await AccountForHolder(pass, tenantId),
                pass.IdVerifiedAt, pass.IdVerifiedByUserId, pass.IdVerifiedDob);

        public async Task<RiderIdVerificationStatus> StatusForTicket(EventTicketPurchase ticket, Guid tenantId) =>
            await Resolve(tenantId, await AccountForRider(ticket, tenantId),
                ticket.IdVerifiedAt, ticket.IdVerifiedByUserId, ticket.IdVerifiedDob);

        private async Task<RiderIdVerificationStatus> Resolve(Guid tenantId,
            Services.Repositories.Data.UserData.User? account,
            DateTime? credentialAt, Guid? credentialBy, DateTime? credentialDob)
        {
            // A platform-wide rider account's check is this tenant's rider_id_verification row
            // (Script0305); a tenant's own (staff) account carries it on users, as before.
            if (account is not null && account.TenantId is null
                && await _users.GetRiderIdVerification(account.Id, tenantId) is { } riderCheck)
            {
                return new RiderIdVerificationStatus
                {
                    Verified = true,
                    VerifiedAtUtc = DateTime.SpecifyKind(riderCheck.VerifiedAt, DateTimeKind.Utc),
                    VerifiedByUserId = riderCheck.VerifiedByUserId,
                    VerifiedByName = await NameOf(riderCheck.VerifiedByUserId, tenantId),
                    VerifiedDob = riderCheck.VerifiedDob,
                    Scope = IdVerificationScope.Rider,
                };
            }
            if (account is not null && account.TenantId == tenantId && account.IdVerifiedAt is not null)
            {
                return new RiderIdVerificationStatus
                {
                    Verified = true,
                    VerifiedAtUtc = DateTime.SpecifyKind(account.IdVerifiedAt.Value, DateTimeKind.Utc),
                    VerifiedByUserId = account.IdVerifiedByUserId,
                    VerifiedByName = await NameOf(account.IdVerifiedByUserId, tenantId),
                    VerifiedDob = account.IdVerifiedDob,
                    Scope = IdVerificationScope.Rider,
                };
            }
            if (credentialAt is not null)
            {
                return new RiderIdVerificationStatus
                {
                    Verified = true,
                    VerifiedAtUtc = DateTime.SpecifyKind(credentialAt.Value, DateTimeKind.Utc),
                    VerifiedByUserId = credentialBy,
                    VerifiedByName = await NameOf(credentialBy, tenantId),
                    VerifiedDob = credentialDob,
                    Scope = IdVerificationScope.Credential,
                };
            }
            return RiderIdVerificationStatus.Unverified;
        }

        // ── Writing ──────────────────────────────────────────────────────────────
        // The credential is always stamped. The account is stamped too ONLY when the buyer is
        // demonstrably the holder, so a parent's account never inherits their child's ID check.

        public async Task<RiderIdVerificationStatus> RecordForPass(SeasonPassPurchase pass, Guid tenantId,
            Guid? staffUserId, DateTime? verifiedDob)
        {
            // The credential write is guarded (tenant-scoped, paid only), so it can come back
            // having changed nothing, because the pass was refunded between the caller's check and this
            // write. Stop there rather than stamping the account and reporting a success the
            // credential doesn't actually carry.
            var stamped = await _passes.SetIdVerified(pass.Id, tenantId, staffUserId, verifiedDob);
            if (stamped == 0) return RiderIdVerificationStatus.Unverified;

            var account = await AccountForHolder(pass, tenantId);
            if (account is not null) await StampAccount(account, tenantId, staffUserId, verifiedDob);
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

        public async Task<RiderIdVerificationStatus> RecordForTicket(EventTicketPurchase ticket, Guid tenantId,
            Guid? staffUserId, DateTime? verifiedDob)
        {
            var stamped = await _tickets.SetIdVerified(ticket.Id, tenantId, staffUserId, verifiedDob);
            if (stamped == 0) return RiderIdVerificationStatus.Unverified;

            var account = await AccountForRider(ticket, tenantId);
            if (account is not null) await StampAccount(account, tenantId, staffUserId, verifiedDob);
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
            var account = await AccountForHolder(pass, tenantId);
            if (account is not null)
            {
                await _users.ClearIdVerified(account.Id, tenantId);
                await _users.ClearRiderIdVerification(account.Id, tenantId);
            }
        }

        // A platform-wide rider account (users.tenant_id IS NULL) is stamped in this tenant's
        // rider_id_verification row; a tenant's own account on users, as before.
        private async Task StampAccount(Services.Repositories.Data.UserData.User account, Guid tenantId,
            Guid? staffUserId, DateTime? verifiedDob)
        {
            if (account.TenantId is null)
                await _users.SetRiderIdVerification(account.Id, tenantId, staffUserId, verifiedDob);
            else
                await _users.SetIdVerified(account.Id, tenantId, staffUserId, verifiedDob);
        }

        // ── Identity resolution ──────────────────────────────────────────────────

        /// <summary>
        /// The account that IS this pass's holder, or null when the holder is someone else (or
        /// nobody with an account). Two ways to qualify: the pass carries no distinct holder name,
        /// so the buyer is the rider by default; or the recorded holder name matches the account's
        /// own name. Compared case- and whitespace-insensitively, per the repo's rule that
        /// user-entered strings are never matched case-sensitively.
        /// </summary>
        private async Task<Services.Repositories.Data.UserData.User?> AccountForHolder(
            SeasonPassPurchase pass, Guid tenantId)
        {
            // A pass always has a buyer account (the column is NOT NULL), unlike a ticket.
            var buyer = await _users.GetById(pass.PurchaserUserId);
            // A platform-wide rider account (no tenant) or this tenant's own account; never another tenant's.
            if (buyer is null || (buyer.TenantId is not null && buyer.TenantId != tenantId)) return null;

            var holder = Normalize($"{pass.HolderFirstName} {pass.HolderLastName}");
            if (holder.Length == 0) return buyer;   // never registered a distinct holder
            return holder == Normalize($"{buyer.FirstName} {buyer.LastName}") ? buyer : null;
        }

        private async Task<Services.Repositories.Data.UserData.User?> AccountForRider(
            EventTicketPurchase ticket, Guid tenantId)
        {
            if (ticket.PurchaserUserId is not Guid buyerId) return null;
            var buyer = await _users.GetById(buyerId);
            // A platform-wide rider account (no tenant) or this tenant's own account; never another tenant's.
            if (buyer is null || (buyer.TenantId is not null && buyer.TenantId != tenantId)) return null;

            var rider = Normalize($"{ticket.RiderFirstName} {ticket.RiderLastName}");
            if (rider.Length == 0) return buyer;
            return rider == Normalize($"{buyer.FirstName} {buyer.LastName}") ? buyer : null;
        }

        /// <summary>Collapses inner whitespace and lowercases, so "Sam  Boyd" and "sam boyd" are
        /// the same person and a null half-name doesn't produce a stray space.</summary>
        private static string Normalize(string? name) =>
            string.Join(' ', (name ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToLowerInvariant();

        private async Task<string?> NameOf(Guid? userId, Guid tenantId)
        {
            if (userId is not Guid id) return null;
            var u = await _users.GetById(id);
            if (u is null || u.TenantId != tenantId) return null;
            return $"{u.FirstName} {u.LastName}".Trim();
        }
    }
}
