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
        /// <summary>The rider already wears this very band today: nothing to write (link the admission).</summary>
        AlreadyWorn,
        Issue,
        Replace,
        /// <summary>The rider already wears another band today and this isn't a replacement.</summary>
        RiderHasBand,
        /// <summary>The code is on another rider today.</summary>
        CodeTaken,
    }

    public static class RiderDayBandRules
    {
        public const int MaxCodeLength = 120;
        public static readonly string[] ReplaceReasons = { "lost", "broken", "other" };

        /// <summary>A scanned / typed band code, trimmed; null when empty or too long.</summary>
        public static string? CleanCode(string? code)
        {
            var c = code?.Trim();
            return string.IsNullOrEmpty(c) || c.Length > MaxCodeLength ? null : c;
        }

        /// <summary>
        /// One band per rider per day and one rider per band per day. <paramref name="current"/> is the rider's
        /// active band today, <paramref name="codeHolder"/> the active band with this code today (anyone's).
        /// </summary>
        public static DayBandAction Decide(RiderDayBand? current, RiderDayBand? codeHolder, string code, bool replacing)
        {
            if (current is not null && string.Equals(current.Code, code, StringComparison.OrdinalIgnoreCase))
                return DayBandAction.AlreadyWorn;
            if (codeHolder is not null && codeHolder.Id != current?.Id) return DayBandAction.CodeTaken;
            if (current is null) return DayBandAction.Issue;
            return replacing ? DayBandAction.Replace : DayBandAction.RiderHasBand;
        }
    }

    /// <summary>
    /// Daily wristbands for riders (Script0312): a band goes on a RIDER for the tenant's business date and
    /// covers every admission they have that day. The rider is keyed as the gate keys their photo and ID check
    /// (IRiderPhotos.KeyForPass now; KeyForTicket in item 10), so the same rider is the same band whichever
    /// credential admits them. Every band passes the same checks: wristbands on, the gate clear for the rider
    /// (waiver / ID check / photo, exactly as admission judges them), a verified ID when the track requires it
    /// for a band, one band per rider per day, and one rider per band code per day (including legacy
    /// event_wristband rows in use that day).
    /// </summary>
    public interface IRiderDayBands
    {
        /// <summary>The tenant's business date now.</summary>
        DateOnly Today();
        Task<RiderPhotoKey> RiderForPass(SeasonPassPurchase pass);
        /// <summary>The pass holder's active band today, if any.</summary>
        Task<RiderDayBand?> TodayForPass(SeasonPassPurchase pass);
        /// <summary>Checks a band for this pass holder without writing (run before the admission it rides on).</summary>
        Task<RiderDayBandResult> Check(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId, string? code, string? replaceReason);
        /// <summary>Issues (or replaces, with a reason) the holder's band today and ties the admission to it.</summary>
        Task<RiderDayBandResult> IssueForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid reservationId,
            Guid? eventId, string? code, string? replaceReason, Guid? staffUserId);
        /// <summary>The track requires a band to complete check-in and wristbands are on.</summary>
        bool RequiredAtCheckin { get; }
        /// <summary>Ties another admission (a second event the same day) to a band the rider already wears.</summary>
        Task LinkAdmission(Guid bandId, Guid reservationId, Guid? staffUserId);
        /// <summary>Active band codes covering these reservations.</summary>
        Task<Dictionary<Guid, string>> CodesForReservations(IEnumerable<Guid> reservationIds);
        /// <summary>The admissions a band covers.</summary>
        Task<List<RiderDayBandAdmissionRow>> Admissions(Guid bandId);
        /// <summary>Voids the band covering a reservation (unlinked at the gate).</summary>
        Task<int> VoidForReservation(Guid reservationId, string reason, Guid? staffUserId);
        /// <summary>The active band with this code today, if any.</summary>
        Task<RiderDayBand?> ActiveByCodeToday(string code);
        /// <summary>The tenant-local wall-clock time a band was issued (for "issued 9:12").</summary>
        string IssuedTime(RiderDayBand band);
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

        private static string HolderName(SeasonPassPurchase pass) =>
            string.IsNullOrWhiteSpace(pass.HolderFirstName) ? pass.PurchaserName : $"{pass.HolderFirstName} {pass.HolderLastName}".Trim();

        public Task LinkAdmission(Guid bandId, Guid reservationId, Guid? staffUserId) =>
            _bands.LinkAdmission(TenantId, bandId, reservationId, null, staffUserId);

        public Task<Dictionary<Guid, string>> CodesForReservations(IEnumerable<Guid> reservationIds) =>
            _bands.GetActiveCodesForReservations(reservationIds, TenantId);

        public Task<List<RiderDayBandAdmissionRow>> Admissions(Guid bandId) => _bands.ListAdmissions(TenantId, bandId);

        public Task<int> VoidForReservation(Guid reservationId, string reason, Guid? staffUserId) =>
            _bands.VoidForReservation(TenantId, reservationId, reason, staffUserId);

        public async Task<RiderDayBand?> ActiveByCodeToday(string code) =>
            RiderDayBandRules.CleanCode(code) is { } c ? await _bands.GetActiveByCode(TenantId, Today(), c) : null;

        public string IssuedTime(RiderDayBand b) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.IssuedAt, DateTimeKind.Utc), TenantClock.Resolve(_tenantContext.Tenant.Timezone))
                .ToString("h:mm tt");

        public async Task<RiderDayBandResult> Check(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId,
            string? code, string? replaceReason)
        {
            if (!_tenantContext.Tenant.WristbandsEnabled)
                return RiderDayBandResult.Fail("Wristbands aren't turned on for this track (Settings → Features).");
            var clean = RiderDayBandRules.CleanCode(code);
            if (clean is null) return RiderDayBandResult.Fail("Scan or enter the band's code.");
            var replacing = replaceReason is not null;
            if (replacing && string.IsNullOrWhiteSpace(replaceReason))
                return RiderDayBandResult.Fail("Say why the band is being replaced (lost, broken, ...).");

            var name = HolderName(pass);
            // The gate decides, exactly as admission does: a rider the gate blocks can't be banded.
            var decision = await _gate.EvaluatePass(TenantId, pass, product, eventId);
            if (decision.BlockReason is not null) return new RiderDayBandResult(null, decision.BlockReason, decision, null, null);
            if (_tenantContext.Tenant.RequireIdForWristband
                && !(await _idVerification.StatusForPass(pass, TenantId)).Verified)
                return RiderDayBandResult.Fail($"{name}'s ID and age haven't been verified yet. "
                    + "Check their photo ID on the scan screen first, then band them.");

            var rider = await RiderForPass(pass);
            var today = Today();
            var current = await _bands.GetActiveForRider(TenantId, today, rider.OwnerKey, rider.RiderKey);
            var holder = await _bands.GetActiveByCode(TenantId, today, clean);
            switch (RiderDayBandRules.Decide(current, holder, clean, replacing))
            {
                case DayBandAction.AlreadyWorn:
                    return new RiderDayBandResult(current, null, null, rider, current);
                case DayBandAction.CodeTaken:
                    return RiderDayBandResult.Fail($"Band {clean} is already on {holder!.RiderName} today. Use a different band.", rider, current);
                case DayBandAction.RiderHasBand:
                    return RiderDayBandResult.Fail($"{name} already wears band {current!.Code} today (issued {IssuedTime(current)}). "
                        + "Use Replace band for a lost or broken one.", rider, current);
            }
            // A band from before day bands (event_wristband) in use today on someone else.
            if (await _legacy.HolderOnDate(TenantId, clean, today, Timezone, pass.Id) is { } legacyHolder)
                return RiderDayBandResult.Fail($"Band {clean} is already on {legacyHolder} today. Use a different band.", rider, current);
            return new RiderDayBandResult(null, null, null, rider, current);
        }

        public async Task<RiderDayBandResult> IssueForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid reservationId,
            Guid? eventId, string? code, string? replaceReason, Guid? staffUserId)
        {
            var check = await Check(pass, product, eventId, code, replaceReason);
            if (!check.Ok) return check;
            var clean = RiderDayBandRules.CleanCode(code)!;
            if (check.Band is { } worn)
            {
                // Already on their wrist: this admission is covered by it too.
                await _bands.LinkAdmission(TenantId, worn.Id, reservationId, null, staffUserId);
                return check;
            }
            var rider = check.Rider!;
            var band = new RiderDayBand
            {
                TenantId = TenantId,
                BusinessDate = Today(),
                Code = clean,
                OwnerKey = rider.OwnerKey,
                RiderKey = rider.RiderKey,
                RiderUserId = rider.UserId,
                RiderName = HolderName(pass),
                SeasonPassPurchaseId = pass.Id,
                IssuedByUserId = staffUserId,
            };
            if (check.Current is { } old && replaceReason is not null)
            {
                var reason = replaceReason.Trim();
                var replaced = await _bands.Replace(old.Id, band, reason, staffUserId);
                if (replaced is null) return RiderDayBandResult.Fail($"Band {clean} was just put on someone else, or the old band changed. Scan again.");
                await _bands.LinkAdmission(TenantId, replaced.Id, reservationId, null, staffUserId);
                await _audit.Log("wristband.replaced", $"Replaced band {old.Code} on {band.RiderName} with {clean} ({reason})",
                    targetKind: "rider_day_band", targetId: replaced.Id, tenantId: TenantId,
                    metadata: new { oldBandId = old.Id, oldCode = old.Code, newCode = clean, reason, seasonPassPurchaseId = pass.Id });
                return new RiderDayBandResult(replaced, null, null, rider, replaced);
            }
            var issued = await _bands.Issue(band, reservationId, null);
            if (issued is null)
                return RiderDayBandResult.Fail($"Band {clean} was just put on someone else, or {band.RiderName} was just banded. Scan again.");
            await _audit.Log("wristband.issued", $"Band {clean} on {band.RiderName} for {band.BusinessDate:yyyy-MM-dd}",
                targetKind: "rider_day_band", targetId: issued.Id, tenantId: TenantId,
                metadata: new { code = clean, seasonPassPurchaseId = pass.Id, reservationId });
            return new RiderDayBandResult(issued, null, null, rider, issued);
        }
    }
}
