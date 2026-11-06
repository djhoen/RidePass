using System.Globalization;
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
    // Tips (Script0294 .. Script0298): a tip is settled on exactly one SHIFT CLOSE (tip_shift), the
    // unit of a person's shift report, which is no longer tied to a drawer. The close claims every
    // unclaimed tip on the person's paid sales since the tenant's claims cutover (a tab closed after
    // their last close included), snapshots the numbers from those very rows, and logs every payout
    // in tip_payout. In manager_later mode (the default: one shared register, tips paid later by a
    // manager) nothing comes out of a drawer and the whole figure is house-owes, settled from
    // TipsOwed in cash or through payroll. In drawer_at_close mode the person takes their tips in
    // cash from the TERMINAL account's open drawer and only the remainder is owed. Every payout
    // writes a 'tip_payout' ledger row so the books see it; expected drawer cash subtracts the
    // tip_shift tip-outs taken against that drawer and the manager's own cash settlements rather
    // than reading the ledger (BuildReconciliation). The drawer turn-in is a blind count and nothing
    // else. The tip jar (cash outside the register) is recorded per day as a split among the staff
    // with no ledger or liability behind it: the jar is money they already took.
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
        private readonly IConcessionRepository _concessions;
        private readonly webapi.Security.ICashierTokenService _cashierTokens;
        private readonly ITenantContext _tenantContext;
        private readonly IDbHelper _db;

        public CashController(ICashRepository cash, ITenantLedgerRepository ledger,
            IUserRepository users, IConcessionRepository concessions,
            webapi.Security.ICashierTokenService cashierTokens, ITenantContext tenantContext, IDbHelper db)
        {
            _cash = cash;
            _ledger = ledger;
            _users = users;
            _concessions = concessions;
            _cashierTokens = cashierTokens;
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

        // ── Person: shift review + shift close ───────────────────────────────────

        // A person's own close-out numbers (Toast "Shift Review"): the tips their next shift close
        // will settle (every unclaimed tip, with the part from before this drawer opened called
        // out) and anything the house still owes them from earlier shifts. Deliberately NOT
        // expected cash, which would defeat the blind count. The person is the cashier token's user
        // when one is sent (a shared register), else the signed-in account. The drawer is the
        // TERMINAL's, found exactly as ShiftClose finds it (ResolveDrawer), and the window is
        // informational: from that drawer's opened_at, or the tenant's business day so far when no
        // drawer is open. A preview only: the close recomputes everything from the rows it claims.
        //
        // The cashier token travels in the X-Cashier-Token header, like a credential, so it never
        // lands in access logs or browser history as part of the URL. The ?cashierToken= query
        // form is DEPRECATED and accepted for one release only, for a register that has not been
        // reloaded since the Script0298 deploy; the header wins when both are sent.
        [HttpGet("ShiftReview")]
        [Authorize(Policy = TenantPermissions.Policy.ShiftClose)]
        public async Task<IActionResult> ShiftReview([FromQuery] Guid? eventId,
            [FromHeader(Name = "X-Cashier-Token")] string? cashierTokenHeader, [FromQuery] string? cashierToken)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var terminalId = CurrentUserId();
            if (terminalId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            var token = string.IsNullOrWhiteSpace(cashierTokenHeader) ? cashierToken : cashierTokenHeader;
            var (person, personError) = await ResolvePerson(token, terminalId.Value);
            if (personError is not null) return new ApiResponses().BadRequestResult(personError);
            var personId = person!.Id;

            var tenantId = _tenantContext.TenantId;
            var asOf = DateTime.UtcNow;
            var settings = await _concessions.GetMenuSettings(tenantId);
            var drawer = await ResolveDrawer(tenantId, terminalId.Value, eventId);
            var windowFrom = drawer is not null
                ? TenantClock.AsUtc(drawer.OpenedAt)
                : TenantClock.BusinessDayStartUtc(_tenantContext.Tenant.Timezone, asOf);
            var tips = await _cash.GetWorkerTipTotals(tenantId, personId, asOf, windowFrom,
                drawer is not null ? TenantClock.AsUtc(drawer.OpenedAt) : null);
            var owed = await _cash.GetTipsOwedRow(tenantId, personId);

            return new ApiResponses().OkResult(new ShiftReviewResponse
            {
                UserId = personId,
                Name = UserDisplayName.Of(person.FirstName, person.LastName, person.Email),
                Session = drawer,
                DrawerOpen = drawer is not null,
                DrawerSessionId = drawer?.Id,
                WindowFromUtc = windowFrom,
                WindowToUtc = asOf,
                TipsEarnedCents = tips.TipsEarnedCents,
                CardTipsCents = tips.CardTipsCents,
                CashSaleTipsCents = tips.CashSaleTipsCents,
                TipsFromEarlierShiftsCents = tips.TipsFromEarlierShiftsCents,
                SaleCount = tips.SaleCount,
                CashSaleCount = tips.CashSaleCount,
                OwedToYouCents = owed is null ? 0 : TipsOwedRowDto.From(owed).OwedCents,
                TipsPayoutMode = settings?.TipsPayoutMode ?? "manager_later",
                StaffCodeMode = settings?.StaffCodeMode ?? "off",
            });
        }

        // A person closing their shift: the claim + snapshot in ONE transaction (CashRepository.
        // CloseShift), and in drawer_at_close mode the drawer tip-out, taken out of the TERMINAL
        // account's open drawer, with its payout and ledger row. The person is the cashier token's
        // user or the signed-in account; the terminal is always the signed-in account.
        //
        // Which drawer, in drawer_at_close mode (ResolveDrawer): (1) the terminal's open session
        // for req.EventId when one is given and open; (2) else the terminal's open no-event
        // session; (3) else, if anything is being taken, 400 "Open the drawer first." (a close
        // taking nothing still goes through with no drawer). The drawer found is re-checked and
        // row-locked inside the transaction, so a turn-in slipping in between reads 400 rather
        // than paying out of a closed drawer.
        //
        // Idempotent on req.ClientRequestId: a submit that already wrote a shift for this person
        // gets that shift back, before any other check (so a retry after the drawer was counted
        // still answers 200) and again under the lock inside CloseShift (so two copies of one
        // submit can never both write).
        [HttpPost("ShiftClose")]
        [Authorize(Policy = TenantPermissions.Policy.ShiftClose)]
        public async Task<IActionResult> ShiftClose([FromBody] ShiftCloseRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var terminalId = CurrentUserId();
            if (terminalId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            if (req.TipsFromDrawerCents < 0) return new ApiResponses().BadRequestResult("Tips taken from the drawer can't be negative.");
            if (req.CashTipsDeclaredCents < 0) return new ApiResponses().BadRequestResult("Declared cash tips can't be negative.");
            var (person, personError) = await ResolvePerson(req.CashierToken, terminalId.Value);
            if (personError is not null) return new ApiResponses().BadRequestResult(personError);
            var personId = person!.Id;
            var tenantId = _tenantContext.TenantId;

            var clientRequestId = req.ClientRequestId is { } crid && crid != Guid.Empty ? crid : (Guid?)null;
            if (clientRequestId is { } replayId)
            {
                var replay = await _cash.GetTipShiftByClientRequestId(tenantId, personId, replayId);
                if (replay is not null) return new ApiResponses().OkResult(TipShiftDto.From(replay));
            }

            var settings = await _concessions.GetMenuSettings(tenantId);
            var payoutMode = settings?.TipsPayoutMode ?? "manager_later";
            if (payoutMode == "manager_later" && req.TipsFromDrawerCents != 0)
                return new ApiResponses().BadRequestResult("Tips are paid later by a manager here; nothing comes out of the drawer at shift close.");

            CashSession? drawer = null;
            if (payoutMode == "drawer_at_close")
            {
                drawer = await ResolveDrawer(tenantId, terminalId.Value, req.EventId);
                if (drawer is null && req.TipsFromDrawerCents > 0)
                    return new ApiResponses().BadRequestResult("Open the drawer first.");
            }

            // One close per person at a time: a double-submit must not claim the same tips twice
            // or pay the same tip-out twice. The claim itself is idempotent (claimed rows leave the
            // pool), so the lock is what turns the second submit into "nothing to settle" rather
            // than a second row with the same drawer take. The drawer's own lock (the key TurnIn
            // uses) is taken second, so a concurrent turn-in on that drawer waits for the close.
            // TurnIn never takes the person lock, so the two orders cannot deadlock.
            await using var closeLock = await _db.AcquireAdvisoryLock($"shift-close:{tenantId}:{personId}");
            await using var drawerLock = drawer is null ? null : await _db.AcquireAdvisoryLock($"cash-turnin:{drawer.Id}");

            var asOf = DateTime.UtcNow;
            var shift = new TipShift
            {
                TenantId = tenantId,
                UserId = personId,
                ClosedByUserId = terminalId,
                CashSessionId = drawer?.Id,
                BusinessDate = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, asOf),
                CashTipsDeclaredCents = req.CashTipsDeclaredCents,
                TipsFromDrawerCents = req.TipsFromDrawerCents,
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                ClientRequestId = clientRequestId,
            };

            // Tips taken out of the terminal's drawer: a payout to the person, paid out of that
            // drawer (paid_by / sold_by = the terminal account, whose drawer it is), plus the ledger
            // row that takes the same amount out of Tips payable and Cash on hand. Written only once
            // the claim has confirmed the take fits.
            TipPayout? drawerPayout = null;
            TenantLedgerEntry? payoutLedger = null;
            if (req.TipsFromDrawerCents > 0)
            {
                drawerPayout = new TipPayout
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = personId,
                    AmountCents = req.TipsFromDrawerCents,
                    Source = "drawer",
                    PaidByUserId = terminalId,
                };
                payoutLedger = TipPayoutLedgerRow(drawerPayout, paymentMethod: "cash", soldByUserId: terminalId, memo: "Tips paid out from drawer");
            }

            Guid shiftId;
            try
            {
                shiftId = await _cash.CloseShift(shift, asOf, drawerPayout, payoutLedger);
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
            catch (NothingToSettleException)
            {
                // Nothing landed: no unclaimed tip and nothing declared, so there is no report.
                return new ApiResponses().BadRequestResult("Nothing to settle yet.");
            }
            catch (DrawerAlreadyClosedException)
            {
                // Nothing landed: the drawer was turned in between our read and the row lock.
                return new ApiResponses().BadRequestResult("That drawer was already closed out. Open a drawer first.");
            }
            var created = await _cash.GetTipShiftById(shiftId, tenantId);
            return new ApiResponses().OkResult(created is null ? null : TipShiftDto.From(created));
        }

        // ── Worker: drawer turn-in ───────────────────────────────────────────────

        // The blind count of a drawer and nothing else. Tips were settled on the shift close
        // (above), whichever drawer they were rung on. A request that still carries the pre-0298
        // tip fields comes from a register running the old build; it is refused rather than
        // silently counted without its tip-out, because that worker believes they just settled.
        [HttpPost("TurnIn")]
        [Authorize(Policy = TenantPermissions.Policy.CashTurnIn)]
        public async Task<IActionResult> SubmitTurnIn([FromBody] SubmitTurnInRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var userId = CurrentUserId();
            if (userId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            if (req.WorkerCountedCents < 0) return new ApiResponses().BadRequestResult("Counted amount can't be negative.");
#pragma warning disable CS0618 // the obsolete fields are read only to refuse them
            if (req.TipsFromDrawerCents > 0 || req.CashTipsDeclaredCents > 0)
                return new ApiResponses().BadRequestResult("This register is running an older version. Reload the page, then close out.");
#pragma warning restore CS0618

            var tenantId = _tenantContext.TenantId;
            var session = await _cash.GetOpenSession(tenantId, userId.Value, req.EventId);
            if (session is null) return new ApiResponses().BadRequestResult("No open cash session to turn in for this event.");

            // One close per session: a double-submit must not write two turn-ins, so the session
            // is re-read under a lock on its id. The lock is keyed on the id we first saw; if the
            // re-read comes back with a different open session, the drawer was closed and reopened
            // in the gap and this request's count belongs to a drawer that no longer exists.
            var lockedId = session.Id;
            await using var closeLock = await _db.AcquireAdvisoryLock($"cash-turnin:{lockedId}");
            session = await _cash.GetOpenSession(tenantId, userId.Value, req.EventId);
            if (session is null) return new ApiResponses().BadRequestResult("This cash session has already been turned in.");
            if (session.Id != lockedId) return new ApiResponses().ConflictResult("Your drawer changed; reopen Close out.");

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
            };

            Guid turnInId;
            try
            {
                turnInId = await _cash.SubmitTurnIn(turnIn);
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

        // Who the house owes tips to, who has tips waiting for a shift close, and the payout log. A
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
        // subtracts their cash tip payouts over the session window (ListCashPayoutsPaidBy), keyed
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
            // A balance only exists through this tenant's own shift closes, so a positive one is
            // also the proof that the recipient is this tenant's staff.
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

        // ── Manager: tip jar ─────────────────────────────────────────────────────

        // The jar for a day: who closed a shift (the people to pre-check), every counter staff
        // member who could take a share, and the splits recorded so far.
        [HttpGet("TipJar")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> TipJar([FromQuery] string? date)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            if (!TryResolveBusinessDate(date, out var businessDate))
                return new ApiResponses().BadRequestResult("Date must be yyyy-MM-dd.");

            var suggested = await _cash.ListTipShiftUserIds(tenantId, businessDate);
            var staff = (await _users.ListByTenant(tenantId))
                .Where(u => u.Status == "active" && IsCounterStaff(u))
                .Select(u => new TipJarStaffDto { UserId = u.Id, Name = UserDisplayName.Of(u.FirstName, u.LastName, u.Email) })
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var splits = await BuildTipJarSplits(tenantId, businessDate);

            return new ApiResponses().OkResult(new TipJarResponse
            {
                Date = businessDate.ToString("yyyy-MM-dd"),
                SuggestedUserIds = suggested,
                Staff = staff,
                Splits = splits,
            });
        }

        // Records a split: an even division in whole cents, the odd cents going one each to the
        // first people in the order given (TipJarMath). Everyone named must be this tenant's
        // active counter staff; the people are the only thing a request can get wrong, so that is
        // what is checked.
        [HttpPost("TipJar")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> RecordTipJar([FromBody] TipJarSplitRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var managerId = CurrentUserId();
            if (managerId is null) return new ApiResponses().BadRequestResult("No authenticated user.");
            var tenantId = _tenantContext.TenantId;
            if (!TryResolveBusinessDate(req.Date, out var businessDate))
                return new ApiResponses().BadRequestResult("Date must be yyyy-MM-dd.");
            if (req.TotalCents <= 0) return new ApiResponses().BadRequestResult("The jar total must be more than $0.00.");
            var userIds = (req.UserIds ?? new List<Guid>()).Where(id => id != Guid.Empty).ToList();
            if (userIds.Count == 0) return new ApiResponses().BadRequestResult("Pick at least one person to split the jar with.");
            if (userIds.Distinct().Count() != userIds.Count) return new ApiResponses().BadRequestResult("Each person can only be in the split once.");

            var staff = (await _users.ListByTenant(tenantId))
                .Where(u => u.Status == "active" && IsCounterStaff(u))
                .ToDictionary(u => u.Id);
            if (userIds.Any(id => !staff.ContainsKey(id)))
                return new ApiResponses().BadRequestResult("Everyone in the split has to be active counter staff at this venue.");

            var split = new TipJarSplit
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BusinessDate = businessDate,
                TotalCents = req.TotalCents,
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                CreatedByUserId = managerId,
            };
            var shares = TipJarMath.Split(req.TotalCents, userIds)
                .Select(s => new TipJarShare { TenantId = tenantId, UserId = s.UserId, AmountCents = s.AmountCents })
                .ToList();
            await _cash.CreateTipJarSplit(split, shares);

            var created = (await BuildTipJarSplits(tenantId, businessDate)).FirstOrDefault(s => s.Id == split.Id);
            return new ApiResponses().OkResult(created);
        }

        // A same-day correction: the split and its shares go away. Nothing else referenced them.
        [HttpDelete("TipJar/{id:guid}")]
        [Authorize(Policy = TenantPermissions.Policy.CashReconcile)]
        public async Task<IActionResult> DeleteTipJar(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await _cash.DeleteTipJarSplit(id, _tenantContext.TenantId))
                return new ApiResponses().NotFoundResult("Tip jar split not found.");
            return new ApiResponses().OkResult();
        }

        private async Task<List<TipJarSplitDto>> BuildTipJarSplits(Guid tenantId, DateOnly businessDate)
        {
            var splits = await _cash.ListTipJarSplits(tenantId, businessDate);
            var shares = (await _cash.ListTipJarShares(tenantId, businessDate)).ToLookup(s => s.SplitId);
            return splits.Select(j => new TipJarSplitDto
            {
                Id = j.Id,
                TotalCents = j.TotalCents,
                Note = j.Note,
                CreatedByName = j.CreatedByUserId is null ? "(removed user)" : UserDisplayName.Of(j.CreatedByName, j.CreatedByEmail),
                CreatedAtUtc = TenantClock.AsUtc(j.CreatedAt),
                Shares = shares[j.Id].Select(s => new TipJarShareDto
                {
                    UserId = s.UserId,
                    Name = UserDisplayName.Of(s.Name, s.Email),
                    AmountCents = s.AmountCents,
                }).ToList(),
            }).ToList();
        }

        // Who may take a share of the jar: the same roles that may hold a POS code.
        private static bool IsCounterStaff(Services.Repositories.Data.UserData.User u) =>
            TenantPermissions.MayHoldPosCode(u.Role, u.Roles);

        // yyyy-MM-dd, or today at the track when blank.
        private bool TryResolveBusinessDate(string? date, out DateOnly businessDate)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                businessDate = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);
                return true;
            }
            return DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out businessDate);
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
            // worker's net cash over the session window (open -> turn-in) - the tips taken out of
            // THIS drawer at shift closes (tip_shift rows naming the session, whoever's shift it
            // was: on a shared register the terminal's drawer pays everyone's tip-out) - any
            // house-owes the worker settled in cash for someone else from this drawer while it was
            // open, which is exactly what their drawer should hold. The tip-outs come from
            // tip_shift, not the payouts' ledger rows: those are stamped at the close instant,
            // right at a window's edge, and the window must not bend to catch them. Manager
            // settlements are their own rows (source 'cash', keyed on who paid), read ONCE for the
            // whole span the report covers and matched to each drawer's window in memory.
            var earliestOpen = turnIns
                .Select(ti => sessionsById.TryGetValue(ti.CashSessionId, out var ts) ? ts.OpenedAt : ti.SubmittedAt)
                .Concat(sessions.Select(s => s.OpenedAt))
                .DefaultIfEmpty(nowUtc)
                .Min();
            var cashPayouts = await _cash.ListCashPayoutsPaidBy(tenantId, earliestOpen, nowUtc);
            long PaidOutInCash(Guid payerUserId, DateTime fromUtc, DateTime toUtc) => cashPayouts
                .Where(p => p.PayerUserId == payerUserId && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
                .Sum(p => (long)p.AmountCents);
            var tipOuts = await _cash.SumTipsFromDrawerBySession(tenantId,
                turnIns.Select(ti => ti.CashSessionId).Concat(sessions.Select(s => s.Id)).Distinct().ToList());

            foreach (var ti in turnIns)
            {
                turnedInSessionIds.Add(ti.CashSessionId);
                sessionsById.TryGetValue(ti.CashSessionId, out var s);
                var openedAt = s?.OpenedAt ?? ti.SubmittedAt;
                var openingFloat = s?.OpeningFloatCents ?? 0;
                var net = await _ledger.SumCashNetForWorker(tenantId, ti.WorkerUserId, openedAt, ti.SubmittedAt);
                var managerCashPayouts = PaidOutInCash(ti.WorkerUserId, openedAt, ti.SubmittedAt);
                // tip_shift is the only source: Script0298 moved every pre-0298 tip-out onto a
                // shift naming this session and zeroed the turn-in's own copy.
                var tipsFromDrawer = tipOuts.GetValueOrDefault(ti.CashSessionId);
                var expected = openingFloat + net - tipsFromDrawer - managerCashPayouts;
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
                    TipsFromDrawerCents = checked((int)tipsFromDrawer),
                    HouseOwesCents = ti.HouseOwesCents,
                });
            }

            // Still-open sessions with no turn-in yet: show expected-so-far so the manager can
            // see who hasn't turned in. A shift close can already have taken tips out of an open
            // drawer, and a manager's cash settlements made from it so far have left too.
            foreach (var s in sessions)
            {
                if (turnedInSessionIds.Contains(s.Id) || s.Status == "turned_in") continue;
                var net = await _ledger.SumCashNetForWorker(tenantId, s.UserId, s.OpenedAt, nowUtc);
                var managerCashPayouts = PaidOutInCash(s.UserId, s.OpenedAt, nowUtc);
                var tipsFromDrawer = tipOuts.GetValueOrDefault(s.Id);
                rows.Add(new ReconciliationRow
                {
                    TurnInId = null,
                    CashSessionId = s.Id,
                    WorkerUserId = s.UserId,
                    Status = "open",
                    OpenedAtUtc = s.OpenedAt,
                    OpeningFloatCents = s.OpeningFloatCents,
                    ExpectedCents = s.OpeningFloatCents + net - tipsFromDrawer - managerCashPayouts,
                    TipsFromDrawerCents = checked((int)tipsFromDrawer),
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

        // Whose shift a review or close is about: the cashier token's person (a shared register,
        // Script0298) when one is sent and it is this tenant's and unexpired, else the signed-in
        // account. Either way the person is re-read and must still exist here and be active: a
        // token names a user by id and outlives a role change or a disable. A token that no longer
        // validates is an error, never a silent fall-back to the terminal: that would settle one
        // person's tips under another's name.
        private async Task<(Services.Repositories.Data.UserData.User? Person, string? Error)> ResolvePerson(string? cashierToken, Guid terminalUserId)
        {
            var fromToken = !string.IsNullOrWhiteSpace(cashierToken);
            var personId = terminalUserId;
            if (fromToken)
            {
                var token = _cashierTokens.Validate(cashierToken, _tenantContext.TenantId);
                if (!token.IsValid) return (null, webapi.Security.ICashierTokenService.InvalidTokenMessage);
                personId = token.UserId!.Value;
            }
            var person = await _users.GetById(personId);
            if (person is null || person.TenantId != _tenantContext.TenantId || person.Status != "active")
                return (null, fromToken ? webapi.Security.ICashierTokenService.InvalidTokenMessage : "No authenticated user.");
            return (person, null);
        }

        // The terminal's drawer, the same way for the review and the close: its open session for
        // the event when one is named and open, else its open no-event session, else none.
        private async Task<CashSession?> ResolveDrawer(Guid tenantId, Guid terminalUserId, Guid? eventId)
        {
            CashSession? drawer = null;
            if (eventId.HasValue) drawer = await _cash.GetOpenSession(tenantId, terminalUserId, eventId);
            return drawer ?? await _cash.GetOpenSession(tenantId, terminalUserId, null);
        }

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
