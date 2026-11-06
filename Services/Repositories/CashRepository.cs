using Dapper;
using Services.Cash;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.CashData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class CashRepository : ICashRepository
    {
        private readonly IDbHelper _db;

        private const string SelectSessionColumns = @"
            id, tenant_id AS TenantId, event_id AS EventId, user_id AS UserId,
            device_id AS DeviceId, opening_float_cents AS OpeningFloatCents, status,
            opened_at AS OpenedAt, closed_at AS ClosedAt";

        private const string SelectTurnInColumns = @"
            id, tenant_id AS TenantId, cash_session_id AS CashSessionId, event_id AS EventId,
            worker_user_id AS WorkerUserId, manager_user_id AS ManagerUserId,
            expected_cents AS ExpectedCents, worker_counted_cents AS WorkerCountedCents,
            manager_counted_cents AS ManagerCountedCents, variance_cents AS VarianceCents,
            status, note, submitted_at AS SubmittedAt, confirmed_at AS ConfirmedAt,
            tips_earned_cents AS TipsEarnedCents, card_tips_cents AS CardTipsCents,
            cash_tips_declared_cents AS CashTipsDeclaredCents,
            tips_from_drawer_cents AS TipsFromDrawerCents, house_owes_cents AS HouseOwesCents";

        // The pool a close-out settles: the worker's PAID sales carrying a tip that no turn-in has
        // claimed yet (Script0296), tipped on or after the tenant's claims cutover (Script0297:
        // anything before it was paid out by hand before RidePass tracked tips, and a NULL cutover
        // means a tenant whose whole history is post-feature). The first predicate matches
        // idx_concession_sale_unclaimed_tips exactly; both expect the sale aliased cs and the tenant
        // row aliased t, and every reader uses these same two strings so the three can never drift.
        private const string UnclaimedTipPredicate =
            "cs.status = 'paid' AND cs.tip_turn_in_id IS NULL AND cs.tip_cents > 0";
        private const string TipsClaimCutoverPredicate =
            "COALESCE(cs.paid_at, cs.created_at) >= COALESCE(t.tips_claims_from_utc, '-infinity'::timestamptz)";

        public CashRepository(IDbHelper db)
        {
            _db = db;
        }

        // What the house owes each worker is DERIVED, never stored: house-owes accrued on their
        // turn-ins minus what a manager has since paid them. This query hands back the two RAW
        // sides (plus the drawer payouts, which already came out of house-owes at close and are
        // only reported) and ShiftCloseMath.OutstandingOwed is the single place that turns them
        // into a balance, so the Tips owed page, the worker's own "owed to you" line and the End
        // of Day figure can never disagree. Unclaimed tips ride along so a manager can see what is
        // still waiting for a close-out.
        //
        // With a user, the predicate is pushed into every CTE so the lookup walks
        // idx_cash_turn_in_worker / idx_tip_payout_tenant_user / idx_concession_sale_unclaimed_tips
        // instead of aggregating the whole tenant to read one row. Callers append a WHERE.
        //
        // The CTEs are a separate string so SumOutstandingOwed (the End of Day headline) reads the
        // very same definitions of "house owes" and "paid by a manager" as the per-worker rows.
        //
        // The users join is not tenant-filtered on purpose: every user_id here comes from a
        // cash_turn_in, tip_payout or concession_sale row already constrained to @tenantId, so it
        // is a name lookup along a scoped FK, the same shape as EndOfDayReportRepository.GetDayStaff.
        private static string TipsOwedCtes(bool singleUser)
        {
            var shiftUser = singleUser ? "AND worker_user_id = @userId" : "";
            var payoutUser = singleUser ? "AND user_id = @userId" : "";
            var saleUser = singleUser ? "AND cs.sold_by_user_id = @userId" : "";
            return $@"
            WITH shifts AS (
                SELECT worker_user_id                   AS user_id,
                       SUM(tips_earned_cents)::bigint   AS earned_cents,
                       SUM(house_owes_cents)::bigint    AS house_owes_cents,
                       MAX(submitted_at)                AS last_shift_at,
                       MAX(submitted_at) FILTER (WHERE tips_earned_cents > 0 OR house_owes_cents > 0) AS last_tip_shift_at
                FROM cash_turn_in
                WHERE tenant_id = @tenantId {shiftUser}
                GROUP BY worker_user_id
            ),
            paid AS (
                SELECT user_id,
                       COALESCE(SUM(amount_cents) FILTER (WHERE source = 'drawer'), 0)::bigint             AS from_drawer_cents,
                       COALESCE(SUM(amount_cents) FILTER (WHERE source IN ('cash', 'payroll')), 0)::bigint AS by_manager_cents,
                       MAX(created_at)                                                                     AS last_payout_at
                FROM tip_payout
                WHERE tenant_id = @tenantId {payoutUser}
                GROUP BY user_id
            ),
            unclaimed AS (
                SELECT cs.sold_by_user_id          AS user_id,
                       SUM(cs.tip_cents)::bigint   AS unclaimed_cents
                FROM concession_sale cs
                JOIN tenant t ON t.id = cs.tenant_id
                WHERE cs.tenant_id = @tenantId AND {UnclaimedTipPredicate} AND {TipsClaimCutoverPredicate}
                  AND cs.sold_by_user_id IS NOT NULL {saleUser}
                GROUP BY cs.sold_by_user_id
            ),
            people AS (
                SELECT user_id FROM shifts
                UNION
                SELECT user_id FROM paid
                UNION
                SELECT user_id FROM unclaimed
            )";
        }

        private static string TipsOwedSql(bool singleUser) => TipsOwedCtes(singleUser) + @"
            SELECT p.user_id                                                        AS UserId,
                   NULLIF(TRIM(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), '') AS Name,
                   u.email                                                          AS Email,
                   COALESCE(s.house_owes_cents, 0)                                  AS HouseOwesTotalCents,
                   COALESCE(d.by_manager_cents, 0)                                  AS PaidByManagerCents,
                   COALESCE(d.from_drawer_cents, 0)                                 AS PaidFromDrawerCents,
                   COALESCE(s.earned_cents, 0)                                      AS EarnedTotalCents,
                   COALESCE(c.unclaimed_cents, 0)                                   AS UnclaimedTipsCents,
                   s.last_shift_at                                                  AS LastShiftAt,
                   s.last_tip_shift_at                                              AS LastTipShiftAt,
                   d.last_payout_at                                                 AS LastPayoutAt
            FROM people p
            LEFT JOIN shifts s    ON s.user_id = p.user_id
            LEFT JOIN paid d      ON d.user_id = p.user_id
            LEFT JOIN unclaimed c ON c.user_id = p.user_id
            LEFT JOIN users u     ON u.id = p.user_id";

        // ── Sessions ─────────────────────────────────────────────────────────────

        public async Task<CashSession?> GetOpenSession(Guid tenantId, Guid userId, Guid? eventId)
        {
            // event_id NULL and a concrete event are distinct sessions, so branch the
            // predicate rather than rely on null-equality of a parameter.
            var eventClause = eventId.HasValue ? "event_id = @eventId" : "event_id IS NULL";
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE tenant_id = @tenantId AND user_id = @userId AND status = 'open'
                  AND ({eventClause})
                LIMIT 1";
            var result = await _db.Query<CashSession>(sql, new { tenantId, userId, eventId });
            return result.FirstOrDefault();
        }

        public async Task<CashSession?> GetSessionById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<CashSession>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<List<CashSession>> ListSessionsByEvent(Guid tenantId, Guid eventId)
        {
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE tenant_id = @tenantId AND event_id = @eventId
                ORDER BY opened_at";
            var result = await _db.Query<CashSession>(sql, new { tenantId, eventId });
            return result.ToList();
        }

        // Sessions NOT tied to an event: a bike shop or F&B shift is a shift, not an event day.
        // Windowed by opened_at so the manager's view stays bounded.
        public async Task<List<CashSession>> ListSessionsWithoutEvent(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE tenant_id = @tenantId AND event_id IS NULL
                  AND opened_at >= @fromUtc AND opened_at < @toUtc
                ORDER BY opened_at";
            return (await _db.Query<CashSession>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<Guid> CreateSession(CashSession session)
        {
            const string sql = @"
                INSERT INTO cash_session (tenant_id, event_id, user_id, device_id, opening_float_cents, status)
                VALUES (@TenantId, @EventId, @UserId, @DeviceId, @OpeningFloatCents, @Status)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, session);
            return result.First();
        }

        // ── Turn-ins ─────────────────────────────────────────────────────────────

        // The whole close in one transaction: the turn-in, the tips it CLAIMS, the snapshot of those
        // very tips, its session leaving 'open', and (when the worker took tips out of the drawer)
        // the payout and its ledger row. The snapshot is read off the claim's own RETURNING rows,
        // not off a query made moments earlier, so a tab closed, refunded or re-rung between the
        // shift review and the submit can never leave tips_earned_cents disagreeing with what was
        // actually settled. If that movement means the drawer take no longer fits, the exception
        // rolls everything back and the worker reviews again. None of these rows may land without
        // the others: a turn-in that says tips were taken but has no payout behind it, or that
        // settled tips it never counted, would misstate money forever.
        public async Task<Guid> SubmitTurnIn(CashTurnIn turnIn, DateTime claimAsOfUtc, TipPayout? drawerPayout, TenantLedgerEntry? payoutLedgerRow)
        {
            if (turnIn.Id == Guid.Empty) turnIn.Id = Guid.NewGuid();
            return await _db.WithTransaction(async (conn, tx) =>
            {
                // The tip snapshot columns land as 0 and are filled from the claim below.
                turnIn.TipsEarnedCents = 0;
                turnIn.CardTipsCents = 0;
                turnIn.HouseOwesCents = 0;
                await conn.ExecuteAsync(@"
                    INSERT INTO cash_turn_in (id, tenant_id, cash_session_id, event_id, worker_user_id,
                                              expected_cents, worker_counted_cents, status, note,
                                              tips_earned_cents, card_tips_cents, cash_tips_declared_cents,
                                              tips_from_drawer_cents, house_owes_cents)
                    VALUES (@Id, @TenantId, @CashSessionId, @EventId, @WorkerUserId,
                            @ExpectedCents, @WorkerCountedCents, @Status, @Note,
                            @TipsEarnedCents, @CardTipsCents, @CashTipsDeclaredCents,
                            @TipsFromDrawerCents, @HouseOwesCents)", turnIn, tx);

                // Claim the tips this close settles, cut at the caller's instant: a sale paid after
                // it stays unclaimed for the next close-out rather than being settled unseen.
                var claimed = (await conn.QueryAsync<ClaimedTip>($@"
                    UPDATE concession_sale cs
                    SET tip_turn_in_id = @turnInId
                    FROM tenant t
                    WHERE t.id = cs.tenant_id
                      AND cs.tenant_id = @tenantId
                      AND cs.sold_by_user_id = @workerId
                      AND {UnclaimedTipPredicate}
                      AND {TipsClaimCutoverPredicate}
                      AND COALESCE(cs.paid_at, cs.created_at) < @asOfUtc
                    RETURNING cs.tip_cents AS TipCents, cs.payment_method AS PaymentMethod",
                    new { turnInId = turnIn.Id, tenantId = turnIn.TenantId, workerId = turnIn.WorkerUserId, asOfUtc = claimAsOfUtc },
                    tx)).ToList();

                // Card = the two Stripe rails; the total also carries any other tender.
                var earned = claimed.Sum(c => (long)c.TipCents);
                var card = claimed
                    .Where(c => c.PaymentMethod is "stripe" or "stripe_direct")
                    .Sum(c => (long)c.TipCents);
                if (!ShiftCloseMath.IsValidTipsFromDrawer(turnIn.TipsFromDrawerCents, checked((int)earned)))
                    throw new TipsChangedDuringCloseException(earned, card);   // rolls the whole close back

                turnIn.TipsEarnedCents = checked((int)earned);
                turnIn.CardTipsCents = checked((int)card);
                turnIn.HouseOwesCents = ShiftCloseMath.HouseOwes(turnIn.TipsEarnedCents, turnIn.TipsFromDrawerCents);
                await conn.ExecuteAsync(@"
                    UPDATE cash_turn_in
                    SET tips_earned_cents = @TipsEarnedCents,
                        card_tips_cents = @CardTipsCents,
                        house_owes_cents = @HouseOwesCents
                    WHERE id = @Id AND tenant_id = @TenantId", turnIn, tx);

                // closed_at stamps when the session leaves 'open'; reopening is not a flow. Exactly
                // one row must flip: zero means the drawer was already closed out underneath this
                // request (or the session is not this tenant's), and the whole close rolls back.
                var flipped = await conn.ExecuteAsync(@"
                    UPDATE cash_session
                    SET status = 'turned_in', closed_at = now()
                    WHERE id = @sessionId AND tenant_id = @tenantId AND status = 'open'",
                    new { sessionId = turnIn.CashSessionId, tenantId = turnIn.TenantId }, tx);
                if (flipped != 1) throw new DrawerAlreadyClosedException(turnIn.CashSessionId);

                if (drawerPayout is not null)
                {
                    drawerPayout.CashTurnInId = turnIn.Id;
                    foreach (var (sql, param) in TipPayoutStatements(drawerPayout, payoutLedgerRow))
                        await conn.ExecuteAsync(sql, param, tx);
                }
                return turnIn.Id;
            });
        }

        // One claimed sale's tip, as the close-out's UPDATE ... RETURNING hands it back.
        private sealed class ClaimedTip
        {
            public int TipCents { get; set; }
            public string PaymentMethod { get; set; } = "";
        }

        public async Task<CashTurnIn?> GetTurnInById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<CashTurnIn>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task ConfirmTurnIn(Guid id, Guid tenantId, Guid managerUserId,
            int managerCountedCents, string? note)
        {
            // Only a still-submitted turn-in can be confirmed (idempotent against
            // double-confirm). COALESCE keeps any worker note when the manager adds none.
            // variance_cents is left for the reconciliation report to snapshot against the
            // computed expected (the ledger doesn't yet carry per-worker cash attribution).
            const string sql = @"
                UPDATE cash_turn_in
                SET manager_user_id = @managerUserId,
                    manager_counted_cents = @managerCountedCents,
                    status = 'confirmed',
                    note = COALESCE(@note, note),
                    confirmed_at = now()
                WHERE id = @id AND tenant_id = @tenantId AND status = 'submitted'";
            await _db.Execute(sql, new { id, tenantId, managerUserId, managerCountedCents, note });
        }

        public async Task<List<CashTurnIn>> ListPendingTurnIns(Guid tenantId, Guid? eventId)
        {
            var eventClause = eventId.HasValue ? "AND event_id = @eventId" : "";
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE tenant_id = @tenantId AND status = 'submitted' {eventClause}
                ORDER BY submitted_at";
            var result = await _db.Query<CashTurnIn>(sql, new { tenantId, eventId });
            return result.ToList();
        }

        public async Task<List<CashTurnIn>> ListTurnInsWithoutEvent(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE tenant_id = @tenantId AND event_id IS NULL
                  AND submitted_at >= @fromUtc AND submitted_at < @toUtc
                ORDER BY submitted_at DESC";
            return (await _db.Query<CashTurnIn>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<CashTurnIn>> ListTurnInsByEvent(Guid tenantId, Guid eventId)
        {
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE tenant_id = @tenantId AND event_id = @eventId
                ORDER BY submitted_at DESC";
            var result = await _db.Query<CashTurnIn>(sql, new { tenantId, eventId });
            return result.ToList();
        }

        // ── Tips ─────────────────────────────────────────────────────────────────

        public async Task<WorkerTipTotals> GetWorkerTipTotals(Guid tenantId, Guid userId, DateTime asOfUtc, DateTime windowFromUtc, DateTime? sessionOpenedAtUtc)
        {
            // 'paid' only, the same population ConcessionRepository.GetEmployeeSales counts tips
            // over: an open tab has taken no money yet, a refunded sale gave its tip back, a voided
            // tab never happened. A tab is tipped when it is CLOSED, not when it was opened, so
            // every time here is paid_at (created_at for anything older than paid_at).
            //
            // The tips are every unclaimed, tipped sale up to the cut-off and back as far as the
            // tenant's claims cutover, no further: a tab closed after the worker's last close-out,
            // or a shift that never turned in, is settled on this one instead of on nobody, while
            // tips from before RidePass tracked them stay out. The sale counts are a different,
            // informational question ("how busy was this shift") and keep the review window. One
            // pass over the union of the two populations, each aggregate filtered to its own.
            var earlierClause = sessionOpenedAtUtc.HasValue
                ? "tipped_at < @sessionOpenedAtUtc"
                : "FALSE";
            var sql = $@"
                WITH sales AS (
                    SELECT cs.tip_cents,
                           cs.payment_method,
                           ({UnclaimedTipPredicate} AND {TipsClaimCutoverPredicate}) AS unclaimed,
                           COALESCE(cs.paid_at, cs.created_at)                       AS tipped_at
                    FROM concession_sale cs
                    JOIN tenant t ON t.id = cs.tenant_id
                    WHERE cs.tenant_id = @tenantId
                      AND cs.sold_by_user_id = @userId
                      AND cs.status = 'paid'
                      AND COALESCE(cs.paid_at, cs.created_at) < @asOfUtc
                      AND (({UnclaimedTipPredicate} AND {TipsClaimCutoverPredicate})
                           OR COALESCE(cs.paid_at, cs.created_at) >= @windowFromUtc)
                )
                SELECT COALESCE(SUM(tip_cents) FILTER (WHERE unclaimed), 0)::bigint                                                        AS TipsEarnedCents,
                       COALESCE(SUM(tip_cents) FILTER (WHERE unclaimed AND payment_method IN ('stripe', 'stripe_direct')), 0)::bigint AS CardTipsCents,
                       COALESCE(SUM(tip_cents) FILTER (WHERE unclaimed AND payment_method = 'cash'), 0)::bigint                 AS CashSaleTipsCents,
                       COALESCE(SUM(tip_cents) FILTER (WHERE unclaimed AND {earlierClause}), 0)::bigint                         AS TipsFromEarlierShiftsCents,
                       COUNT(*) FILTER (WHERE tipped_at >= @windowFromUtc)::int                                                   AS SaleCount,
                       COUNT(*) FILTER (WHERE tipped_at >= @windowFromUtc AND payment_method = 'cash')::int                       AS CashSaleCount
                FROM sales";
            return (await _db.Query<WorkerTipTotals>(sql, new { tenantId, userId, asOfUtc, windowFromUtc, sessionOpenedAtUtc })).First();
        }

        public async Task<Guid> CreateTipPayout(TipPayout payout, TenantLedgerEntry? ledgerRow)
        {
            await _db.ExecuteBatch(TipPayoutStatements(payout, ledgerRow));
            return payout.Id;
        }

        // tip_payout first, then its ledger row, then the back-link. Both rows (and a turn-in
        // written in the same transaction) take now(), the transaction clock, so they share one
        // instant.
        private static List<(string Sql, object? Param)> TipPayoutStatements(TipPayout payout, TenantLedgerEntry? ledgerRow)
        {
            if (payout.Id == Guid.Empty) payout.Id = Guid.NewGuid();
            var statements = new List<(string Sql, object? Param)>
            {
                (@"INSERT INTO tip_payout (id, tenant_id, user_id, cash_turn_in_id, amount_cents, source, paid_by_user_id, note)
                   VALUES (@Id, @TenantId, @UserId, @CashTurnInId, @AmountCents, @Source, @PaidByUserId, @Note)", payout),
            };
            if (ledgerRow is null) return statements;

            // The ledger row is pinned to the payout it books, whatever the caller filled in.
            if (ledgerRow.Id == Guid.Empty) ledgerRow.Id = Guid.NewGuid();
            ledgerRow.TenantId = payout.TenantId;
            ledgerRow.EntryKind = "tip_payout";
            ledgerRow.SourceKind = "tip_payout";
            ledgerRow.SourceId = payout.Id;
            statements.Add((@"INSERT INTO tenant_ledger_entry
                                  (id, tenant_id, entry_kind, source_kind, source_id, occurred_at_utc,
                                   gross_cents, stripe_fee_cents, ridepass_cut_cents, net_to_tenant_cents,
                                   memo, payment_method, sold_by_user_id)
                              VALUES (@Id, @TenantId, @EntryKind, @SourceKind, @SourceId, now(),
                                      @GrossCents, @StripeFeeCents, @RidepassCutCents, @NetToTenantCents,
                                      @Memo, @PaymentMethod, @SoldByUserId)", ledgerRow));
            statements.Add((@"UPDATE tip_payout SET ledger_entry_id = @ledgerEntryId
                              WHERE id = @id AND tenant_id = @tenantId",
                            new { ledgerEntryId = ledgerRow.Id, id = payout.Id, tenantId = payout.TenantId }));
            return statements;
        }

        public async Task<List<TipsOwedRow>> ListTipsOwed(Guid tenantId, DateTime activitySinceUtc)
        {
            // Anyone still owed, anyone with tips waiting for a close-out, plus anyone with a
            // TIPPED shift or a payout in the window, so a balance that was just cleared stays
            // visible long enough to be checked. A shift with no tips at all (a bike shop drawer)
            // is not tip activity and stays off the page. The owed test here is a FILTER on the raw
            // sides, not the reported balance; the caller derives and orders by that.
            var sql = TipsOwedSql(singleUser: false) + @"
            WHERE (COALESCE(s.house_owes_cents, 0) - COALESCE(d.by_manager_cents, 0)) > 0
               OR COALESCE(c.unclaimed_cents, 0) > 0
               OR s.last_tip_shift_at >= @activitySinceUtc
               OR d.last_payout_at >= @activitySinceUtc
            ORDER BY Name";
            return (await _db.Query<TipsOwedRow>(sql, new { tenantId, activitySinceUtc })).ToList();
        }

        public async Task<TipsOwedRow?> GetTipsOwedRow(Guid tenantId, Guid userId)
        {
            var sql = TipsOwedSql(singleUser: true) + @"
            WHERE p.user_id = @userId";
            return (await _db.Query<TipsOwedRow>(sql, new { tenantId, userId })).FirstOrDefault();
        }

        public async Task<long> SumOutstandingOwed(Guid tenantId)
        {
            // The End of Day headline: every worker's outstanding balance, summed in one pass over
            // the same CTEs the per-worker rows come from. GREATEST(.., 0) is
            // ShiftCloseMath.OutstandingOwed written in SQL (an over-payment made by hand reads as
            // nothing owed, never as a debt owed back), so this total is exactly the sum of the
            // balances the Tips owed page shows. Only the shifts and paid CTEs take part: a
            // payout with no turn-in behind it clamps to 0, and unclaimed tips are not owed yet.
            var sql = TipsOwedCtes(singleUser: false) + @"
            SELECT COALESCE(SUM(GREATEST(COALESCE(s.house_owes_cents, 0) - COALESCE(d.by_manager_cents, 0), 0)), 0)::bigint
            FROM shifts s
            LEFT JOIN paid d ON d.user_id = s.user_id";
            return (await _db.Query<long>(sql, new { tenantId })).FirstOrDefault();
        }

        public async Task<List<CashPayoutRow>> ListCashPayoutsPaidBy(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Cash managers handed out of their own drawers to settle house-owes, one row per
            // payout so the reconciliation can match each one to whichever session window it fell
            // in, in memory, from a single read. Only 'cash': a drawer tip-out is the worker's own
            // and is already subtracted from the turn-in it belongs to, and payroll moved no cash.
            // A payout whose payer has since been removed belongs to no drawer and is left out.
            const string sql = @"
                SELECT paid_by_user_id AS PayerUserId,
                       created_at      AS CreatedAt,
                       amount_cents    AS AmountCents
                FROM tip_payout
                WHERE tenant_id = @tenantId
                  AND source = 'cash'
                  AND paid_by_user_id IS NOT NULL
                  AND created_at >= @fromUtc
                  AND created_at < @toUtc";
            return (await _db.Query<CashPayoutRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<TipPayoutRow>> ListTipPayouts(Guid tenantId, DateTime sinceUtc, int take = 500)
        {
            // Both users joins follow ids already scoped by p.tenant_id (see TipsOwedSql). The
            // payer join is LEFT for a second reason: paid_by_user_id is null once that account is
            // deleted (Script0296), and the payout must still list.
            const string sql = @"
                SELECT p.id                AS Id,
                       p.user_id           AS UserId,
                       NULLIF(TRIM(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), '') AS Name,
                       u.email             AS Email,
                       p.amount_cents      AS AmountCents,
                       p.source            AS Source,
                       p.paid_by_user_id   AS PaidByUserId,
                       NULLIF(TRIM(COALESCE(b.first_name, '') || ' ' || COALESCE(b.last_name, '')), '') AS PaidByName,
                       b.email             AS PaidByEmail,
                       p.note              AS Note,
                       p.cash_turn_in_id   AS CashTurnInId,
                       p.created_at        AS CreatedAt
                FROM tip_payout p
                LEFT JOIN users u ON u.id = p.user_id
                LEFT JOIN users b ON b.id = p.paid_by_user_id
                WHERE p.tenant_id = @tenantId
                  AND p.created_at >= @sinceUtc
                ORDER BY p.created_at DESC
                LIMIT @take";
            return (await _db.Query<TipPayoutRow>(sql, new { tenantId, sinceUtc, take })).ToList();
        }

        public async Task<long> SumManagerPayouts(Guid tenantId, DateTime sinceUtc)
        {
            // Same window as the history list, but summed in the database so the headline is exact
            // even when the list itself is cut at its row cap.
            const string sql = @"
                SELECT COALESCE(SUM(amount_cents), 0)::bigint
                FROM tip_payout
                WHERE tenant_id = @tenantId
                  AND source IN ('cash', 'payroll')
                  AND created_at >= @sinceUtc";
            return (await _db.Query<long>(sql, new { tenantId, sinceUtc })).FirstOrDefault();
        }

        public async Task<List<string>> TipPayoutSources(Guid tenantId)
        {
            // Three possible values, so DISTINCT over idx_tip_payout_tenant_time is a short walk.
            const string sql = @"
                SELECT DISTINCT source
                FROM tip_payout
                WHERE tenant_id = @tenantId";
            return (await _db.Query<string>(sql, new { tenantId })).ToList();
        }
    }
}
