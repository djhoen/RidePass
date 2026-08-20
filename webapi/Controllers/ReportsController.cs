using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Accounting;
using Services.Helpers;
using Services.Repositories.Data.ReportData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Reports;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportsRepository _reports;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly IConcessionRepository _concessions;
        private readonly IEventRepository _events;
        private readonly IWaiverRepository _waivers;
        private readonly IMembershipRepository _memberships;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly ISmsSender _sms;
        private readonly ISmtpEmailer _emailer;
        private readonly IScheduledTaskRepository _scheduledTasks;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantTaxRepository _tax;
        private readonly IEndOfDayReportRepository _endOfDay;
        private readonly IQuickBooksRepository _quickBooks;

        public ReportsController(
            IReportsRepository reports,
            IConcessionRepository concessions,
            IEventRepository events,
            IWaiverRepository waivers,
            IMembershipRepository memberships,
            IEventTicketPurchaseRepository tickets,
            ISeasonPassRepository seasonPasses,
            ISmsSender sms,
            ISmtpEmailer emailer,
            IScheduledTaskRepository scheduledTasks,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            ITenantContext tenantContext,
            ITenantTaxRepository tax,
            IEndOfDayReportRepository endOfDay,
            IQuickBooksRepository quickBooks,
            Services.Audit.IAuditLogger audit)
        {
            _audit = audit;
            _reports = reports;
            _concessions = concessions;
            _events = events;
            _waivers = waivers;
            _memberships = memberships;
            _tickets = tickets;
            _seasonPasses = seasonPasses;
            _sms = sms;
            _emailer = emailer;
            _scheduledTasks = scheduledTasks;
            _waiverGate = waiverGate;
            _tenantContext = tenantContext;
            _tax = tax;
            _endOfDay = endOfDay;
            _quickBooks = quickBooks;
        }

        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/Summary")]
        public async Task<IActionResult> GetTenantSummary([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (toUtc <= fromUtc)
            {
                return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            }

            var tenantId = _tenantContext.TenantId;
            var tz = _tenantContext.Tenant.Timezone;

            var ticket = await _reports.GetTicketTotals(tenantId, fromUtc, toUtc);
            var revenueByKind = await _reports.GetRevenueByKind(tenantId, fromUtc, toUtc);
            var riders = await _reports.GetUniqueRiders(tenantId, fromUtc, toUtc);
            var disputes = await _reports.GetDisputeCount(tenantId, fromUtc, toUtc);
            var daily = await _reports.GetDailyRevenue(tenantId, fromUtc, toUtc, tz);
            var topEvents = await _reports.GetTopEvents(tenantId, fromUtc, toUtc);
            // "Passes" here means SEASON passes. The old day-pass subsystem was removed in
            // Script0118, and these two were left hardcoded to 0 / empty when it went, which is
            // why the tile read zero for a track that had sold passes all season.
            var passesSold = await _reports.GetSeasonPassesSold(tenantId, fromUtc, toUtc);
            var topPassProducts = await _reports.GetTopSeasonPassProducts(tenantId, fromUtc, toUtc);

            var summary = new TenantReportSummary
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                // All-kinds gross revenue from the ledger (tickets + passes + memberships + extras +
                // rentals + concessions), broken out by type. TicketsSold stays the event-ticket count.
                TotalRevenueCents = revenueByKind.Sum(r => r.RevenueCents),
                PassesSold = passesSold,
                TicketsSold = ticket.SoldCount,
                UniqueRiders = riders,
                RefundedCount = ticket.RefundedCount,
                CancelledCount = ticket.CancelledCount,
                DisputedCount = disputes,
                RefundedAmountCents = ticket.RefundedCents,
                RevenueByType = revenueByKind.Select(r => new RevenueByKindDto
                {
                    Kind = r.SourceKind,
                    RevenueCents = r.RevenueCents,
                    SaleCount = r.SaleCount,
                }).ToList(),
                DailyRevenue = daily.Select(MapDaily).ToList(),
                TopPassProducts = topPassProducts.Select(p => new TopProductDto
                {
                    ProductId = p.ProductId,
                    ProductName = p.ProductName,
                    SoldCount = p.SoldCount,
                    RevenueCents = p.RevenueCents,
                }).ToList(),
                TopEvents = topEvents.Select(e => new TopEventDto
                {
                    EventId = e.EventId,
                    EventTitle = e.EventTitle,
                    EventStartUtc = DateTime.SpecifyKind(e.EventStartUtc, DateTimeKind.Utc),
                    SoldCount = e.SoldCount,
                    RevenueCents = e.RevenueCents,
                }).ToList(),
            };

            return new ApiResponses().OkResult(summary);
        }

        // ── Admission / amusement tax collected ─────────────────────────────────
        // Tax the tenant collected on event admissions in the range, so they can remit it. Net =
        // collected minus tax on refunded tickets. Excludes concession sales tax (a separate report).
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/AdmissionTax")]
        public async Task<IActionResult> GetAdmissionTax([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");

            var totals = await _reports.GetAdmissionTaxTotals(_tenantContext.TenantId, fromUtc, toUtc);
            var cfg = await _tax.GetByKind(_tenantContext.TenantId, "admission");
            return new ApiResponses().OkResult(new AdmissionTaxReport
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                TaxCollectedCents = totals.TaxCollectedCents,
                RefundedTaxCents = totals.RefundedTaxCents,
                NetTaxCents = totals.TaxCollectedCents - totals.RefundedTaxCents,
                TaxableSalesCents = totals.TaxableSalesCents,
                TaxedTicketCount = totals.TaxedTicketCount,
                CurrentRateBps = cfg?.RateBps ?? 0,
                JurisdictionLabel = cfg?.JurisdictionLabel,
            });
        }

        // ── End of Day (Z report) ───────────────────────────────────────────────
        // The single-day close for one tenant-local business date.
        //
        // Sourced entirely from v_accounting_entries, the read model QuickBooksSyncService posts
        // that day's journal entry from, and bucketed with the same QboAccountKeys function
        // JournalEntryBuilder uses. That is the point of the report: what the admin closes the day
        // on and what their accountant opens in QuickBooks are the same numbers, and they cannot
        // drift, because there is only one source and one bucketing rule.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/EndOfDay")]
        public async Task<IActionResult> GetEndOfDay([FromQuery] string? date = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryResolveBusinessDate(date, out var businessDate))
                return new ApiResponses().BadRequestResult("Date must be a calendar date in yyyy-MM-dd form.");

            return new ApiResponses().OkResult(await BuildEndOfDay(businessDate));
        }

        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/EndOfDay/Csv")]
        public async Task<IActionResult> GetEndOfDayCsv([FromQuery] string? date = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryResolveBusinessDate(date, out var businessDate))
                return new ApiResponses().BadRequestResult("Date must be a calendar date in yyyy-MM-dd form.");

            var report = await BuildEndOfDay(businessDate);
            var bytes = EndOfDayCsvBuilder.Build(report, _tenantContext.Tenant.DisplayName);
            // No audit row here, unlike the Trackside export: this file carries money totals and
            // staff names, not a list of riders' emails and phone numbers.
            return File(bytes, "text/csv", EndOfDayCsvBuilder.FilenameFor(report.BusinessDate));
        }

        // Empty/absent date means "today at the track", never today in UTC or on the admin's laptop.
        private bool TryResolveBusinessDate(string? date, out DateOnly businessDate)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                businessDate = DateOnly.FromDateTime(TenantLocalNow());
                return true;
            }
            return DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out businessDate);
        }

        private DateTime TenantLocalNow()
        {
            var iana = _tenantContext.Tenant.Timezone;
            if (string.IsNullOrWhiteSpace(iana)) return DateTime.UtcNow;
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(iana)); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return DateTime.UtcNow; }
        }

        private async Task<EndOfDayReportResponse> BuildEndOfDay(DateOnly businessDate)
        {
            var tenantId = _tenantContext.TenantId;
            var tz = string.IsNullOrWhiteSpace(_tenantContext.Tenant.Timezone) ? "UTC" : _tenantContext.Tenant.Timezone;

            var buckets = await _endOfDay.GetDayBuckets(tenantId, businessDate);
            var staffRows = await _endOfDay.GetDayStaff(tenantId, businessDate);
            var sessions = await _endOfDay.GetDayCashSessions(tenantId, businessDate, tz);
            var turnIns = await _endOfDay.GetDayCashTurnIns(tenantId, businessDate, tz);

            // Revenue is sale + refund. Every other entry kind is money that moved without being
            // earned (gift-card sales, deposits, chargebacks, RidePass's own charges) and is
            // reported under totals instead. 'adjustment' is allowed by the ledger's CHECK
            // constraint but has no writer anywhere in the codebase today; if one is ever added it
            // needs a home here, or it will close the day invisibly.
            var saleAndRefund = buckets.Where(b => b.EntryKind is "sale" or "refund").ToList();

            // ── Revenue, bucketed exactly the way the journal entry is ──────────────────
            var byKey = new Dictionary<string, EndOfDayRevenueLine>(StringComparer.Ordinal);
            foreach (var b in saleAndRefund)
            {
                // EffectiveRevenueKey, not RevenueForSourceKind: an event type can name its own
                // slot, which is how a Training Center's lessons and camps get their own line here
                // instead of disappearing into the gate. Same call the journal entry makes, so this
                // table and the posted JE stay line-for-line identical.
                var key = QboAccountKeys.EffectiveRevenueKey(b.SourceKind, b.RevenueKeyOverride);
                if (!byKey.TryGetValue(key, out var line))
                {
                    line = new EndOfDayRevenueLine { Key = key, Label = QboAccountKeys.Label(key) };
                    byKey[key] = line;
                }
                if (b.EntryKind == "sale")
                {
                    line.SaleCount += b.EntryCount;
                    line.GrossCents += b.GrossCents;
                }
                else
                {
                    line.RefundCount += b.EntryCount;
                    line.RefundCents += b.GrossCents;   // already negative in the ledger
                }
                line.TaxCents += b.TaxCents;
                line.TipCents += b.TipCents;
            }
            foreach (var line in byKey.Values)
            {
                line.NetGrossCents = line.GrossCents + line.RefundCents;
                line.NetRevenueCents = line.NetGrossCents - line.TaxCents - line.TipCents;
            }
            var revenue = byKey.Values
                .OrderBy(l => { var i = Array.IndexOf(QboAccountKeys.All, l.Key); return i < 0 ? int.MaxValue : i; })
                .ThenBy(l => l.Key, StringComparer.Ordinal)
                .ToList();

            long Sum(Func<AccountingBucketRow, bool> where, Func<AccountingBucketRow, long> pick) =>
                buckets.Where(where).Sum(pick);

            var grossSales = Sum(b => b.EntryKind == "sale", b => b.GrossCents);
            var refunds = Sum(b => b.EntryKind == "refund", b => b.GrossCents);
            var tax = saleAndRefund.Sum(b => b.TaxCents);
            var tips = saleAndRefund.Sum(b => b.TipCents);

            var totals = new EndOfDayTotals
            {
                GrossSalesCents = grossSales,
                RefundsCents = refunds,
                NetSalesCents = grossSales + refunds,
                TaxCents = tax,
                TipsCents = tips,
                NetRevenueCents = grossSales + refunds - tax - tips,

                GiftCardsSoldCents = Sum(b => b.EntryKind == "gift_card_sold", b => b.GrossCents),
                // Redemptions on SALES. A refund row carries a negative gift-card proration, which
                // nets inside the gift-card tender line below but is deliberately left out of this
                // headline figure, which answers "how much stored value was spent here today".
                GiftCardsRedeemedCents = Sum(b => b.EntryKind == "sale", b => b.GiftCardAppliedCents),
                DepositsCollectedCents = Sum(b => b.EntryKind == "deposit_collected", b => b.GrossCents),
                DepositsReleasedCents = Sum(b => b.EntryKind == "deposit_released", b => b.GrossCents),
                // A chargeback reverses the original sale, so its gross is negative.
                DisputeLossCents = Sum(b => b.EntryKind == "dispute_loss", b => b.GrossCents),
                // The chargeback fee rides stripe_fee_cents; a dispute_fee row's gross is 0.
                DisputeFeeCents = Sum(b => b.EntryKind == "dispute_fee", b => b.StripeFeeCents),
                // SMS/email charges are written with a NEGATIVE gross (money out). Flipped to a
                // positive here so the screen can label it plainly as a cost.
                PlatformChargesCents = -Sum(b => b.EntryKind is "sms_charge" or "email_charge", b => b.GrossCents),

                // Fees on the trading rows only. The chargeback fee is reported on its own line
                // above rather than folded in here, so nothing is counted twice.
                StripeFeesCents = saleAndRefund.Sum(b => b.StripeFeeCents),
                RidepassFeesCents = saleAndRefund.Sum(b => b.RidepassCutCents),
                NetToTenantCents = saleAndRefund.Sum(b => b.NetToTenantCents),

                TransactionCount = buckets.Where(b => b.EntryKind == "sale").Sum(b => b.EntryCount),
                RefundCount = buckets.Where(b => b.EntryKind == "refund").Sum(b => b.EntryCount),
            };

            // ── Tenders: the same money cut by how it was PAID ──────────────────────────
            // A sale settled partly on a gift card contributes to two buckets, so the card and cash
            // lines take gross MINUS the gift-funded part and the gift-card line takes that part.
            // Gift-card PURCHASES are not here: they are not a sale, and they show under the other
            // movements section instead, which keeps the tenders summing to net sales.
            var cardMethods = new[] { "stripe", "stripe_direct", "stripe_connect" };
            var tenders = new List<EndOfDayTenderLine>
            {
                new()
                {
                    Method = "card", Label = "Card",
                    AmountCents = saleAndRefund.Where(b => cardMethods.Contains(b.PaymentMethod))
                                               .Sum(b => b.GrossCents - b.GiftCardAppliedCents),
                    Count = saleAndRefund.Where(b => cardMethods.Contains(b.PaymentMethod)).Sum(b => b.EntryCount),
                },
                new()
                {
                    Method = "cash", Label = "Cash",
                    AmountCents = saleAndRefund.Where(b => b.PaymentMethod == "cash")
                                               .Sum(b => b.GrossCents - b.GiftCardAppliedCents),
                    Count = saleAndRefund.Where(b => b.PaymentMethod == "cash").Sum(b => b.EntryCount),
                },
                new()
                {
                    Method = "gift_card", Label = "Gift card",
                    // Every payment method can carry a gift-card portion: 'voucher' when the card
                    // covered the whole sale, an ordinary card/cash method when it covered part.
                    AmountCents = saleAndRefund.Sum(b => b.GiftCardAppliedCents),
                    // GiftCardEntryCount, not EntryCount on buckets whose gift-card SUM is
                    // nonzero: the other tenders filter on payment_method, which is a grouping
                    // key, so their whole bucket qualifies. Gift-card use is not a grouping key,
                    // so it has to be counted inside the aggregate.
                    Count = saleAndRefund.Sum(b => b.GiftCardEntryCount),
                },
                new()
                {
                    Method = "credit", Label = "Store credit",
                    AmountCents = saleAndRefund.Where(b => b.PaymentMethod == "credit").Sum(b => b.GrossCents),
                    Count = saleAndRefund.Where(b => b.PaymentMethod == "credit").Sum(b => b.EntryCount),
                },
            };

            var cash = new EndOfDayCashSection
            {
                Sessions = sessions.Select(s => new EndOfDayCashSessionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    UserName = string.IsNullOrWhiteSpace(s.UserName) ? "Unknown" : s.UserName,
                    EventTitle = s.EventTitle,
                    DeviceId = s.DeviceId,
                    OpeningFloatCents = s.OpeningFloatCents,
                    Status = s.Status,
                    OpenedAtUtc = DateTime.SpecifyKind(s.OpenedAt.ToUniversalTime(), DateTimeKind.Utc),
                    ClosedAtUtc = s.ClosedAt.HasValue
                        ? DateTime.SpecifyKind(s.ClosedAt.Value.ToUniversalTime(), DateTimeKind.Utc) : null,
                }).ToList(),
                TurnIns = turnIns.Select(t => new EndOfDayCashTurnInDto
                {
                    Id = t.Id,
                    WorkerName = string.IsNullOrWhiteSpace(t.WorkerName) ? "Unknown" : t.WorkerName,
                    ManagerName = t.ManagerName,
                    ExpectedCents = t.ExpectedCents,
                    WorkerCountedCents = t.WorkerCountedCents,
                    ManagerCountedCents = t.ManagerCountedCents,
                    VarianceCents = t.VarianceCents,
                    Status = t.Status,
                    Note = t.Note,
                    SubmittedAtUtc = DateTime.SpecifyKind(t.SubmittedAt.ToUniversalTime(), DateTimeKind.Utc),
                    ConfirmedAtUtc = t.ConfirmedAt.HasValue
                        ? DateTime.SpecifyKind(t.ConfirmedAt.Value.ToUniversalTime(), DateTimeKind.Utc) : null,
                }).ToList(),
                OpeningFloatCents = sessions.Sum(s => s.OpeningFloatCents),
                WorkerCountedCents = turnIns.Sum(t => t.WorkerCountedCents),
                ManagerCountedCents = turnIns.Sum(t => t.ManagerCountedCents ?? 0),
                CashSalesCents = saleAndRefund.Where(b => b.PaymentMethod == "cash")
                                              .Sum(b => b.GrossCents - b.GiftCardAppliedCents),
            };

            return new EndOfDayReportResponse
            {
                BusinessDate = businessDate.ToString("yyyy-MM-dd"),
                Timezone = tz,
                GeneratedAtUtc = DateTime.UtcNow,
                Revenue = revenue,
                Totals = totals,
                Tenders = tenders,
                Staff = staffRows.Select(s => new EndOfDayStaffLine
                {
                    UserId = s.UserId,
                    Name = !string.IsNullOrWhiteSpace(s.Name) ? s.Name!
                         : !string.IsNullOrWhiteSpace(s.Email) ? s.Email! : "Unknown",
                    SaleCount = s.SaleCount,
                    RefundCount = s.RefundCount,
                    GrossCents = s.GrossCents,
                    CashCents = s.CashCents,
                }).ToList(),
                Cash = cash,
                QuickBooks = await BuildQuickBooksStatus(tenantId, businessDate),
            };
        }

        // Read-only view of the sync log. Nothing here posts, retries, or claims a business date;
        // the sweep and the QuickBooks settings screen own every write path.
        private async Task<EndOfDayQuickBooksStatus> BuildQuickBooksStatus(Guid tenantId, DateOnly businessDate)
        {
            var connection = await _quickBooks.GetConnection(tenantId);
            if (connection is null) return new EndOfDayQuickBooksStatus { Connected = false, Status = "not_connected" };

            var log = await _quickBooks.GetSyncLog(tenantId, businessDate);
            if (log is not null)
            {
                return new EndOfDayQuickBooksStatus
                {
                    Connected = true,
                    Status = log.Status,
                    DocNumber = log.QboDocNumber,
                    JournalEntryId = log.QboJournalEntryId,
                    SyncedAtUtc = log.SyncedAtUtc.HasValue
                        ? DateTime.SpecifyKind(log.SyncedAtUtc.Value, DateTimeKind.Utc) : null,
                    LastError = log.LastError,
                };
            }

            // No log row yet. Either the sweep has not reached this day, or it never will: sync is
            // switched off, or the date predates the connection's start cursor, which the sweep
            // never walks back past.
            var willNeverPost = !connection.SyncEnabled || businessDate < connection.SyncStartDate;
            return new EndOfDayQuickBooksStatus
            {
                Connected = true,
                Status = willNeverPost ? "disabled" : "pending",
                LastError = connection.LastSyncError,
            };
        }

        // ── Sales tax collected, every revenue stream ───────────────────────────
        // The companion to AdmissionTax above, which reads event_ticket_purchase directly and so
        // only ever answers the admissions question. This one reads v_accounting_entries, so food
        // and beverage, bike shop, rentals and everything else are in it, split by the same
        // QuickBooks account slots the journal entry uses.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/SalesTax")]
        public async Task<IActionResult> GetSalesTax([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");

            var tenantId = _tenantContext.TenantId;
            var tz = string.IsNullOrWhiteSpace(_tenantContext.Tenant.Timezone) ? "UTC" : _tenantContext.Tenant.Timezone;
            var rows = await _endOfDay.GetSalesTaxBuckets(tenantId, fromUtc, toUtc);

            // Only rows that actually carried tax count toward taxable sales and the taxed-sale
            // count. A zero-tax row is a sale in a jurisdiction (or of a kind) that is not taxed,
            // and including its gross would inflate the base the rate is checked against.
            //
            // The taxed counts and taxed gross come from the aggregate's own FILTERed columns, not
            // from testing the bucket's summed TaxCents: a bucket mixes taxed and untaxed rows, so
            // testing the sum and then taking EntryCount / GrossCents would credit every untaxed
            // row in it as a taxed sale. Same bug the gift-card tender count had.
            var taxed = rows.Where(r => r.TaxedEntryCount > 0).ToList();

            var byCategory = taxed
                .GroupBy(r => QboAccountKeys.EffectiveRevenueKey(r.SourceKind, r.RevenueKeyOverride), StringComparer.Ordinal)
                .Select(g => new SalesTaxCategoryRow
                {
                    Key = g.Key,
                    Label = QboAccountKeys.Label(g.Key),
                    TaxCents = g.Sum(r => r.TaxCents),
                    CollectedTaxCents = g.Where(r => r.EntryKind == "sale").Sum(r => r.TaxCents),
                    RefundedTaxCents = g.Where(r => r.EntryKind == "refund").Sum(r => r.TaxCents),
                    TaxableSalesCents = g.Sum(r => r.TaxedGrossCents),
                    SaleCount = g.Where(r => r.EntryKind == "sale").Sum(r => r.TaxedEntryCount),
                })
                .OrderBy(c => { var i = Array.IndexOf(QboAccountKeys.All, c.Key); return i < 0 ? int.MaxValue : i; })
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .ToList();

            var byDay = taxed
                .GroupBy(r => r.BusinessDate)
                .Select(g => new SalesTaxDayRow
                {
                    BusinessDate = g.Key.ToString("yyyy-MM-dd"),
                    TaxCents = g.Sum(r => r.TaxCents),
                    CollectedTaxCents = g.Where(r => r.EntryKind == "sale").Sum(r => r.TaxCents),
                    RefundedTaxCents = g.Where(r => r.EntryKind == "refund").Sum(r => r.TaxCents),
                    TaxableSalesCents = g.Sum(r => r.TaxedGrossCents),
                    SaleCount = g.Where(r => r.EntryKind == "sale").Sum(r => r.TaxedEntryCount),
                })
                .OrderBy(d => d.BusinessDate, StringComparer.Ordinal)
                .ToList();

            return new ApiResponses().OkResult(new SalesTaxReport
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                Timezone = tz,
                NetTaxCents = taxed.Sum(r => r.TaxCents),
                CollectedTaxCents = taxed.Where(r => r.EntryKind == "sale").Sum(r => r.TaxCents),
                RefundedTaxCents = taxed.Where(r => r.EntryKind == "refund").Sum(r => r.TaxCents),
                TaxableSalesCents = taxed.Sum(r => r.TaxedGrossCents),
                TaxedSaleCount = taxed.Where(r => r.EntryKind == "sale").Sum(r => r.TaxedEntryCount),
                ByCategory = byCategory,
                ByDay = byDay,
            });
        }

        // ── Revenue by department ───────────────────────────────────────────────
        // The P&L view of the same money the End of Day report closes a single day on: the QBO
        // revenue slots, rolled up into the business units an owner actually thinks in.
        //
        // Everything here bucket by bucket agrees with the posted journal entry, because it uses
        // the same two functions the sync does: EffectiveRevenueKey picks the slot (so a track's
        // lessons and camps land in Training Center rather than at the gate) and QboDepartments
        // groups the slots. Nothing about the split is computed twice.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/RevenueByDepartment")]
        public async Task<IActionResult> GetRevenueByDepartment([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");

            var tenantId = _tenantContext.TenantId;
            var tz = string.IsNullOrWhiteSpace(_tenantContext.Tenant.Timezone) ? "UTC" : _tenantContext.Tenant.Timezone;
            var rows = await _endOfDay.GetRevenueBuckets(tenantId, fromUtc, toUtc);

            // Net revenue is gross minus tax minus tips, the same identity JournalEntryBuilder
            // credits revenue with. Tax is the jurisdiction's and tips are staff's; neither was
            // ever earned by a department, so neither may reach a department's revenue line.
            static long Net(IEnumerable<RevenueBucketRow> g) =>
                g.Sum(r => r.GrossCents - r.TaxCents - r.TipCents);

            var categories = rows
                .GroupBy(r => QboAccountKeys.EffectiveRevenueKey(r.SourceKind, r.RevenueKeyOverride), StringComparer.Ordinal)
                .Select(g => new RevenueCategoryRow
                {
                    Key = g.Key,
                    Label = QboAccountKeys.Label(g.Key),
                    NetRevenueCents = Net(g),
                    GrossCents = g.Sum(r => r.GrossCents),
                    TaxCents = g.Sum(r => r.TaxCents),
                    TipCents = g.Sum(r => r.TipCents),
                    RefundCents = g.Where(r => r.EntryKind == "refund").Sum(r => r.GrossCents),
                    SaleCount = g.Where(r => r.EntryKind == "sale").Sum(r => r.EntryCount),
                    RefundCount = g.Where(r => r.EntryKind == "refund").Sum(r => r.EntryCount),
                })
                .ToList();

            var totalNet = categories.Sum(c => c.NetRevenueCents);

            var departments = categories
                .GroupBy(c => QboDepartments.ForRevenueKey(c.Key), StringComparer.Ordinal)
                .Select(g => new RevenueDepartmentRow
                {
                    Key = g.Key,
                    Label = QboDepartments.Label(g.Key),
                    NetRevenueCents = g.Sum(c => c.NetRevenueCents),
                    GrossCents = g.Sum(c => c.GrossCents),
                    TaxCents = g.Sum(c => c.TaxCents),
                    TipCents = g.Sum(c => c.TipCents),
                    RefundCents = g.Sum(c => c.RefundCents),
                    SaleCount = g.Sum(c => c.SaleCount),
                    RefundCount = g.Sum(c => c.RefundCount),
                    // Signed denominator, so a department that refunded more than it sold reads
                    // negative rather than being quietly clamped. Guarded against a period whose
                    // net is exactly zero, which is division by zero and not a meaningful share.
                    PctOfTotal = totalNet == 0
                        ? 0m
                        : Math.Round(g.Sum(c => c.NetRevenueCents) * 100m / totalNet, 1),
                    Categories = g
                        .OrderByDescending(c => c.NetRevenueCents)
                        .ThenBy(c => c.Key, StringComparer.Ordinal)
                        .ToList(),
                })
                // A track with no bike shop simply never sees a bike shop heading. A department
                // that only refunded in the period is NOT empty and stays visible.
                .Where(d => d.SaleCount != 0 || d.RefundCount != 0 || d.GrossCents != 0)
                .OrderBy(d => { var i = Array.IndexOf(QboDepartments.All, d.Key); return i < 0 ? int.MaxValue : i; })
                .ToList();

            return new ApiResponses().OkResult(new RevenueByDepartmentReport
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                Timezone = tz,
                NetRevenueCents = totalNet,
                GrossCents = rows.Sum(r => r.GrossCents),
                TaxCents = rows.Sum(r => r.TaxCents),
                TipCents = rows.Sum(r => r.TipCents),
                RefundCents = rows.Where(r => r.EntryKind == "refund").Sum(r => r.GrossCents),
                SaleCount = rows.Where(r => r.EntryKind == "sale").Sum(r => r.EntryCount),
                RefundCount = rows.Where(r => r.EntryKind == "refund").Sum(r => r.EntryCount),
                Departments = departments,
            });
        }

        private static DailyRevenuePointDto MapDaily(DailyRevenuePoint p) => new()
        {
            Date = p.Date,
            RevenueCents = p.RevenueCents,
            PassesSold = p.PassesSold,
            TicketsSold = p.TicketsSold,
        };

        // ── Food & Beverage profitability ───────────────────────────────────────
        // Revenue, theoretical COGS (from recipes), and margin for concession sales in a range, broken
        // out by item, category, payment method, and hour of day. Paid sales only; refunds reported apart.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/ConcessionProfitability")]
        public async Task<IActionResult> GetConcessionProfitability([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");

            var tenantId = _tenantContext.TenantId;
            var tz = _tenantContext.Tenant.Timezone;

            var agg = await _concessions.GetSalesAggregate(tenantId, fromUtc, toUtc);
            var cogs = await _concessions.GetCogsTotal(tenantId, fromUtc, toUtc);
            var refunds = await _concessions.GetRefundAggregate(tenantId, fromUtc, toUtc);
            var payments = await _concessions.GetPaymentBreakdown(tenantId, fromUtc, toUtc);
            var items = await _concessions.GetItemProfitability(tenantId, fromUtc, toUtc);
            var categories = await _concessions.GetCategoryProfitability(tenantId, fromUtc, toUtc);
            var hours = await _concessions.GetHourlyProfitability(tenantId, fromUtc, toUtc, tz);

            var grossProfit = agg.NetSalesCents - cogs;
            var report = new ConcessionProfitabilityReport
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                NetSalesCents = agg.NetSalesCents,
                TaxCents = agg.TaxCents,
                TipsCents = agg.TipCents,
                GrossSalesCents = agg.TotalCents,
                CogsCents = cogs,
                GrossProfitCents = grossProfit,
                MarginPct = Margin(grossProfit, agg.NetSalesCents),
                OrderCount = agg.OrderCount,
                AvgOrderValueCents = agg.OrderCount > 0 ? agg.TotalCents / agg.OrderCount : 0,
                RefundedCount = refunds.RefundedCount,
                RefundedAmountCents = refunds.RefundedAmountCents,
                Items = items.Select(i => new ConcessionProfitabilityReport.ItemRow
                {
                    Name = i.Name,
                    QtySold = i.QtySold,
                    RevenueCents = i.RevenueCents,
                    CogsCents = i.CogsCents,
                    ProfitCents = i.RevenueCents - i.CogsCents,
                    MarginPct = Margin(i.RevenueCents - i.CogsCents, i.RevenueCents),
                }).ToList(),
                Categories = categories.Select(c => new ConcessionProfitabilityReport.CategoryRow
                {
                    Category = c.Category,
                    RevenueCents = c.RevenueCents,
                    CogsCents = c.CogsCents,
                    ProfitCents = c.RevenueCents - c.CogsCents,
                    MarginPct = Margin(c.RevenueCents - c.CogsCents, c.RevenueCents),
                }).ToList(),
                Payments = payments.Select(p => new ConcessionProfitabilityReport.PaymentRow
                {
                    Method = p.PaymentMethod,
                    Count = p.SaleCount,
                    AmountCents = p.AmountCents,
                }).ToList(),
                Hours = hours.Select(h => new ConcessionProfitabilityReport.HourRow
                {
                    Hour = h.Hour,
                    RevenueCents = h.RevenueCents,
                    OrderCount = h.OrderCount,
                }).ToList(),
            };
            return new ApiResponses().OkResult(report);
        }

        // Margin % of a revenue base, rounded to one decimal; 0 when there's no revenue.
        private static double Margin(long profitCents, long baseCents)
            => baseCents > 0 ? Math.Round(profitCents * 100.0 / baseCents, 1) : 0;

        // ── Food & Beverage sales by employee ────────────────────────────────────
        // Per-seller F&B totals (sales, tender, tips, refunds) over a range, for staff accountability.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/ConcessionEmployees")]
        public async Task<IActionResult> GetConcessionEmployees([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");

            var rows = await _concessions.GetEmployeeSales(_tenantContext.TenantId, fromUtc, toUtc);
            var report = new ConcessionEmployeeReport
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                Rows = rows.Select(r => new ConcessionEmployeeReport.Row
                {
                    UserId = r.UserId,
                    Name = string.IsNullOrWhiteSpace(r.Name) ? "Unattributed" : r.Name,
                    OrdersCount = r.OrdersCount,
                    GrossSalesCents = r.GrossSalesCents,
                    NetSalesCents = r.NetSalesCents,
                    TaxCents = r.TaxCents,
                    TipCents = r.TipCents,
                    CashCents = r.CashCents,
                    CardCents = r.CardCents,
                    RefundedCount = r.RefundedCount,
                    RefundedCents = r.RefundedCents,
                    AvgOrderValueCents = r.OrdersCount > 0 ? r.GrossSalesCents / r.OrdersCount : 0,
                }).ToList(),
            };
            return new ApiResponses().OkResult(report);
        }

        // ── Rider Report (Admission) ────────────────────────────────────────
        // Date-range roll call across every event in the window: tickets + season-pass
        // reservations with check-in state, linked wristband, and waiver coverage.
        // Server-capped; the UI narrows the range when truncated.
        private const int RiderReportCap = 10000;

        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/Riders")]
        public async Task<IActionResult> GetRiders([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc,
            [FromQuery] string? search, [FromQuery] string audience = "rider",
            [FromQuery] string? purchaseTypes = null, [FromQuery] string? eventTypeCodes = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("The date range is empty.");
            if (audience is not ("rider" or "spectator"))
                return new ApiResponses().BadRequestResult("audience must be 'rider' or 'spectator'.");
            // Comma-separated so the filters stay bookmarkable. Unknown purchase types are rejected
            // rather than ignored: silently returning everything would read as "no such riders".
            var types = SplitCsv(purchaseTypes);
            var unknown = types.Where(t => !RiderPurchaseTypes.All.Contains(t)).ToList();
            if (unknown.Count > 0)
                return new ApiResponses().BadRequestResult($"Unknown purchase type: {string.Join(", ", unknown)}.");
            var eventTypes = SplitCsv(eventTypeCodes);

            var rows = await _reports.GetRidersByRange(_tenantContext.TenantId,
                fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(), search, RiderReportCap + 1, audience,
                types, eventTypes);
            var truncated = rows.Count > RiderReportCap;
            if (truncated) rows = rows.Take(RiderReportCap).ToList();
            return new ApiResponses().OkResult(new RiderReportResponse
            {
                Rows = rows.Select(ToRiderItem).ToList(),
                Truncated = truncated,
                TotalRows = rows.Count,
                TotalCheckedIn = rows.Count(r => r.CheckedIn),
                TotalMissingWaiver = rows.Count(r => !r.WaiverSigned),
            });
        }

        private static List<string> SplitCsv(string? csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<string>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Distinct().ToList();

        // Whole years elapsed between two dates, birthday-accurate (not a 365-day division).
        private static int? AgeOn(DateTime? birthdate, DateTime onDate)
        {
            if (birthdate is null) return null;
            var age = onDate.Year - birthdate.Value.Year;
            if (onDate.Date < birthdate.Value.Date.AddYears(age)) age--;
            return age < 0 || age > 120 ? null : age;
        }

        // Drill-in for one rider (identified by account id and/or email from a report row):
        // what they're registered for (last year + upcoming) and the waivers they've signed.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/RiderDetail")]
        public async Task<IActionResult> GetRiderDetail([FromQuery] Guid? userId, [FromQuery] string? email,
            [FromQuery] string? name)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (userId is null && string.IsNullOrWhiteSpace(email))
                return new ApiResponses().BadRequestResult("A rider account or email is required to look up details.");
            var regs = await _reports.GetRiderRegistrations(_tenantContext.TenantId, userId, email);
            var waivers = await _reports.GetRiderWaivers(_tenantContext.TenantId, userId, email);
            // The profile block reads identity off the (platform-wide) account, so it is only
            // resolved for someone with an actual footprint at THIS tenant. Both lookups above are
            // tenant-scoped, so no footprint means a hand-typed email can't be used to pull a
            // stranger's phone, hometown, or birthdate out of another track's customer base.
            var hasTenantFootprint = regs.Count > 0 || waivers.Count > 0;
            var profile = hasTenantFootprint
                ? await _reports.GetRiderProfile(_tenantContext.TenantId, userId, email)
                : null;
            return new ApiResponses().OkResult(new RiderDetailResponse
            {
                RiderName = name ?? regs.FirstOrDefault()?.RiderName ?? email ?? "",
                Email = email,
                Profile = profile is null ? null : new RiderProfileItem
                {
                    UserId = profile.UserId,
                    Email = profile.Email,
                    Phone = profile.Phone,
                    Hometown = profile.Hometown,
                    RaceNumber = profile.RaceNumber,
                    BirthdateUtc = profile.Birthdate.HasValue
                        ? DateTime.SpecifyKind(profile.Birthdate.Value, DateTimeKind.Utc) : null,
                    Age = AgeOn(profile.Birthdate, DateTime.UtcNow),
                    MemberSinceUtc = profile.MemberSinceUtc.HasValue
                        ? DateTime.SpecifyKind(profile.MemberSinceUtc.Value, DateTimeKind.Utc) : null,
                    Bike = profile.Bike,
                    EmergencyContactName = profile.EmergencyContactName,
                    EmergencyContactPhone = profile.EmergencyContactPhone,
                    ParentGuardianName = profile.ParentGuardianName,
                    TotalRegistrations = profile.TotalRegistrations,
                    TotalCheckedIn = profile.TotalCheckedIn,
                    TotalSpentCents = profile.TotalSpentCents,
                    FirstVisitUtc = profile.FirstVisitUtc.HasValue
                        ? DateTime.SpecifyKind(profile.FirstVisitUtc.Value, DateTimeKind.Utc) : null,
                    LastVisitUtc = profile.LastVisitUtc.HasValue
                        ? DateTime.SpecifyKind(profile.LastVisitUtc.Value, DateTimeKind.Utc) : null,
                    IsGuest = profile.UserId is null,
                },
                Registrations = regs.Select(ToRiderItem).ToList(),
                Waivers = waivers.Select(w => new RiderWaiverItem
                {
                    Id = w.Id,
                    WaiverName = w.WaiverName,
                    WaiverVersion = w.WaiverVersion,
                    SignedAtUtc = DateTime.SpecifyKind(w.SignedAtUtc, DateTimeKind.Utc),
                    SignedByParent = w.SignedByParent,
                    ParentName = w.ParentName,
                    SignerName = w.SignerName,
                    WaiverIsCurrent = w.WaiverIsCurrent,
                    HasSignatureImage = w.HasSignatureImage,
                }).ToList(),
            });
        }

        // One waiver's signature image, fetched only when an admin opens that signature. Kept off
        // the drill-in payload because each image is a base64 data URL and a regular accumulates
        // one per season.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/RiderWaiver/{signatureId:guid}/Signature")]
        public async Task<IActionResult> GetRiderWaiverSignature(Guid signatureId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var image = await _reports.GetWaiverSignatureImage(_tenantContext.TenantId, signatureId);
            if (string.IsNullOrWhiteSpace(image))
                return new ApiResponses().NotFoundResult("No signature image is stored for this waiver.");
            return new ApiResponses().OkResult(new { signatureDataUrl = image });
        }

        private static RiderReportItem ToRiderItem(Services.Repositories.Data.ReportData.RiderReportRow r) => new()
        {
            PurchaseId = r.PurchaseId,
            Source = r.Source,
            EventId = r.EventId,
            EventTitle = r.EventTitle,
            EventStartsAtUtc = DateTime.SpecifyKind(r.EventStartsAtUtc, DateTimeKind.Utc),
            RiderName = r.RiderName,
            Email = r.Email,
            UserId = r.UserId,
            ItemName = r.ItemName,
            CheckedIn = r.CheckedIn,
            CheckedInAtUtc = r.CheckedInAtUtc.HasValue ? DateTime.SpecifyKind(r.CheckedInAtUtc.Value, DateTimeKind.Utc) : null,
            WristbandCode = r.WristbandCode,
            WaiverSigned = r.WaiverSigned,
            PurchaseType = r.PurchaseType,
            EventTypeName = r.EventTypeName,
            EventTypeCode = r.EventTypeCode,
            RegistrationComplete = r.RegistrationComplete,
            // Age ON THE EVENT DAY, not today: a 17-year-old who rode last season was a minor for
            // that entry's waiver, and that's what the report is being asked about.
            AgeAtEvent = AgeOn(r.RiderBirthdate, r.EventStartsAtUtc),
        };

        // ── Event Riders ────────────────────────────────────────────────────
        // Roll-call for one event: every paid registrant across pass / ticket /
        // season-pass-reservation, with their check-in status. Used for the gate
        // staff handout and the post-event "who actually showed?" view.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/EventRiders/{eventId:guid}")]
        public async Task<IActionResult> GetEventRiders(Guid eventId)
        {
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var rows = await _reports.GetEventRiders(_tenantContext.TenantId, eventId);
            var resp = new EventRiderReportResponse
            {
                EventId = ev.Id,
                EventTitle = ev.Title,
                EventStartsAtUtc = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc),
                TotalRegistrants = rows.Sum(r => r.Quantity),
                TotalCheckedIn = rows.Where(r => r.CheckedIn).Sum(r => r.Quantity),
                Rows = rows.Select(r =>
                {
                    var (first, last) = SplitName(r.FirstName, r.LastName, r.PurchaserName);
                    return new EventRiderRowDto
                    {
                        PurchaseId = r.PurchaseId,
                        Source = r.Source,
                        PurchaserName = r.PurchaserName,
                        FirstName = first,
                        LastName = last,
                        PurchaserEmail = r.PurchaserEmail,
                        PurchaserPhone = r.PurchaserPhone,
                        ItemName = r.ItemName,
                        TierKind = r.TierKind,
                        TierAudience = r.TierAudience,
                        RaceNumber = r.RaceNumber,
                        UserRaceNumber = r.UserRaceNumber,
                        Hometown = r.Hometown,
                        Quantity = r.Quantity,
                        AmountCents = r.AmountCents,
                        Status = r.Status,
                        CheckedIn = r.CheckedIn,
                        CheckedInAtUtc = r.CheckedInAtUtc.HasValue
                            ? DateTime.SpecifyKind(r.CheckedInAtUtc.Value, DateTimeKind.Utc) : null,
                        CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc),
                    };
                }).ToList(),
            };
            return new ApiResponses().OkResult(resp);
        }

        // "Who has signed" report: every event-ticket attendee for one event and their waiver signing
        // status, read from the normalized signature store (counter + online sales unified).
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/Events/{eventId:guid}/WaiverSignatures")]
        public async Task<IActionResult> GetEventWaiverSignatures(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var rows = await _reports.GetEventWaiverSignatures(_tenantContext.TenantId, eventId);
            var resp = new EventWaiverSignatureReportResponse
            {
                EventId = ev.Id,
                EventTitle = ev.Title,
                EventStartsAtUtc = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc),
                TotalAttendees = rows.Count,
                TotalSigned = rows.Count(r => !r.WaiverRequired || r.WaiverSigned),
                Rows = rows.Select(r => new EventWaiverSignatureRowDto
                {
                    PurchaseId = r.PurchaseId,
                    AttendeeName = r.AttendeeName,
                    Audience = r.Audience,
                    TierName = r.TierName,
                    RaceNumber = r.RaceNumber,
                    Status = r.Status,
                    RegistrationComplete = r.RegistrationComplete,
                    WaiverRequired = r.WaiverRequired,
                    WaiverSigned = r.WaiverSigned,
                    SignedAtUtc = r.SignedAtUtc.HasValue ? DateTime.SpecifyKind(r.SignedAtUtc.Value, DateTimeKind.Utc) : null,
                    SignedByParent = r.SignedByParent,
                    ParentGuardianName = r.ParentGuardianName,
                    SignerName = r.SignerName,
                }).ToList(),
            };
            return new ApiResponses().OkResult(resp);
        }

        // ── Per-row actions for the Event Riders report ─────────────────────
        // SalesRedeem permission so any staff member running the gate / pit
        // tent can flip these fields. (ReportsView is read-only.)
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPut("Admin/EventRiders/{purchaseId:guid}/CheckIn")]
        public async Task<IActionResult> SetCheckIn(Guid purchaseId, [FromBody] SetCheckInRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var staffId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var tenantId = _tenantContext.TenantId;
            switch (req.Source)
            {
                case "event_ticket":
                    if (req.CheckedIn)
                    {
                        // A required event waiver can't be skipped, even on the admin check-in toggle.
                        var t = await _tickets.GetById(purchaseId, tenantId);
                        if (t is not null)
                        {
                            var waiverBlock = await _waiverGate.BlockReasonForTicket(tenantId, t);
                            if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
                        }
                        // Only a 'paid' ticket may be checked in. TryMarkRedeemed enforces that in SQL,
                        // so a refunded/cancelled ticket can't be flipped to redeemed (which UndoRedeemed
                        // would then resurrect back to 'paid', making it sellable/refundable again).
                        var redeemed = await _tickets.TryMarkRedeemed(purchaseId, tenantId, staffId, DateTime.UtcNow);
                        if (!redeemed)
                            return new ApiResponses().BadRequestResult(
                                "This ticket can't be checked in. It may be refunded, cancelled, or already checked in.");
                    }
                    else await _tickets.UndoRedeemed(purchaseId, tenantId);
                    break;
                case "season_pass":
                    if (req.CheckedIn)
                    {
                        // A season-pass holder is a rider; enforce the event's rider waiver at check-in.
                        var ctx = await _seasonPasses.GetReservationForCheckIn(purchaseId, tenantId);
                        if (ctx is not null)
                        {
                            var waiverBlock = await _waiverGate.BlockReason(tenantId, ctx.EventId,
                                riderAudience: true, ctx.HolderUserId, ctx.HolderEmail, ctx.HolderName);
                            if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
                        }
                    }
                    var affected = await _seasonPasses.UpdateReservationStatus(purchaseId, tenantId,
                        req.CheckedIn ? "checked_in" : "reserved",
                        req.CheckedIn ? staffId : null);
                    if (req.CheckedIn && affected == 0)
                        return new ApiResponses().BadRequestResult(
                            "This pass can't be checked in. It may be refunded, cancelled, or already checked in.");
                    break;
                default:
                    return new ApiResponses().BadRequestResult("Unknown source.");
            }
            return new ApiResponses().OkResult(new { purchaseId, checkedIn = req.CheckedIn });
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPut("Admin/EventRiders/Ticket/{purchaseId:guid}/RaceNumber")]
        public async Task<IActionResult> SetRaceNumber(Guid purchaseId, [FromBody] SetRaceNumberRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Race number lives only on event_ticket_purchase — passes and
            // season-pass reservations don't carry one.
            var trimmed = string.IsNullOrWhiteSpace(req.RaceNumber) ? null : req.RaceNumber.Trim();
            await _tickets.SetRaceNumber(purchaseId, _tenantContext.TenantId, trimmed);
            return new ApiResponses().OkResult(new { purchaseId, raceNumber = trimmed });
        }

        // Send-now-or-schedule rider messages (SMS or email). When RunAtUtc is
        // null/past the request sends immediately and returns per-row results
        // (the old SendSms path). When RunAtUtc is in the future, we enqueue a
        // scheduled_task row keyed by EventId so the report's "Scheduled" panel
        // can list and cancel it, and the TaskRunner's dispatcher picks it up.
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("Admin/EventRiders/{eventId:guid}/SendMessage")]
        public async Task<IActionResult> SendRiderMessage(Guid eventId, [FromBody] SendRiderMessageRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var channel = (req.Channel ?? "sms").ToLowerInvariant();
            if (channel == "sms" && !_sms.IsConfiguredFor(_tenantContext.Tenant))
            {
                return new ApiResponses().BadRequestResult(
                    "SMS isn't configured for this tenant. Provision Twilio in Settings → SMS.");
            }
            if (channel == "email" && !_emailer.IsConfigured)
            {
                return new ApiResponses().BadRequestResult(
                    "Email isn't configured for this tenant. Add SMTP credentials in app settings.");
            }
            if (channel == "email" && string.IsNullOrWhiteSpace(req.Subject))
            {
                // Subject is optional but blank-stripped to enforce a real title;
                // the handler also defaults if we let it through, but better to
                // tell the admin while they still have the dialog open.
                return new ApiResponses().BadRequestResult("Email subject is required.");
            }

            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var runAt = req.RunAtUtc?.ToUniversalTime();
            // Treat "now or earlier" as immediate. A 60-second grace window
            // means clock skew between the admin's browser and the server
            // doesn't accidentally enqueue something they meant to send now.
            var isScheduled = runAt.HasValue && runAt.Value > DateTime.UtcNow.AddSeconds(60);

            if (!isScheduled)
            {
                var (sent, skipped) = await SendRiderMessageNow(eventId, req, channel, ev.Title);
                return new ApiResponses().OkResult(new SendRiderMessageResponse
                {
                    Sent = sent, Skipped = skipped.Count, SkippedNames = skipped,
                });
            }

            // Future-scheduled: enqueue and let the TaskRunner pick it up.
            var payload = new Services.Scheduling.Handlers.SendRiderMessagePayload
            {
                EventId = eventId,
                PurchaseIds = req.PurchaseIds,
                Channel = channel,
                Subject = string.IsNullOrWhiteSpace(req.Subject) ? null : req.Subject!.Trim(),
                Body = req.Body,
            };
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            Guid? createdBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var taskId = await _scheduledTasks.Enqueue(_tenantContext.TenantId, "send_rider_message",
                payloadJson, runAt!.Value, createdBy);
            return new ApiResponses().OkResult(new SendRiderMessageResponse
            {
                ScheduledTaskId = taskId,
                ScheduledRunAtUtc = DateTime.SpecifyKind(runAt.Value, DateTimeKind.Utc),
            });
        }

        // Pulled into a private helper so both the immediate and the scheduled
        // (when the TaskRunner runs it via the handler) paths share the same
        // per-row send logic shape. The TaskRunner uses its own copy in
        // SendRiderMessageHandler — they could be unified later via a shared
        // service.
        private async Task<(int Sent, List<string> Skipped)> SendRiderMessageNow(
            Guid eventId, SendRiderMessageRequest req, string channel, string eventTitle)
        {
            var rows = await _reports.GetEventRiders(_tenantContext.TenantId, eventId);
            var requested = req.PurchaseIds.ToHashSet();
            var targets = rows.Where(r => requested.Contains(r.PurchaseId)).ToList();
            var sent = 0;
            var skipped = new List<string>();
            var tenant = _tenantContext.Tenant;

            foreach (var row in targets)
            {
                bool ok;
                if (channel == "sms")
                {
                    var normalized = TwilioSmsSender.NormalizeE164(row.PurchaserPhone ?? "");
                    if (string.IsNullOrEmpty(normalized))
                    {
                        skipped.Add(row.PurchaserName);
                        continue;
                    }
                    ok = await _sms.Send(tenant, normalized, req.Body);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(row.PurchaserEmail))
                    {
                        skipped.Add(row.PurchaserName);
                        continue;
                    }
                    var subject = string.IsNullOrWhiteSpace(req.Subject)
                        ? $"Update from {tenant.DisplayName}"
                        : req.Subject!.Trim();
                    var html = BuildRiderMessageHtml(req.Body, tenant.DisplayName, eventTitle);
                    ok = await _emailer.Send(row.PurchaserEmail, subject, html, null, Services.Email.TenantEmailIdentity.For(tenant));
                }
                if (ok) sent++;
                else skipped.Add(row.PurchaserName);
            }
            return (sent, skipped);
        }

        // Same shell SendRiderMessageHandler uses — kept inline here so the
        // immediate path doesn't fan out to the scheduling layer just for HTML
        // wrapping. If a third caller needs it, lift into a shared formatter.
        private static string BuildRiderMessageHtml(string plainBody, string tenantName, string eventTitle)
        {
            var escaped = System.Net.WebUtility.HtmlEncode(plainBody).Replace("\n", "<br>");
            return $@"<!doctype html>
<html><body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1f2937;"">
    <div style=""font-size: 12px; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px;"">{System.Net.WebUtility.HtmlEncode(tenantName)}</div>
    <div style=""font-size: 18px; font-weight: 600; margin-top: 4px;"">{System.Net.WebUtility.HtmlEncode(eventTitle)}</div>
    <hr style=""border: none; border-top: 1px solid #e5e7eb; margin: 16px 0;"">
    <div style=""font-size: 15px; line-height: 1.55;"">{escaped}</div>
    <hr style=""border: none; border-top: 1px solid #e5e7eb; margin: 24px 0 16px 0;"">
    <div style=""font-size: 12px; color: #9ca3af;"">Sent from {System.Net.WebUtility.HtmlEncode(tenantName)}. Reply directly to reach the track.</div>
</body></html>";
        }

        // List pending scheduled rider-messages for one event so the admin can
        // see what's queued and cancel any of them. Only 'pending' rows show —
        // succeeded/failed are visible only via debug tools (kept off the
        // report to avoid clutter; can be exposed later if needed).
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpGet("Admin/EventRiders/{eventId:guid}/ScheduledMessages")]
        public async Task<IActionResult> ListScheduledRiderMessages(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");
            var rows = await _scheduledTasks.ListPendingForTenant(_tenantContext.TenantId, eventId);
            var items = rows
                .Where(r => r.Kind == "send_rider_message")
                .Select(r => new ScheduledTaskListItem
                {
                    Id = r.Id,
                    Kind = r.Kind,
                    RunAtUtc = DateTime.SpecifyKind(r.RunAtUtc, DateTimeKind.Utc),
                    Status = r.Status,
                    Summary = ExtractMessageSummary(r.Payload),
                    CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    CreatedByUserId = r.CreatedByUserId,
                });
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("Admin/ScheduledMessages/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelScheduledRiderMessage(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            Guid? actorId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            if (!actorId.HasValue) return new ApiResponses().BadRequestResult("Invalid token.");
            var existing = await _scheduledTasks.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Scheduled task not found.");
            await _scheduledTasks.Cancel(id, _tenantContext.TenantId, actorId.Value);
            return new ApiResponses().OkResult(new { id, status = "cancelled" });
        }

        // Best-effort summary for the listing card: "SMS to 12 (Race Day reminder)".
        // Parses the same SendRiderMessagePayload shape the handler uses.
        private static string? ExtractMessageSummary(string payloadJson)
        {
            try
            {
                var p = System.Text.Json.JsonSerializer.Deserialize<Services.Scheduling.Handlers.SendRiderMessagePayload>(
                    payloadJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (p is null) return null;
                var channelLabel = p.Channel == "email" ? "Email" : "Text";
                var preview = (p.Body ?? string.Empty).Trim();
                if (preview.Length > 60) preview = preview[..57] + "…";
                return $"{channelLabel} to {p.PurchaseIds.Count} — {preview}";
            }
            catch { return null; }
        }

        // CSV shaped to match MyLaps Trackside's import template — Number,
        // FirstName, LastName, Class, Hometown, Email, Phone. Only race-entry
        // rows are exported; spectator passes and pure pass purchases aren't
        // riders the timing software needs.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/EventRiders/{eventId:guid}/Export/Trackside")]
        public async Task<IActionResult> ExportTrackside(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.TracksideExportEnabled)
                return new ApiResponses().BadRequestResult(
                    "Trackside export is turned off for this venue. Enable it under Settings > Features first.");
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var rows = (await _reports.GetEventRiders(_tenantContext.TenantId, eventId))
                .Where(r => r.Source == "event_ticket" && r.TierKind == "race_entry")
                .ToList();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Number,FirstName,LastName,Class,Hometown,Email,Phone");
            foreach (var r in rows)
            {
                var (first, last) = SplitName(r.FirstName, r.LastName, r.PurchaserName);
                var number = !string.IsNullOrWhiteSpace(r.RaceNumber) ? r.RaceNumber : (r.UserRaceNumber ?? "");
                csv.AppendLine(string.Join(',', new[] {
                    CsvEscape(number),
                    CsvEscape(first),
                    CsvEscape(last),
                    CsvEscape(r.ItemName),
                    CsvEscape(r.Hometown ?? ""),
                    CsvEscape(r.PurchaserEmail),
                    CsvEscape(r.PurchaserPhone ?? ""),
                }));
            }
            var safeTitle = string.Concat((ev.Title ?? "event").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
            var filename = $"trackside-{safeTitle}-{DateTime.UtcNow:yyyyMMdd}.csv";

            // Bulk PII leaves the building here: name, email, phone and hometown for every racer at
            // an event, in a file. Individual report VIEWS are deliberately not audited (they are
            // high-volume and low-signal), but a download that can be forwarded or taken to a
            // competitor is worth a row, including how many people were in it.
            await _audit.Log(
                "report.export_trackside",
                $"Exported {rows.Count} rider records (name, email, phone) for \"{ev.Title}\"",
                targetKind: "event",
                targetId: eventId,
                tenantId: _tenantContext.TenantId,
                metadata: new { eventTitle = ev.Title, rowCount = rows.Count, filename });

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", filename);
        }

        // RFC 4180 minimal: quote when the value contains a quote, comma, or
        // line break; double internal quotes.
        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            // Neutralize spreadsheet formula injection: a leading = + - @ (or tab/CR) makes Excel and
            // Sheets evaluate the cell as a formula. Rider-controlled fields (name, hometown, email)
            // flow through here, so prefix a single quote to force the cell to be treated as text.
            if ("=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            var needsQuoting = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuoting) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // ── Daily Events ────────────────────────────────────────────────────
        // Caller passes the UTC half-open range for one local day in the tenant's
        // timezone (frontend computes that — matches the Summary endpoint pattern).
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/DailyEvents")]
        public async Task<IActionResult> GetDailyEvents(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            [FromQuery] string? localDate = null)
        {
            if (toUtc <= fromUtc)
            {
                return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            }
            var rows = await _reports.GetEventsInRange(_tenantContext.TenantId, fromUtc, toUtc);
            var resp = new DailyEventReportResponse
            {
                LocalDate = localDate ?? string.Empty,
                Rows = rows.Select(r => new DailyEventRowDto
                {
                    EventId = r.EventId,
                    Title = r.Title,
                    EventTypeName = r.EventTypeName ?? string.Empty,
                    StartsAtUtc = DateTime.SpecifyKind(r.StartsAtUtc, DateTimeKind.Utc),
                    EndsAtUtc = DateTime.SpecifyKind(r.EndsAtUtc, DateTimeKind.Utc),
                    AllDay = r.AllDay,
                    Capacity = r.Capacity,
                    Status = r.Status,
                    Registered = r.Registered,
                    CheckedIn = r.CheckedIn,
                    RevenueCents = r.RevenueCents,
                }).ToList(),
            };
            return new ApiResponses().OkResult(resp);
        }

        // ── Check-In lookup ─────────────────────────────────────────────────
        // Resolves any redemption token (pass / event_ticket / season_pass purchase)
        // to the rider, returning today + future registrations across all three sources
        // plus waiver / membership gating flags. Gate staff scan a QR, we hand back
        // everything they need to make a check-in decision in one call.
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpGet("Admin/CheckInLookup")]
        public async Task<IActionResult> CheckInLookup(
            [FromQuery] Guid token,
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc)
        {
            if (token == Guid.Empty) return new ApiResponses().BadRequestResult("Token is required.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");

            var data = await _reports.LookupCheckInByToken(_tenantContext.TenantId, token, fromUtc, toUtc);
            if (data is null) return new ApiResponses().NotFoundResult("No registration found for that token.");

            // Waiver gating: tenant has an active waiver AND any of the rider's
            // today registrations is on an event that requires it.
            var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
            data.RequiresWaiver = false;
            data.WaiverSigned = false;
            if (activeWaiver is not null && data.UserId.HasValue)
            {
                var sig = await _waivers.GetSignature(data.UserId.Value, activeWaiver.Id);
                data.WaiverSigned = sig is not null;
                // Look up each today-registration's event to see if it requires a waiver.
                // Check-in is rider-side, so we look at the rider waiver flag here.
                foreach (var r in data.TodayRegistrations)
                {
                    // A walk-up admission has no event, so there is no event waiver to require.
                    if (r.EventId is not Guid regEventId) continue;
                    var ev = await _events.GetById(regEventId, _tenantContext.TenantId);
                    if (ev is not null && ev.RequiresRiderWaiver) { data.RequiresWaiver = true; break; }
                }
            }

            // Membership gating: surface the flag whenever the tenant requires
            // membership for ANY guarded purchase kind. The UI shows a warning
            // when required-and-not-active so staff don't check in a lapsed member.
            var t = _tenantContext.Tenant;
            data.RequiresMembership = t.MembershipEnabled && t.MembershipPriceCents > 0
                && (t.MembershipRequiredForRiders || t.MembershipRequiredForSpectators);
            data.MembershipName = t.MembershipName;
            data.MembershipActive = false;
            if (data.RequiresMembership && data.UserId.HasValue)
            {
                var active = await _memberships.GetActive(data.UserId.Value, t.Id, DateTime.UtcNow);
                data.MembershipActive = active is not null;
            }

            // Map to wire DTO with explicit UTC kinds (Dapper hands back unspecified).
            var resp = new CheckInLookupResponse
            {
                UserId = data.UserId,
                PurchaserName = data.PurchaserName,
                PurchaserEmail = data.PurchaserEmail,
                PurchaserPhone = data.PurchaserPhone,
                PhotoDataUrl = data.PhotoDataUrl,
                MatchedTokenKind = data.MatchedTokenKind,
                RequiresWaiver = data.RequiresWaiver,
                WaiverSigned = data.WaiverSigned,
                RequiresMembership = data.RequiresMembership,
                MembershipActive = data.MembershipActive,
                MembershipName = data.MembershipName,
                TodayRegistrations = data.TodayRegistrations.Select(MapRegistration).ToList(),
                FutureRegistrations = data.FutureRegistrations.Select(MapRegistration).ToList(),
            };
            return new ApiResponses().OkResult(resp);
        }

        private static CheckInRegistrationDto MapRegistration(CheckInRegistration r) => new()
        {
            Id = r.Id,
            Source = r.Source,
            EventId = r.EventId,
            EventTitle = r.EventTitle,
            EventStartsAtUtc = DateTime.SpecifyKind(r.EventStartsAtUtc, DateTimeKind.Utc),
            EventEndsAtUtc = DateTime.SpecifyKind(r.EventEndsAtUtc, DateTimeKind.Utc),
            ItemName = r.ItemName,
            Status = r.Status,
            CheckedIn = r.CheckedIn,
            CheckedInAtUtc = r.CheckedInAtUtc.HasValue
                ? DateTime.SpecifyKind(r.CheckedInAtUtc.Value, DateTimeKind.Utc) : null,
            RedemptionToken = r.RedemptionToken,
        };

        // Trackside imports want first/last separately. Prefer the user-row
        // values when the rider has an account; otherwise best-effort split of
        // the typed-in purchaser name (last token = last name, rest = first).
        private static (string First, string Last) SplitName(string? first, string? last, string fullName)
        {
            if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
            {
                return (first?.Trim() ?? "", last?.Trim() ?? "");
            }
            var parts = (fullName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return ("", "");
            if (parts.Length == 1) return (parts[0], "");
            return (string.Join(' ', parts.Take(parts.Length - 1)), parts[^1]);
        }
    }
}
