using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Cash;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.CashData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Cash;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Cash reconciliation for the operator (gate) app.
    //
    // Workers (CashTurnIn) open a cash session and submit a BLIND-count turn-in: they
    // count without seeing the system's expected total. Managers (CashReconcile) confirm
    // receipt FROM THEIR OWN login and enter their count. A worker can never confirm their
    // own turn-in (segregation of duties), and CashReconcile is deliberately not in the
    // cashier permission set. The expected-vs-counted variance is produced by the
    // reconciliation report, not here.
    //
    // cash.turnin is its own permission rather than riding on sales.counter because a BIKE SHOP
    // cashier handles cash too but deliberately has no gate/F&B counter access. Their shop cash
    // already counts toward expected cash (attributed by sold_by_user_id), so without a turn-in
    // path they would accrue cash they could never hand in.
    //
    // Tips (Script0294, Script0296, Script0297): F&B staff are paid their tips in cash out of their
    // own drawer at close, the way Toast's Shift Review and Square's Cash Out work. A tip is
    // settled on exactly one turn-in: the close claims every unclaimed tip on the worker's paid
    // sales since the tenant's claims cutover (a tab closed after their last close-out included),
    // snapshots the numbers from those very rows, and logs every payout in tip_payout. A drawer
    // that cannot cover the tips leaves a house-owes balance a manager settles later (TipsOwed),
    // in cash or through payroll. Every payout writes a 'tip_payout' ledger row so the books see
    // it; expected drawer cash reads the turn-in's own tips_from_drawer_cents and the manager's
    // own cash settlements rather than the ledger (BuildReconciliation).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CashController : ControllerBase
    {
        // How far back the Tips owed page shows activity and payout history, and how many payout
        // rows it lists before the response says it was cut.
        private const int TipsOwedHistoryDays = 90;
        private const int TipsOwedPayoutCap = 500;

        private readonly ICashRepository _cash;
        private readonly ITenantLedgerRepository _ledger;
        private readonly IUserRepository _users;
        private readonly ITenantContext _tenantContext;
        private readonly IDbHelper _db;

        public CashController(ICashRepository cash, ITenantLedgerRepository ledger,
            IUserRepository users, ITenantContext tenantContext, IDbHelper db)
        {
            _cash = cash;
            _ledger = ledger;
            _users = users;
            _tenantContext = tenantContext;
            _db = db;
        }

        // ── Worker: sessions ─────────────────────────────────────────────────────

        [HttpPost("Session/Open")]
        [Authorize(Policy = TenantPermissions.Policy.CashTurnIn)]
        public async Task<IActionResult> OpenSession([FromBody] OpenCashSessionRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var userId = CurrentUserId();
            if (userId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            if (req.OpeningFloatCents < 0) return new ApiResponses().BadRequestResult("Opening float can't be negative.");

            var tenantId = _tenantContext.TenantId;
            // Get-or-create: at most one open session per worker per event (enforced by
            // uk_cash_session_open); re-opening just returns the existing one.
            var existing = await _cash.GetOpenSession(tenantId, userId.Value, req.EventId);
            if (existing is not null) return new ApiResponses().OkResult(existing);

            var id = await _cash.CreateSession(new CashSession
            {
                TenantId = tenantId,
                EventId = req.EventId,
                UserId = userId.Value,
                DeviceId = req.DeviceId,
                OpeningFloatCents = req.OpeningFloatCents,
                Status = "open",
            });
            var created = await _cash.GetSessionById(id, tenantId);
            return new ApiResponses().OkResult(created);
        }

        [HttpGet("Session/Current")]
        [Authorize(Policy = TenantPermissions.Policy.CashTurnIn)]
        public async Task<IActionResult> CurrentSession([FromQuery] Guid? eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var userId = CurrentUserId();
            if (userId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            // null body = no open session for this worker/event (a normal, expected state).
            var session = await _cash.GetOpenSession(_tenantContext.TenantId, userId.Value, eventId);
            return new ApiResponses().OkResult(session);
        }

        // ── Worker: shift review + turn-in ───────────────────────────────────────

        // The worker's own close-out numbers (Toast "Shift Review"): the tips their next turn-in
        // will settle (every unclaimed tip, with the part from before this session called out) and
        // anything the house still owes them from earlier shifts. Deliberately NOT expected cash,
        // which would defeat the blind count. The window is informational: the open session, or
        // the tenant's business day so far for a cashier who never opened a drawer. A preview only:
        // the turn-in recomputes everything from the rows it actually claims.
        [HttpGet("ShiftReview")]
        [Authorize(Policy = TenantPermissions.Policy.CashTurnIn)]
        public async Task<IActionResult> ShiftReview([FromQuery] Guid? eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var userId = CurrentUserId();
            if (userId is null) return new ApiResponses().BadRequestResult("No authenticated user.");

            var tenantId = _tenantContext.TenantId;
            var asOf = DateTime.UtcNow;
            var session = await _cash.GetOpenSession(tenantId, userId.Value, eventId);
            var windowFrom = session is not null
                ? TenantClock.AsUtc(session.OpenedAt)
                : TenantClock.BusinessDayStartUtc(_tenantContext.Tenant.Timezone, asOf);
            var tips = await _cash.GetWorkerTipTotals(tenantId, userId.Value, asOf, windowFrom,
                session is not null ? TenantClock.AsUtc(session.OpenedAt) : null);
            var owed = await _cash.GetTipsOwedRow(tenantId, userId.Value);

            return new ApiResponses().OkResult(new ShiftReviewResponse
            {
                Session = session,
                WindowFromUtc = windowFrom,
                WindowToUtc = asOf,
                TipsEarnedCents = tips.TipsEarnedCents,
                CardTipsCents = tips.CardTipsCents,
                CashSaleTipsCents = tips.CashSaleTipsCents,
                TipsFromEarlierShiftsCents = tips.TipsFromEarlierShiftsCents,
                SaleCount = tips.SaleCount,
                CashSaleCount = tips.CashSaleCount,
                OwedToYouCents = owed is null ? 0 : TipsOwedRowDto.From(owed).OwedCents,
            });
        }

        [HttpPost("TurnIn")]
        [Authorize(Policy = TenantPermissions.Policy.CashTurnIn)]
        public async Task<IActionResult> SubmitTurnIn([FromBody] SubmitTurnInRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var userId = CurrentUserId();
            if (userId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            if (req.WorkerCountedCents < 0) return new ApiResponses().BadRequestResult("Counted amount can't be negative.");
            if (req.TipsFromDrawerCents < 0) return new ApiResponses().BadRequestResult("Tips taken from the drawer can't be negative.");
            if (req.CashTipsDeclaredCents < 0) return new ApiResponses().BadRequestResult("Declared cash tips can't be negative.");

            var tenantId = _tenantContext.TenantId;
            var session = await _cash.GetOpenSession(tenantId, userId.Value, req.EventId);
            if (session is null) return new ApiResponses().BadRequestResult("No open cash session to turn in for this event.");

            // One close per session: a double-submit must not write two turn-ins (and pay the same
            // tips out of the drawer twice), so the session is re-read under a lock on its id. The
            // lock is keyed on the id we first saw; if the re-read comes back with a different open
            // session, the drawer was closed and reopened in the gap and this request's numbers
            // belong to a drawer that no longer exists.
            var lockedId = session.Id;
            await using var closeLock = await _db.AcquireAdvisoryLock($"cash-turnin:{lockedId}");
            session = await _cash.GetOpenSession(tenantId, userId.Value, req.EventId);
            if (session is null) return new ApiResponses().BadRequestResult("This cash session has already been turned in.");
            if (session.Id != lockedId) return new ApiResponses().ConflictResult("Your drawer changed; reopen Close out.");

            // The tips are never read here and never trusted from the client: the repository
            // claims the worker's unclaimed sales as of this instant and snapshots the turn-in
            // from the rows it claimed, inside the one transaction. The only check that belongs
            // here is the shape of the request; whether the drawer take fits what the claim found
            // is decided against the claim itself.
            var asOf = DateTime.UtcNow;
            var turnIn = new CashTurnIn
            {
                TenantId = tenantId,
                CashSessionId = session.Id,
                EventId = session.EventId,
                WorkerUserId = userId.Value,
                ExpectedCents = null,   // snapshotted by the reconciliation report (next slice)
                WorkerCountedCents = req.WorkerCountedCents,
                Status = "submitted",
                Note = req.Note,
                CashTipsDeclaredCents = req.CashTipsDeclaredCents,
                TipsFromDrawerCents = req.TipsFromDrawerCents,
            };

            // Tips the worker took out of their own drawer: a payout paid by the worker, plus the
            // ledger row that takes the same amount out of Tips payable and Cash on hand. Written
            // only once the claim has confirmed the take fits.
            TipPayout? drawerPayout = null;
            TenantLedgerEntry? payoutLedger = null;
            if (req.TipsFromDrawerCents > 0)
            {
                drawerPayout = new TipPayout
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId.Value,
                    AmountCents = req.TipsFromDrawerCents,
                    Source = "drawer",
                    PaidByUserId = userId.Value,
                };
                payoutLedger = TipPayoutLedgerRow(drawerPayout, paymentMethod: "cash", soldByUserId: userId.Value, memo: "Tips paid out from drawer");
            }

            Guid turnInId;
            try
            {
                turnInId = await _cash.SubmitTurnIn(turnIn, asOf, drawerPayout, payoutLedger);
            }
            catch (TipsChangedDuringCloseException ex)
            {
                // Nothing landed. A tab closed, refunded or re-rung since the review changed what
                // there is to settle, and the drawer take no longer fits it. The fresh figures ride
                // along so the screen can reset to them without another round trip.
                return new ApiResponses().BadRequestResult(
                    "Tips changed while you were closing out; review again.",
                    new { tipsEarnedCents = ex.TipsEarnedCents, cardTipsCents = ex.CardTipsCents });
            }
            catch (DrawerAlreadyClosedException)
            {
                // The session left 'open' between our locked re-read and the flip: nothing landed.
                return new ApiResponses().BadRequestResult("This drawer was already closed out.");
            }
            var created = await _cash.GetTurnInById(turnInId, tenantId);
            return new ApiResponses().OkResult(created);
        }

        // ── Manager: review + confirm ────────────────────────────────────────────

        [HttpGet("TurnIn/Pending")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> PendingTurnIns([FromQuery] Guid? eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var list = await _cash.ListPendingTurnIns(_tenantContext.TenantId, eventId);
            return new ApiResponses().OkResult(list);
        }

        [HttpGet("TurnIn/ByEvent/{eventId:guid}")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> TurnInsByEvent(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var list = await _cash.ListTurnInsByEvent(_tenantContext.TenantId, eventId);
            return new ApiResponses().OkResult(list);
        }

        [HttpPost("TurnIn/{id:guid}/Confirm")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> ConfirmTurnIn(Guid id, [FromBody] ConfirmTurnInRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var managerId = CurrentUserId();
            if (managerId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            if (req.ManagerCountedCents < 0) return new ApiResponses().BadRequestResult("Counted amount can't be negative.");

            var tenantId = _tenantContext.TenantId;
            var turnIn = await _cash.GetTurnInById(id, tenantId);
            if (turnIn is null) return new ApiResponses().NotFoundResult("Turn-in not found.");
            if (turnIn.Status != "submitted") return new ApiResponses().BadRequestResult("This turn-in has already been confirmed.");
            // Segregation of duties: the worker who turned in can't confirm their own drop.
            if (turnIn.WorkerUserId == managerId.Value)
                return new ApiResponses().BadRequestResult("You can't confirm your own cash turn-in.");

            await _cash.ConfirmTurnIn(id, tenantId, managerId.Value, req.ManagerCountedCents, req.Note);
            var updated = await _cash.GetTurnInById(id, tenantId);
            return new ApiResponses().OkResult(updated);
        }

        // ── Manager: tips owed ───────────────────────────────────────────────────

        // Who the house owes tips to, who has tips waiting for a close-out, and the payout log. A
        // worker stays listed while anything is owed or unclaimed, or while they had tip activity
        // (a tipped shift or a payout) in the last 90 days, so a balance that was just cleared is
        // still there to be checked.
        [HttpGet("TipsOwed")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> TipsOwed()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var sinceUtc = DateTime.UtcNow.AddDays(-TipsOwedHistoryDays);

            var rows = await _cash.ListTipsOwed(tenantId, sinceUtc);
            // One row past the cap tells us whether the cap cut anything, without a second count.
            var payouts = await _cash.ListTipPayouts(tenantId, sinceUtc, TipsOwedPayoutCap + 1);
            var truncated = payouts.Count > TipsOwedPayoutCap;
            if (truncated) payouts = payouts.Take(TipsOwedPayoutCap).ToList();
            var paidByManager = await _cash.SumManagerPayouts(tenantId, sinceUtc);

            return new ApiResponses().OkResult(new TipsOwedResponse
            {
                Rows = rows.Select(TipsOwedRowDto.From)
                    .OrderByDescending(r => r.OwedCents)
                    .ThenByDescending(r => r.UnclaimedTipsCents)
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Payouts = payouts.Select(ToDto).ToList(),
                PayoutsTruncated = truncated,
                PaidByManagerLast90Cents = paidByManager,
            });
        }

        // A manager settling house-owes, in cash from their own drawer or through payroll. Both
        // write a 'tip_payout' ledger row that takes the amount out of Tips payable: cash against
        // Cash on hand, payroll against the wages-payable style slot the payroll run clears. A
        // cash payout also comes out of the manager's own expected drawer: the reconciliation
        // subtracts their cash tip payouts over the session window (SumCashPayoutsPaidBy), keyed
        // on paid_by_user_id, so the ledger row's sold_by is attribution for the books, not the
        // input to that arithmetic. Payroll moved no cash and touches no drawer.
        [HttpPost("TipsOwed/Pay")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> PayTipsOwed([FromBody] PayTipsOwedRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var managerId = CurrentUserId();
            if (managerId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            if (req.UserId == Guid.Empty) return new ApiResponses().BadRequestResult("Pick who is being paid.");
            if (req.AmountCents <= 0) return new ApiResponses().BadRequestResult("Amount must be more than $0.00.");
            var source = (req.Source ?? "").Trim().ToLowerInvariant();
            if (source is not ("cash" or "payroll")) return new ApiResponses().BadRequestResult("Source must be cash or payroll.");
            // Segregation of duties, same as confirming a turn-in: nobody pays out their own tips.
            if (req.UserId == managerId.Value) return new ApiResponses().BadRequestResult("You can't pay out your own tips.");

            var tenantId = _tenantContext.TenantId;
            // Payouts to one recipient are serialized so two managers can't both settle one balance.
            await using var payoutLock = await _db.AcquireAdvisoryLock($"tip-payout:{tenantId}:{req.UserId}");
            var before = await _cash.GetTipsOwedRow(tenantId, req.UserId);
            // A balance only exists through this tenant's own turn-ins, so a positive one is also
            // the proof that the recipient is this tenant's staff.
            if (before is null) return new ApiResponses().ConflictResult("Nothing is owed to this person.");
            var beforeDto = TipsOwedRowDto.From(before);
            var outstanding = beforeDto.OwedCents;
            if (outstanding <= 0) return new ApiResponses().ConflictResult("Nothing is owed to this person.");
            if (!ShiftCloseMath.IsValidPayout(req.AmountCents, outstanding))
                return new ApiResponses().ConflictResult($"Only ${outstanding / 100m:0.00} is owed; the payout can't be more than that.");

            var payout = new TipPayout
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = req.UserId,
                AmountCents = req.AmountCents,
                Source = source,
                PaidByUserId = managerId.Value,
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            };
            var ledger = source == "cash"
                ? TipPayoutLedgerRow(payout, paymentMethod: "cash", soldByUserId: managerId.Value,
                    memo: $"Tips paid out to {beforeDto.Name}")
                : TipPayoutLedgerRow(payout, paymentMethod: "payroll", soldByUserId: null,
                    memo: $"Tips moved to payroll for {beforeDto.Name}");
            await _cash.CreateTipPayout(payout, ledger);

            var after = await _cash.GetTipsOwedRow(tenantId, req.UserId);
            return new ApiResponses().OkResult(after is null ? beforeDto : TipsOwedRowDto.From(after));
        }

        // ── Manager: reconciliation report ───────────────────────────────────────

        [HttpGet("Reconciliation/{eventId:guid}")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> Reconciliation(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var nowUtc = DateTime.UtcNow;

            var sessions = await _cash.ListSessionsByEvent(tenantId, eventId);
            var turnIns = await _cash.ListTurnInsByEvent(tenantId, eventId);
            return new ApiResponses().OkResult(await BuildReconciliation(tenantId, sessions, turnIns, nowUtc));
        }

        // Shift reconciliation for cash taken OUTSIDE an event: the bike shop counter and any
        // F&B shift open a session with no event_id, so the event-scoped report above would never
        // show them. Without this a shop cashier could turn in cash a manager never sees.
        // Defaults to the last 7 days.
        [HttpGet("Reconciliation")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> ReconciliationForShifts(
            [FromQuery] DateTime? fromUtc = null, [FromQuery] DateTime? toUtc = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var nowUtc = DateTime.UtcNow;
            var to = (toUtc ?? nowUtc).ToUniversalTime();
            var from = (fromUtc ?? to.AddDays(-7)).ToUniversalTime();
            if (to <= from) return new ApiResponses().BadRequestResult("The end of the range must be after the start.");
            if ((to - from).TotalDays > 92) return new ApiResponses().BadRequestResult("Pick a range of 92 days or less.");

            var sessions = await _cash.ListSessionsWithoutEvent(tenantId, from, to);
            var turnIns = await _cash.ListTurnInsWithoutEvent(tenantId, from, to);
            return new ApiResponses().OkResult(await BuildReconciliation(tenantId, sessions, turnIns, nowUtc));
        }

        // Shared row builder for both reconciliation views. Expected cash is derived from the
        // ledger by worker and is source-kind agnostic, so bike shop and F&B cash both count.
        private async Task<ReconciliationResponse> BuildReconciliation(
            Guid tenantId,
            List<Services.Repositories.Data.CashData.CashSession> sessions,
            List<Services.Repositories.Data.CashData.CashTurnIn> turnIns,
            DateTime nowUtc)
        {
            var sessionsById = sessions.ToDictionary(s => s.Id);

            var rows = new List<ReconciliationRow>();
            var turnedInSessionIds = new HashSet<Guid>();

            // One row per turn-in (the reconciliation unit). Expected = opening float + the
            // worker's net cash over the session window (open -> turn-in) - the tips they took
            // out of the drawer at close - any house-owes they settled in cash for someone else
            // from this drawer while it was open, which is exactly what their drawer should hold.
            // The turn-in's own tips_from_drawer_cents is the source of truth for the tip-out, not
            // the payout's ledger row: that row is stamped at the close instant, right at the
            // window's edge, and the window must not bend to catch it. Manager settlements are
            // their own rows (source 'cash', keyed on who paid), read ONCE for the whole span the
            // report covers and matched to each drawer's window in memory.
            var earliestOpen = turnIns
                .Select(ti => sessionsById.TryGetValue(ti.CashSessionId, out var ts) ? ts.OpenedAt : ti.SubmittedAt)
                .Concat(sessions.Select(s => s.OpenedAt))
                .DefaultIfEmpty(nowUtc)
                .Min();
            var cashPayouts = await _cash.ListCashPayoutsPaidBy(tenantId, earliestOpen, nowUtc);
            long PaidOutInCash(Guid payerUserId, DateTime fromUtc, DateTime toUtc) => cashPayouts
                .Where(p => p.PayerUserId == payerUserId && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
                .Sum(p => (long)p.AmountCents);

            foreach (var ti in turnIns)
            {
                turnedInSessionIds.Add(ti.CashSessionId);
                sessionsById.TryGetValue(ti.CashSessionId, out var s);
                var openedAt = s?.OpenedAt ?? ti.SubmittedAt;
                var openingFloat = s?.OpeningFloatCents ?? 0;
                var net = await _ledger.SumCashNetForWorker(tenantId, ti.WorkerUserId, openedAt, ti.SubmittedAt);
                var managerCashPayouts = PaidOutInCash(ti.WorkerUserId, openedAt, ti.SubmittedAt);
                var expected = openingFloat + net - ti.TipsFromDrawerCents - managerCashPayouts;
                var counted = ti.ManagerCountedCents ?? ti.WorkerCountedCents;
                rows.Add(new ReconciliationRow
                {
                    TurnInId = ti.Id,
                    CashSessionId = ti.CashSessionId,
                    WorkerUserId = ti.WorkerUserId,
                    Status = ti.Status,
                    OpenedAtUtc = openedAt,
                    SubmittedAtUtc = ti.SubmittedAt,
                    ConfirmedAtUtc = ti.ConfirmedAt,
                    OpeningFloatCents = openingFloat,
                    ExpectedCents = expected,
                    WorkerCountedCents = ti.WorkerCountedCents,
                    ManagerCountedCents = ti.ManagerCountedCents,
                    VarianceCents = counted - expected,
                    TipsFromDrawerCents = ti.TipsFromDrawerCents,
                    HouseOwesCents = ti.HouseOwesCents,
                });
            }

            // Still-open sessions with no turn-in yet: show expected-so-far so the manager can
            // see who hasn't turned in. No tip-out has left an open drawer yet, but a manager's
            // cash settlements made from it so far have.
            foreach (var s in sessions)
            {
                if (turnedInSessionIds.Contains(s.Id) || s.Status == "turned_in") continue;
                var net = await _ledger.SumCashNetForWorker(tenantId, s.UserId, s.OpenedAt, nowUtc);
                var managerCashPayouts = PaidOutInCash(s.UserId, s.OpenedAt, nowUtc);
                rows.Add(new ReconciliationRow
                {
                    TurnInId = null,
                    CashSessionId = s.Id,
                    WorkerUserId = s.UserId,
                    Status = "open",
                    OpenedAtUtc = s.OpenedAt,
                    OpeningFloatCents = s.OpeningFloatCents,
                    ExpectedCents = s.OpeningFloatCents + net - managerCashPayouts,
                });
            }

            // Refunds-by-worker over the event's reconciliation window (first session open to now).
            var windowFrom = sessions.Count > 0 ? sessions.Min(s => s.OpenedAt) : nowUtc;
            var refundTotals = await _ledger.ListRefundsByWorker(tenantId, windowFrom, nowUtc);

            // Resolve worker display names once for every worker referenced.
            var workerIds = rows.Select(r => r.WorkerUserId)
                .Concat(refundTotals.Select(t => t.WorkerUserId))
                .Distinct().ToList();
            var names = new Dictionary<Guid, string>();
            foreach (var wid in workerIds)
            {
                var u = await _users.GetById(wid);
                if (u is not null) names[wid] = $"{u.FirstName} {u.LastName}".Trim();
            }
            foreach (var r in rows) r.WorkerName = names.GetValueOrDefault(r.WorkerUserId);

            var refundRows = refundTotals.Select(t => new ReconciliationRefundRow
            {
                WorkerUserId = t.WorkerUserId,
                WorkerName = names.GetValueOrDefault(t.WorkerUserId),
                CashRefundCount = t.CashCount,
                CashRefundCents = t.CashCents,
                CardRefundCount = t.CardCount,
                CardRefundCents = t.CardCents,
            }).ToList();

            return new ReconciliationResponse { Rows = rows, RefundsByWorker = refundRows };
        }

        private Guid? CurrentUserId() =>
            Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : (Guid?)null;

        // The ledger row behind a tip payout: the amount leaving Tips payable. Negative gross, no
        // fee, no cut, net 0, so it moves the books (JournalEntryBuilder: DR Tips payable, then
        // CR Cash on hand for cash tender or CR Tips moved to payroll for 'payroll') but never
        // what the tenant is paid. sold_by is whose drawer the cash left; null for payroll, where
        // no cash left anywhere. The repository stamps occurred_at from the transaction clock and
        // pins kind/source to the payout, so neither is set here.
        private static TenantLedgerEntry TipPayoutLedgerRow(TipPayout payout, string paymentMethod, Guid? soldByUserId, string memo) => new()
        {
            TenantId = payout.TenantId,
            GrossCents = -payout.AmountCents,
            StripeFeeCents = 0,
            RidepassCutCents = 0,
            NetToTenantCents = 0,
            PaymentMethod = paymentMethod,
            SoldByUserId = soldByUserId,
            Memo = memo,
        };

        private static TipPayoutDto ToDto(TipPayoutRow p) => new()
        {
            Id = p.Id,
            UserId = p.UserId,
            Name = UserDisplayName.Of(p.Name, p.Email),
            AmountCents = p.AmountCents,
            Source = p.Source,
            // The payer's account can be deleted after the fact (Script0294 nulls the FK); the
            // payout is still real money that was handed over.
            PaidByName = p.PaidByUserId is null ? "(removed user)" : UserDisplayName.Of(p.PaidByName, p.PaidByEmail),
            Note = p.Note,
            CreatedAtUtc = TenantClock.AsUtc(p.CreatedAt),
        };
    }
}
