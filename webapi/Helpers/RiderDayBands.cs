using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.WristbandData;
using Services.Repositories.Interfaces;
using Services.Riders;
using Services.Waivers;
using webapi.Multitenancy;

namespace webapi.Helpers
{
    /// <summary>What a band request comes to, before anything is written (pure; see RiderDayBandRules.Decide).</summary>
    public enum DayBandAction
    {
        /// <summary>The rider already wears this very band that day: nothing to write (link the admission).</summary>
        AlreadyWorn,
        Issue,
        Replace,
        /// <summary>The rider already wears another band that day and this isn't a replacement.</summary>
        RiderHasBand,
        /// <summary>The code is on another rider that day.</summary>
        CodeTaken,
        /// <summary>"Replace" with the band they already wear.</summary>
        ReplaceWithSameBand,
    }

    public static class RiderDayBandRules
    {
        public const int MaxCodeLength = 120;
        public const string CorrectedAtGate = "corrected at the gate";

        /// <summary>A scanned / typed band code, trimmed; null when empty or too long.</summary>
        public static string? CleanCode(string? code)
        {
            var c = code?.Trim();
            return string.IsNullOrEmpty(c) || c.Length > MaxCodeLength ? null : c;
        }

        /// <summary>
        /// One band per rider per day and one rider per band per day. <paramref name="current"/> is the rider's
        /// active band that day, <paramref name="codeHolder"/> the active band with this code that day (anyone's).
        /// </summary>
        public static DayBandAction Decide(RiderDayBand? current, RiderDayBand? codeHolder, string code, bool replacing)
        {
            if (current is not null && string.Equals(current.Code, code, StringComparison.OrdinalIgnoreCase))
                return replacing ? DayBandAction.ReplaceWithSameBand : DayBandAction.AlreadyWorn;
            if (codeHolder is not null && codeHolder.Id != current?.Id) return DayBandAction.CodeTaken;
            if (current is null) return DayBandAction.Issue;
            return replacing ? DayBandAction.Replace : DayBandAction.RiderHasBand;
        }
    }

    /// <summary>Who has a band code on a date, across both stores: a rider's day band, else an older
    /// event_wristband band live that day (the same window a scan resolves with).</summary>
    public sealed record BandCodeUse(RiderDayBand? DayBand, WristbandResolution? Legacy)
    {
        public string HolderName => DayBand?.RiderName
            ?? (Legacy is { } l ? ($"{l.RiderFirstName} {l.RiderLastName}".Trim() is { Length: > 0 } n ? n : l.PurchaserName) : "someone");
    }

    /// <summary>
    /// Daily wristbands for riders (Script0312): a band goes on a RIDER for a tenant business date and covers
    /// every admission they have that day. The rider is keyed as the gate keys their photo and ID check
    /// (IRiderPhotos.KeyForPass now; KeyForTicket in item 10), so the same rider is the same band whichever
    /// credential admits them. Every band passes the same checks (Check): wristbands on, the gate clear for the
    /// rider (waiver / ID check / photo, as admission judges them), a verified ID when the track requires it for
    /// a band, one band per rider per day, and one rider per band code per day across both stores (CodeInUse).
    /// The write (Write) only writes what a passed check decided.
    /// </summary>
    public interface IRiderDayBands
    {
        /// <summary>The tenant's business date now.</summary>
        DateOnly Today();
        Task<RiderPhotoKey> RiderForPass(SeasonPassPurchase pass);
        /// <summary>The pass holder's active band today, if any.</summary>
        Task<RiderDayBand?> TodayForPass(SeasonPassPurchase pass);

        /// <summary>Who has this band code on that date (a day band, else an older band live that day), or null.</summary>
        Task<BandCodeUse?> CodeInUse(string code, DateOnly date);

        /// <summary>
        /// Checks a band for this pass holder on <paramref name="businessDate"/> (default today) without writing.
        /// <paramref name="decided"/>: the gate decision the caller already made for this admission (not re-run).
        /// <paramref name="correcting"/>: the admission being fixed in a band dialog; a different band on a rider
        /// whose band covers that admission is a replacement "corrected at the gate".
        /// </summary>
        Task<RiderDayBandResult> Check(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId, string? code,
            string? replaceReason, DateOnly? businessDate = null, WaiverGateDecision? decided = null, Guid? correcting = null);

        /// <summary>Writes what a passed <see cref="Check"/> decided and ties the admission to the band. No checks
        /// of its own; a race lost since the check (a unique index) comes back as an error.</summary>
        Task<RiderDayBandResult> Write(RiderDayBandResult check, SeasonPassPurchase pass, Guid reservationId, Guid? staffUserId);

        /// <summary>Check, then Write.</summary>
        Task<RiderDayBandResult> IssueForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid reservationId,
            Guid? eventId, string? code, string? replaceReason, Guid? staffUserId, DateOnly? businessDate = null,
            Guid? correcting = null);

        /// <summary>
        /// The day band at a check-in, for every path that checks a pass in: the band the holder wears today, and
        /// whether the track's "wristband required at check-in" refuses this admission (no band worn, none
        /// scanned with it). <paramref name="scanned"/>: a passed band check for a band scanned with the admission.
        /// </summary>
        Task<RiderDayBandCheckIn> ForCheckIn(SeasonPassPurchase? pass, RiderDayBandResult? scanned = null);

        /// <summary>After the check-in: the band the holder already wears covers this admission too.</summary>
        Task CompleteCheckIn(RiderDayBandCheckIn checkIn, Guid reservationId, Guid? staffUserId);

        /// <summary>Takes one admission off its band (the band stays worn). Returns rows removed.</summary>
        Task<int> UnlinkAdmission(Guid reservationId);
        /// <summary>Active band codes covering these reservations.</summary>
        Task<Dictionary<Guid, string>> CodesForReservations(IEnumerable<Guid> reservationIds);
        /// <summary>The admissions a band covers.</summary>
        Task<List<RiderDayBandAdmissionRow>> Admissions(Guid bandId);
        /// <summary>The tenant-local wall-clock time a band was issued (for "issued 9:12").</summary>
        string IssuedTime(RiderDayBand band);
        /// <summary>A band check-in completes only with a band (wristbands on and the setting on).</summary>
        bool RequiredAtCheckin { get; }
    }

    public class RiderDayBands : IRiderDayBands
    {
        private readonly IRiderDayBandRepository _bands;
        private readonly IWristbandRepository _legacy;
        private readonly IRiderPhotos _photos;
        private readonly IWaiverCheckInGate _gate;
        private readonly IRiderIdVerification _idVerification;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public RiderDayBands(IRiderDayBandRepository bands, IWristbandRepository legacy, IRiderPhotos photos,
            IWaiverCheckInGate gate, IRiderIdVerification idVerification, Services.Audit.IAuditLogger audit,
            ITenantContext tenantContext)
        {
            _bands = bands;
            _legacy = legacy;
            _photos = photos;
            _gate = gate;
            _idVerification = idVerification;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private string Timezone => TenantClock.IanaId(_tenantContext.Tenant.Timezone);

        public DateOnly Today() => TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);

        public bool RequiredAtCheckin => _tenantContext.Tenant.WristbandsEnabled && _tenantContext.Tenant.WristbandRequiredAtCheckin;

        public Task<RiderPhotoKey> RiderForPass(SeasonPassPurchase pass) => _photos.KeyForPass(pass, TenantId);

        public async Task<RiderDayBand?> TodayForPass(SeasonPassPurchase pass)
        {
            var key = await RiderForPass(pass);
            return await _bands.GetActiveForRider(TenantId, Today(), key.OwnerKey, key.RiderKey);
        }

        public async Task<BandCodeUse?> CodeInUse(string code, DateOnly date)
        {
            if (RiderDayBandRules.CleanCode(code) is not { } c) return null;
            if (await _bands.GetActiveByCode(TenantId, date, c) is { } day) return new BandCodeUse(day, null);
            return await _legacy.ResolveCodeOn(TenantId, c, date, Timezone) is { } hit ? new BandCodeUse(null, hit) : null;
        }

        private static string HolderName(SeasonPassPurchase pass) =>
            string.IsNullOrWhiteSpace(pass.HolderFirstName) ? pass.PurchaserName : $"{pass.HolderFirstName} {pass.HolderLastName}".Trim();

        public string IssuedTime(RiderDayBand b) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.IssuedAt, DateTimeKind.Utc), TenantClock.Resolve(_tenantContext.Tenant.Timezone))
                .ToString("h:mm tt");

        private string When(DateOnly date) => date == Today() ? "today" : $"on {date:MMM d}";

        public async Task<RiderDayBandResult> Check(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId,
            string? code, string? replaceReason, DateOnly? businessDate = null, WaiverGateDecision? decided = null,
            Guid? correcting = null)
        {
            if (!_tenantContext.Tenant.WristbandsEnabled)
                return RiderDayBandResult.Fail("Wristbands aren't turned on for this track (Settings → Features).");
            var clean = RiderDayBandRules.CleanCode(code);
            if (clean is null) return RiderDayBandResult.Fail("Scan or enter the band's code.");
            if (replaceReason is not null && string.IsNullOrWhiteSpace(replaceReason))
                return RiderDayBandResult.Fail("Say why the band is being replaced (lost, broken, ...).");
            var date = businessDate ?? Today();
            var name = HolderName(pass);

            // The gate decides, exactly as admission does: a rider the gate blocks can't be banded.
            var decision = decided ?? await _gate.EvaluatePass(TenantId, pass, product, eventId);
            if (decision.BlockReason is not null) return new RiderDayBandResult(null, decision.BlockReason, decision, null, null);
            if (_tenantContext.Tenant.RequireIdForWristband
                && !(await _idVerification.StatusForPass(pass, TenantId)).Verified)
                return RiderDayBandResult.Fail($"{name}'s ID and age haven't been verified yet. "
                    + "Check their photo ID on the scan screen first, then band them.");

            var rider = await RiderForPass(pass);
            var current = await _bands.GetActiveForRider(TenantId, date, rider.OwnerKey, rider.RiderKey);
            var use = await CodeInUse(clean, date);
            var reason = replaceReason?.Trim();
            var action = RiderDayBandRules.Decide(current, use?.DayBand, clean, reason is not null);
            // Fixing the band on an admission (the band dialog): the band it carries is replaced, audited.
            if (action == DayBandAction.RiderHasBand && correcting is Guid fix && await _bands.Covers(TenantId, current!.Id, fix))
            {
                action = DayBandAction.Replace;
                reason = RiderDayBandRules.CorrectedAtGate;
            }
            switch (action)
            {
                case DayBandAction.ReplaceWithSameBand:
                    return RiderDayBandResult.Fail($"That's the band {name} is already wearing ({current!.Code}). Scan the new band.", rider, current);
                case DayBandAction.CodeTaken:
                    return RiderDayBandResult.Fail($"Band {clean} is already on {use!.HolderName} {When(date)}. Use a different band.", rider, current);
                case DayBandAction.RiderHasBand:
                    return RiderDayBandResult.Fail($"{name} already wears band {current!.Code} {When(date)} (issued {IssuedTime(current)}). "
                        + "Use Replace band for a lost or broken one.", rider, current);
            }
            // A band from before day bands (event_wristband) live that day on someone else.
            if (action != DayBandAction.AlreadyWorn && use?.Legacy is { } legacy
                && !(legacy.Source == "season_pass" && legacy.PassPurchaseId == pass.Id))
                return RiderDayBandResult.Fail($"Band {clean} is already on {use.HolderName} {When(date)}. Use a different band.", rider, current);
            return new RiderDayBandResult(action == DayBandAction.AlreadyWorn ? current : null, null, null, rider, current)
            {
                Action = action,
                Code = clean,
                Date = date,
                ReplaceReason = action == DayBandAction.Replace ? reason : null,
            };
        }

        public async Task<RiderDayBandResult> Write(RiderDayBandResult check, SeasonPassPurchase pass, Guid reservationId, Guid? staffUserId)
        {
            if (!check.Ok || check.Action is null) return check;
            if (check.Action == DayBandAction.AlreadyWorn)
            {
                // Already on their wrist: this admission is covered by it too.
                await _bands.LinkAdmission(TenantId, check.Band!.Id, reservationId, null, staffUserId);
                return check;
            }
            var rider = check.Rider!;
            var band = new RiderDayBand
            {
                TenantId = TenantId,
                BusinessDate = check.Date,
                Code = check.Code!,
                OwnerKey = rider.OwnerKey,
                RiderKey = rider.RiderKey,
                RiderUserId = rider.UserId,
                RiderName = HolderName(pass),
                SeasonPassPurchaseId = pass.Id,
                IssuedByUserId = staffUserId,
            };
            if (check.Action == DayBandAction.Replace && check.Current is { } old)
            {
                var reason = check.ReplaceReason ?? RiderDayBandRules.CorrectedAtGate;
                var replaced = await _bands.Replace(old.Id, band, reason, staffUserId);
                if (replaced is null) return RiderDayBandResult.Fail($"Band {band.Code} was just put on someone else, or the old band changed. Scan again.");
                await _bands.LinkAdmission(TenantId, replaced.Id, reservationId, null, staffUserId);
                await _audit.Log("wristband.replaced", $"Replaced band {old.Code} on {band.RiderName} with {band.Code} ({reason})",
                    targetKind: "rider_day_band", targetId: replaced.Id, tenantId: TenantId,
                    metadata: new { oldBandId = old.Id, oldCode = old.Code, newCode = band.Code, reason, seasonPassPurchaseId = pass.Id, reservationId });
                return check with { Band = replaced, Current = replaced };
            }
            var issued = await _bands.Issue(band, reservationId, null);
            if (issued is null)
                return RiderDayBandResult.Fail($"Band {band.Code} was just put on someone else, or {band.RiderName} was just banded. Scan again.");
            await _audit.Log("wristband.issued", $"Band {band.Code} on {band.RiderName} for {band.BusinessDate:yyyy-MM-dd}",
                targetKind: "rider_day_band", targetId: issued.Id, tenantId: TenantId,
                metadata: new { code = band.Code, seasonPassPurchaseId = pass.Id, reservationId });
            return check with { Band = issued, Current = issued };
        }

        public async Task<RiderDayBandResult> IssueForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid reservationId,
            Guid? eventId, string? code, string? replaceReason, Guid? staffUserId, DateOnly? businessDate = null, Guid? correcting = null)
        {
            var check = await Check(pass, product, eventId, code, replaceReason, businessDate, null, correcting);
            return check.Ok ? await Write(check, pass, reservationId, staffUserId) : check;
        }

        public async Task<RiderDayBandCheckIn> ForCheckIn(SeasonPassPurchase? pass, RiderDayBandResult? scanned = null)
        {
            if (!_tenantContext.Tenant.WristbandsEnabled || pass is null || pass.TenantId != TenantId)
                return new RiderDayBandCheckIn(null, false);
            var worn = scanned is not null ? scanned.Current : await TodayForPass(pass);
            return new RiderDayBandCheckIn(worn, RequiredAtCheckin && worn is null && scanned is null);
        }

        public Task CompleteCheckIn(RiderDayBandCheckIn checkIn, Guid reservationId, Guid? staffUserId) =>
            checkIn.Worn is { } worn ? _bands.LinkAdmission(TenantId, worn.Id, reservationId, null, staffUserId) : Task.CompletedTask;

        public Task<int> UnlinkAdmission(Guid reservationId) => _bands.UnlinkReservation(TenantId, reservationId);

        public Task<Dictionary<Guid, string>> CodesForReservations(IEnumerable<Guid> reservationIds) =>
            _bands.GetActiveCodesForReservations(reservationIds, TenantId);

        public Task<List<RiderDayBandAdmissionRow>> Admissions(Guid bandId) => _bands.ListAdmissions(TenantId, bandId);
    }
}
