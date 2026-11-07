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

        /// <summary>
        /// The business date a ticket admission is banded for: a single-day event's own local date; for a
        /// multi-day event (a camp), today while it runs (each day's attendance is that day's band), else its
        /// nearest day.
        /// </summary>
        public static DateOnly TicketDate(DateOnly eventStart, DateOnly eventEnd, DateOnly today)
        {
            if (eventEnd <= eventStart) return eventStart;
            if (today < eventStart) return eventStart;
            if (today > eventEnd) return eventEnd;
            return today;
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
    /// Daily wristbands for riders (Script0312 / Script0313): a band goes on a RIDER for a tenant business date
    /// and covers every admission they have that day, passes and tickets alike. The rider is keyed as the gate
    /// keys their photo and ID check (IRiderPhotos.KeyForPass / KeyForTicket: their own account, a dependent
    /// under their guardian, or a name under the buyer), so the same rider is the same band whichever
    /// credential admits them. Every band passes the same checks (Check): wristbands on, the gate clear for the
    /// rider (waiver / ID check / photo, as admission judges them), a verified ID when the track requires it for
    /// a band, one band per rider per day, and one rider per band code per day across both stores (CodeInUse).
    /// The write (Write) only writes what a passed check decided.
    /// </summary>
    public interface IRiderDayBands
    {
        /// <summary>The tenant's business date now.</summary>
        DateOnly Today();
        /// <summary>A band check-in completes only with a band (wristbands on and the setting on).</summary>
        bool RequiredAtCheckin { get; }

        // ── Who ──
        Task<RiderDayBandSubject> ForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId);
        Task<RiderDayBandSubject> ForTicket(EventTicketPurchase ticket);
        /// <summary>The business date a ticket's admission is banded for (RiderDayBandRules.TicketDate).</summary>
        Task<DateOnly> TicketDate(EventTicketPurchase ticket);
        /// <summary>The rider's active band on that date, if any.</summary>
        Task<RiderDayBand?> BandFor(RiderDayBandSubject subject, DateOnly date);
        /// <summary>The bands these riders wear on that date (same rule as BandFor), in one read.</summary>
        Task<Dictionary<(string, string), RiderDayBand>> BandsFor(IEnumerable<RiderPhotoKey> keys, DateOnly date);
        Task<RiderPhotoKey> RiderForPass(SeasonPassPurchase pass);
        /// <summary>The pass holder's active band today, if any.</summary>
        Task<RiderDayBand?> TodayForPass(SeasonPassPurchase pass);

        /// <summary>Who has this band code on that date (a day band, else an older band live that day), or null.</summary>
        Task<BandCodeUse?> CodeInUse(string code, DateOnly date);

        // ── Checks, then writes ──
        /// <summary>
        /// Checks a band for <paramref name="subject"/> on <paramref name="date"/> without writing.
        /// <paramref name="decided"/>: the gate decision the caller already made for this admission (not re-run).
        /// <paramref name="correctingReservation"/> / <paramref name="correctingTicket"/>: the admission being fixed
        /// in a band dialog; a different band on a rider whose band covers it is a replacement "corrected at the gate".
        /// </summary>
        Task<RiderDayBandResult> Check(RiderDayBandSubject subject, string? code, string? replaceReason, DateOnly date,
            WaiverGateDecision? decided = null, Guid? correctingReservation = null, Guid? correctingTicket = null,
            RiderDayBandTickets? read = null);
        /// <summary>Writes what a passed check decided and ties the admissions to the band (a pass admission, and /
        /// or a rider's tickets). No checks of its own; a race lost since the check comes back as an error.</summary>
        Task<RiderDayBandResult> Write(RiderDayBandResult check, RiderDayBandSubject subject, Guid? reservationId,
            IReadOnlyCollection<Guid>? ticketIds, Guid? staffUserId);

        /// <summary>Pass shorthand: Check for the pass holder (default today).</summary>
        Task<RiderDayBandResult> Check(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId, string? code,
            string? replaceReason, DateOnly? businessDate = null, WaiverGateDecision? decided = null, Guid? correcting = null);
        /// <summary>Pass shorthand: Write tying the reservation.</summary>
        Task<RiderDayBandResult> Write(RiderDayBandResult check, SeasonPassPurchase pass, Guid reservationId, Guid? staffUserId);
        /// <summary>Pass shorthand: Check, then Write.</summary>
        Task<RiderDayBandResult> IssueForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid reservationId,
            Guid? eventId, string? code, string? replaceReason, Guid? staffUserId, DateOnly? businessDate = null,
            Guid? correcting = null);
        /// <summary>Ticket: Check, then Write, for the ticket's business date (a band dialog fixes the ticket's band).</summary>
        Task<RiderDayBandResult> IssueForTicket(EventTicketPurchase ticket, string? code, string? replaceReason, Guid? staffUserId);

        // ── Check-in, for every path that admits a pass or a ticket ──
        /// <summary>
        /// The day band at a check-in: the band the rider wears that day, and whether the track's "wristband
        /// required at check-in" refuses this admission (none worn, none scanned with it). <paramref name="scanned"/>:
        /// a passed band check for a band scanned with the admission.
        /// </summary>
        Task<RiderDayBandCheckIn> ForCheckIn(RiderDayBandSubject? subject, DateOnly date, RiderDayBandResult? scanned = null);
        Task<RiderDayBandCheckIn> ForCheckIn(SeasonPassPurchase? pass, RiderDayBandResult? scanned = null);
        /// <summary>After the check-in: the band the rider already wears covers this admission too.</summary>
        Task CompleteCheckIn(RiderDayBandCheckIn checkIn, Guid reservationId, Guid? staffUserId);
        /// <summary>
        /// The day band at a ticket check-in, for every path that admits a ticket (scan, order redeem, Event Riders,
        /// offline sync): the band the rider wears on the ticket's business date, and the "required at check-in"
        /// refusal, which applies to rider tickets only (<paramref name="decision"/>: the gate decision already made).
        /// </summary>
        Task<RiderDayBandCheckIn> ForTicketCheckIn(EventTicketPurchase ticket, WaiverGateDecision? decision, DateOnly? date = null);
        /// <summary>The same for many tickets admitted on one date (an order): their riders and the bands they wear,
        /// read once, and each ticket's check-in rule. A null decision: never refused (offline sync).</summary>
        Task<RiderDayBandTickets> ForTickets(IReadOnlyList<(EventTicketPurchase Ticket, WaiverGateDecision? Decision)> tickets, DateOnly date);
        Task CompleteTicketCheckIn(RiderDayBandCheckIn checkIn, Guid ticketId, Guid? staffUserId);

        // ── Reads and unlinks ──
        Task<int> UnlinkAdmission(Guid reservationId);
        Task<int> UnlinkTicket(Guid ticketId);
        Task<Dictionary<Guid, string>> CodesForReservations(IEnumerable<Guid> reservationIds);
        Task<Dictionary<Guid, string>> CodesForTickets(IEnumerable<Guid> ticketIds);
        Task<List<RiderDayBandAdmissionRow>> Admissions(Guid bandId);
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
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventRepository _events;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public RiderDayBands(IRiderDayBandRepository bands, IWristbandRepository legacy, IRiderPhotos photos,
            IWaiverCheckInGate gate, IRiderIdVerification idVerification, IEventTicketTierRepository tiers,
            IEventRepository events, Services.Audit.IAuditLogger audit, ITenantContext tenantContext)
        {
            _bands = bands;
            _legacy = legacy;
            _photos = photos;
            _gate = gate;
            _idVerification = idVerification;
            _tiers = tiers;
            _events = events;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private string Timezone => TenantClock.IanaId(_tenantContext.Tenant.Timezone);

        public DateOnly Today() => TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);

        public bool RequiredAtCheckin => _tenantContext.Tenant.WristbandsEnabled && _tenantContext.Tenant.WristbandRequiredAtCheckin;

        // ── Who ──

        private static string HolderName(SeasonPassPurchase pass) =>
            string.IsNullOrWhiteSpace(pass.HolderFirstName) ? pass.PurchaserName : $"{pass.HolderFirstName} {pass.HolderLastName}".Trim();

        private static string RiderName(EventTicketPurchase t)
        {
            var n = $"{t.RiderFirstName} {t.RiderLastName}".Trim();
            return n.Length > 0 ? n : t.PurchaserName;
        }

        public Task<RiderPhotoKey> RiderForPass(SeasonPassPurchase pass) => _photos.KeyForPass(pass, TenantId);

        public async Task<RiderDayBandSubject> ForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId) =>
            new(await RiderForPass(pass), HolderName(pass), pass.Id, null,
                () => _gate.EvaluatePass(TenantId, pass, product, eventId),
                async () => (await _idVerification.StatusForPass(pass, TenantId)).Verified);

        public async Task<RiderDayBandSubject> ForTicket(EventTicketPurchase ticket) =>
            new(await _photos.KeyForTicket(ticket, TenantId), RiderName(ticket), null, ticket.Id,
                () => _gate.EvaluateTicket(TenantId, ticket),
                async () => (await _idVerification.StatusForTicket(ticket, TenantId)).Verified);

        public async Task<DateOnly> TicketDate(EventTicketPurchase ticket)
        {
            var today = Today();
            var tier = await _tiers.GetById(ticket.TierId, TenantId);
            var ev = tier is null ? null : await _events.GetById(tier.EventId, TenantId);
            if (ev is null) return today;
            DateOnly Local(DateTime utc) => TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.SpecifyKind(utc, DateTimeKind.Utc));
            return RiderDayBandRules.TicketDate(Local(ev.StartsAt), Local(ev.EndsAt), today);
        }

        // ── One rule for the band's date (review of item 10) ──
        // A band is issued for the business date of the admission it goes on (a pass: today; a ticket: its event's
        // day). The gate opens the evening before an event, so a band issued tonight for tomorrow is on a wrist
        // tonight too. Hence, on any gate day T:
        //   * the band a rider wears is their band for T, else their band for T + 1 (tonight's band): they never
        //     get a second band tonight, and tonight's admissions are tied to the band on their wrist;
        //   * a scan resolves to the band for T, else for T + 1;
        //   * a code is taken for date D by another rider's band within a day of D (their wrists overlap), while
        //     the same rider may keep their own code from one day to the next.
        private static bool SameRider(RiderDayBand b, RiderPhotoKey k) => b.OwnerKey == k.OwnerKey && b.RiderKey == k.RiderKey;

        private IReadOnlyCollection<DateOnly> WearDates(DateOnly date) =>
            date == Today() ? new[] { date, date.AddDays(1) } : new[] { date };

        public Task<RiderDayBand?> BandFor(RiderDayBandSubject subject, DateOnly date) => CurrentFor(subject.Rider, date);

        private async Task<RiderDayBand?> CurrentFor(RiderPhotoKey key, DateOnly date) =>
            (await BandsFor(new[] { key }, date)).GetValueOrDefault((key.OwnerKey, key.RiderKey));

        public async Task<Dictionary<(string, string), RiderDayBand>> BandsFor(IEnumerable<RiderPhotoKey> keys, DateOnly date)
        {
            var riders = keys.Select(k => (k.OwnerKey, k.RiderKey)).Distinct().ToList();
            var bands = await _bands.ListActiveForRiders(TenantId, WearDates(date), riders);
            // The band for the date itself first, else tonight's (tomorrow's) band.
            return bands
                .OrderBy(b => b.BusinessDate == date ? 0 : 1)
                .GroupBy(b => (b.OwnerKey, b.RiderKey))
                .ToDictionary(g => g.Key, g => g.First());
        }

        public async Task<RiderDayBand?> TodayForPass(SeasonPassPurchase pass) => await CurrentFor(await RiderForPass(pass), Today());

        public async Task<BandCodeUse?> CodeInUse(string code, DateOnly date)
        {
            if (RiderDayBandRules.CleanCode(code) is not { } c) return null;
            // The band for that date, else one issued for the next day (worn since the evening before).
            var near = await _bands.ListActiveByCodeNear(TenantId, date, c);
            if ((near.FirstOrDefault(b => b.BusinessDate == date) ?? near.FirstOrDefault(b => b.BusinessDate == date.AddDays(1))) is { } day)
                return new BandCodeUse(day, null);
            return await _legacy.ResolveCodeOn(TenantId, c, date, Timezone) is { } hit ? new BandCodeUse(null, hit) : null;
        }

        public string IssuedTime(RiderDayBand b) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.IssuedAt, DateTimeKind.Utc), TenantClock.Resolve(_tenantContext.Tenant.Timezone))
                .ToString("h:mm tt");

        private string When(DateOnly date) => date == Today() ? "today" : $"on {date:MMM d}";

        // ── Checks, then writes ──

        public async Task<RiderDayBandResult> Check(RiderDayBandSubject subject, string? code, string? replaceReason, DateOnly date,
            WaiverGateDecision? decided = null, Guid? correctingReservation = null, Guid? correctingTicket = null,
            RiderDayBandTickets? read = null)
        {
            if (!_tenantContext.Tenant.WristbandsEnabled)
                return RiderDayBandResult.Fail("Wristbands aren't turned on for this track (Settings → Features).");
            var clean = RiderDayBandRules.CleanCode(code);
            if (clean is null) return RiderDayBandResult.Fail("Scan or enter the band's code.");
            if (replaceReason is not null && string.IsNullOrWhiteSpace(replaceReason))
                return RiderDayBandResult.Fail("Say why the band is being replaced (lost, broken, ...).");
            var name = subject.Name;

            // The gate decides, exactly as admission does: a rider the gate blocks can't be banded.
            var decision = decided ?? await subject.Gate();
            if (decision.BlockReason is not null) return new RiderDayBandResult(null, decision.BlockReason, decision, null, null);
            if (_tenantContext.Tenant.RequireIdForWristband && !await subject.IdVerified())
                return RiderDayBandResult.Fail($"{name}'s ID and age haven't been verified yet. "
                    + "Check their photo ID on the scan screen first, then band them.");

            var rider = subject.Rider;
            var current = read is not null && read.Date == date ? read.WornBy(subject) : await BandFor(subject, date);
            // Another rider wearing this code within a day of the date (wrists overlap), and older bands.
            var other = (await _bands.ListActiveByCodeNear(TenantId, date, clean)).FirstOrDefault(b => !SameRider(b, rider));
            var use = other is not null ? new BandCodeUse(other, null)
                : await _legacy.ResolveCodeOn(TenantId, clean, date, Timezone) is { } hit ? new BandCodeUse(null, hit) : null;
            var reason = replaceReason?.Trim();
            var action = RiderDayBandRules.Decide(current, use?.DayBand, clean, reason is not null);
            // Fixing the band on an admission (the band dialog): the band it carries is replaced, audited.
            if (action == DayBandAction.RiderHasBand && (correctingReservation is not null || correctingTicket is not null)
                && await _bands.Covers(TenantId, current!.Id, correctingReservation, correctingTicket))
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
                && !(legacy.Source == "season_pass" && subject.PassId is Guid pid && legacy.PassPurchaseId == pid)
                && !(legacy.Source == "ticket" && subject.TicketId is Guid tid && legacy.TicketId == tid))
                return RiderDayBandResult.Fail($"Band {clean} is already on {use.HolderName} {When(date)}. Use a different band.", rider, current);
            return new RiderDayBandResult(action == DayBandAction.AlreadyWorn ? current : null, null, null, rider, current)
            {
                Action = action,
                Code = clean,
                Date = date,
                ReplaceReason = action == DayBandAction.Replace ? reason : null,
            };
        }

        // A band written for an admission replaces any older (event_wristband) band on it: that code stops resolving.
        private async Task LinkAll(Guid bandId, Guid? reservationId, IReadOnlyCollection<Guid>? ticketIds, Guid? staffUserId)
        {
            if (reservationId is Guid r)
            {
                await _bands.LinkAdmission(TenantId, bandId, r, null, staffUserId);
                await _legacy.UnlinkReservation(r, TenantId);
            }
            foreach (var t in ticketIds ?? Array.Empty<Guid>())
            {
                await _bands.LinkAdmission(TenantId, bandId, null, t, staffUserId);
                await _legacy.UnlinkTicket(t, TenantId);
            }
        }

        public async Task<RiderDayBandResult> Write(RiderDayBandResult check, RiderDayBandSubject subject, Guid? reservationId,
            IReadOnlyCollection<Guid>? ticketIds, Guid? staffUserId)
        {
            if (!check.Ok || check.Action is null) return check;
            if (check.Action == DayBandAction.AlreadyWorn)
            {
                // Already on their wrist: these admissions are covered by it too.
                await LinkAll(check.Band!.Id, reservationId, ticketIds, staffUserId);
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
                RiderName = subject.Name,
                SeasonPassPurchaseId = subject.PassId,
                EventTicketPurchaseId = subject.TicketId,
                IssuedByUserId = staffUserId,
            };
            var meta = new { passId = subject.PassId, ticketId = subject.TicketId, reservationId, ticketIds };
            if (check.Action == DayBandAction.Replace && check.Current is { } old)
            {
                var reason = check.ReplaceReason ?? RiderDayBandRules.CorrectedAtGate;
                var replaced = await _bands.Replace(old.Id, band, reason, staffUserId);
                if (replaced is null) return RiderDayBandResult.Fail($"Band {band.Code} was just put on someone else, or the old band changed. Scan again.");
                await LinkAll(replaced.Id, reservationId, ticketIds, staffUserId);
                await _audit.Log("wristband.replaced", $"Replaced band {old.Code} on {band.RiderName} with {band.Code} ({reason})",
                    targetKind: "rider_day_band", targetId: replaced.Id, tenantId: TenantId,
                    metadata: new { oldBandId = old.Id, oldCode = old.Code, newCode = band.Code, reason, admission = meta });
                return check with { Band = replaced, Current = replaced };
            }
            var first = reservationId is null ? ticketIds?.FirstOrDefault() : null;
            var issued = await _bands.Issue(band, reservationId, first is Guid f && f != Guid.Empty ? f : null);
            if (issued is null)
                return RiderDayBandResult.Fail($"Band {band.Code} was just put on someone else, or {band.RiderName} was just banded. Scan again.");
            await LinkAll(issued.Id, reservationId, ticketIds, staffUserId);
            await _audit.Log("wristband.issued", $"Band {band.Code} on {band.RiderName} for {band.BusinessDate:yyyy-MM-dd}",
                targetKind: "rider_day_band", targetId: issued.Id, tenantId: TenantId,
                metadata: new { code = band.Code, admission = meta });
            return check with { Band = issued, Current = issued };
        }

        public async Task<RiderDayBandResult> Check(SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId,
            string? code, string? replaceReason, DateOnly? businessDate = null, WaiverGateDecision? decided = null,
            Guid? correcting = null) =>
            await Check(await ForPass(pass, product, eventId), code, replaceReason, businessDate ?? Today(), decided, correcting);

        public async Task<RiderDayBandResult> Write(RiderDayBandResult check, SeasonPassPurchase pass, Guid reservationId, Guid? staffUserId) =>
            await Write(check, await ForPass(pass, null, null), reservationId, null, staffUserId);

        public async Task<RiderDayBandResult> IssueForPass(SeasonPassPurchase pass, SeasonPassProduct? product, Guid reservationId,
            Guid? eventId, string? code, string? replaceReason, Guid? staffUserId, DateOnly? businessDate = null, Guid? correcting = null)
        {
            var subject = await ForPass(pass, product, eventId);
            var check = await Check(subject, code, replaceReason, businessDate ?? Today(), null, correcting);
            return check.Ok ? await Write(check, subject, reservationId, null, staffUserId) : check;
        }

        public async Task<RiderDayBandResult> IssueForTicket(EventTicketPurchase ticket, string? code, string? replaceReason, Guid? staffUserId)
        {
            var subject = await ForTicket(ticket);
            var check = await Check(subject, code, replaceReason, await TicketDate(ticket), null, null, ticket.Id);
            return check.Ok ? await Write(check, subject, null, new[] { ticket.Id }, staffUserId) : check;
        }

        // ── Check-in ──

        public async Task<RiderDayBandCheckIn> ForCheckIn(RiderDayBandSubject? subject, DateOnly date, RiderDayBandResult? scanned = null)
        {
            if (!_tenantContext.Tenant.WristbandsEnabled || subject is null) return new RiderDayBandCheckIn(null, false);
            var worn = scanned is not null ? scanned.Current : await BandFor(subject, date);
            return new RiderDayBandCheckIn(worn, RequiredAtCheckin && worn is null && scanned is null);
        }

        public async Task<RiderDayBandCheckIn> ForCheckIn(SeasonPassPurchase? pass, RiderDayBandResult? scanned = null)
        {
            if (!_tenantContext.Tenant.WristbandsEnabled || pass is null || pass.TenantId != TenantId)
                return new RiderDayBandCheckIn(null, false);
            return await ForCheckIn(await ForPass(pass, null, null), Today(), scanned);
        }

        public Task CompleteCheckIn(RiderDayBandCheckIn checkIn, Guid reservationId, Guid? staffUserId) =>
            checkIn.Worn is { } worn ? _bands.LinkAdmission(TenantId, worn.Id, reservationId, null, staffUserId) : Task.CompletedTask;

        public Task CompleteTicketCheckIn(RiderDayBandCheckIn checkIn, Guid ticketId, Guid? staffUserId) =>
            checkIn.Worn is { } worn ? _bands.LinkAdmission(TenantId, worn.Id, null, ticketId, staffUserId) : Task.CompletedTask;

        public async Task<RiderDayBandCheckIn> ForTicketCheckIn(EventTicketPurchase ticket, WaiverGateDecision? decision, DateOnly? date = null) =>
            (await ForTickets(new[] { (ticket, decision) }, date ?? await TicketDate(ticket))).CheckIns[ticket.Id];

        public async Task<RiderDayBandTickets> ForTickets(
            IReadOnlyList<(EventTicketPurchase Ticket, WaiverGateDecision? Decision)> tickets, DateOnly date)
        {
            if (!_tenantContext.Tenant.WristbandsEnabled)
            {
                var off = new RiderDayBandTickets { Date = date };
                foreach (var (t, _) in tickets) off.CheckIns[t.Id] = new RiderDayBandCheckIn(null, false);
                return off;
            }
            var subjects = new List<(EventTicketPurchase Ticket, WaiverGateDecision? Decision, RiderDayBandSubject Subject)>();
            foreach (var (t, d) in tickets) subjects.Add((t, d, await ForTicket(t)));
            var ctx = new RiderDayBandTickets { Date = date, Worn = await BandsFor(subjects.Select(s => s.Subject.Rider), date) };
            foreach (var (t, d, subject) in subjects)
            {
                ctx.Subjects[t.Id] = subject;
                var band = ctx.WornBy(subject);
                // Riders wear bands; a spectator's ticket is never refused for want of one.
                ctx.CheckIns[t.Id] = new RiderDayBandCheckIn(band, RequiredAtCheckin && band is null && d is { RiderAudience: true }) { Name = subject.Name };
            }
            return ctx;
        }

        // ── Reads and unlinks ──

        public Task<int> UnlinkAdmission(Guid reservationId) => _bands.UnlinkReservation(TenantId, reservationId);

        public Task<int> UnlinkTicket(Guid ticketId) => _bands.UnlinkTicket(TenantId, ticketId);

        public Task<Dictionary<Guid, string>> CodesForReservations(IEnumerable<Guid> reservationIds) =>
            _bands.GetActiveCodesForReservations(reservationIds, TenantId);

        public Task<Dictionary<Guid, string>> CodesForTickets(IEnumerable<Guid> ticketIds) =>
            _bands.GetActiveCodesForTickets(ticketIds, TenantId);

        public Task<List<RiderDayBandAdmissionRow>> Admissions(Guid bandId) => _bands.ListAdmissions(TenantId, bandId);
    }
}
