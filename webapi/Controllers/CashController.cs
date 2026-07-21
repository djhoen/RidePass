using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.CashData;
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
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CashController : ControllerBase
    {
        private readonly ICashRepository _cash;
        private readonly ITenantLedgerRepository _ledger;
        private readonly IUserRepository _users;
        private readonly ITenantContext _tenantContext;

        public CashController(ICashRepository cash, ITenantLedgerRepository ledger,
            IUserRepository users, ITenantContext tenantContext)
        {
            _cash = cash;
            _ledger = ledger;
            _users = users;
            _tenantContext = tenantContext;
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

        // ── Worker: turn-in ──────────────────────────────────────────────────────

        [HttpPost("TurnIn")]
        [Authorize(Policy = TenantPermissions.Policy.CashTurnIn)]
        public async Task<IActionResult> SubmitTurnIn([FromBody] SubmitTurnInRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var userId = CurrentUserId();
            if (userId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            if (req.WorkerCountedCents < 0) return new ApiResponses().BadRequestResult("Counted amount can't be negative.");

            var tenantId = _tenantContext.TenantId;
            var session = await _cash.GetOpenSession(tenantId, userId.Value, req.EventId);
            if (session is null) return new ApiResponses().BadRequestResult("No open cash session to turn in for this event.");

            var turnInId = await _cash.CreateTurnIn(new CashTurnIn
            {
                TenantId = tenantId,
                CashSessionId = session.Id,
                EventId = session.EventId,
                WorkerUserId = userId.Value,
                ExpectedCents = null,   // snapshotted by the reconciliation report (next slice)
                WorkerCountedCents = req.WorkerCountedCents,
                Status = "submitted",
                Note = req.Note,
            });
            await _cash.SetSessionStatus(session.Id, tenantId, "turned_in");
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
            // worker's net cash over the session window (open -> turn-in), which is exactly
            // what their drawer should hold.
            foreach (var ti in turnIns)
            {
                turnedInSessionIds.Add(ti.CashSessionId);
                sessionsById.TryGetValue(ti.CashSessionId, out var s);
                var openedAt = s?.OpenedAt ?? ti.SubmittedAt;
                var openingFloat = s?.OpeningFloatCents ?? 0;
                var net = await _ledger.SumCashNetForWorker(tenantId, ti.WorkerUserId, openedAt, ti.SubmittedAt);
                var expected = openingFloat + net;
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
                });
            }

            // Still-open sessions with no turn-in yet: show expected-so-far so the manager can
            // see who hasn't turned in.
            foreach (var s in sessions)
            {
                if (turnedInSessionIds.Contains(s.Id) || s.Status == "turned_in") continue;
                var net = await _ledger.SumCashNetForWorker(tenantId, s.UserId, s.OpenedAt, nowUtc);
                rows.Add(new ReconciliationRow
                {
                    TurnInId = null,
                    CashSessionId = s.Id,
                    WorkerUserId = s.UserId,
                    Status = "open",
                    OpenedAtUtc = s.OpenedAt,
                    OpeningFloatCents = s.OpeningFloatCents,
                    ExpectedCents = s.OpeningFloatCents + net,
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
    }
}
