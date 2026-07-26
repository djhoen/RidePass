using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class MarketingAutomationRepository : IMarketingAutomationRepository
    {
        private readonly IDbHelper _db;

        public MarketingAutomationRepository(IDbHelper db) => _db = db;

        private const string Columns = @"
            id                 AS Id,
            tenant_id          AS TenantId,
            name               AS Name,
            trigger_kind       AS TriggerKind,
            trigger_config::text AS TriggerConfig,
            stop_on_upgrade    AS StopOnUpgrade,
            stop_when_used_up  AS StopWhenUsedUp,
            send_window_start  AS SendWindowStart,
            send_window_end    AS SendWindowEnd,
            is_active          AS IsActive,
            enrol_from_utc     AS EnrolFromUtc,
            created_by_user_id AS CreatedByUserId,
            created_at         AS CreatedAt,
            updated_at         AS UpdatedAt";

        public async Task<List<MarketingAutomation>> ListForTenant(Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM marketing_automation WHERE tenant_id = @tenantId ORDER BY created_at DESC";
            return (await _db.Query<MarketingAutomation>(sql, new { tenantId })).ToList();
        }

        public async Task<MarketingAutomation?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM marketing_automation WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<MarketingAutomation>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> Create(MarketingAutomation a)
        {
            // is_active is deliberately NOT settable here: a new automation is always a draft, so
            // there is no path where saving a form arms one by accident.
            const string sql = @"
                INSERT INTO marketing_automation
                    (tenant_id, name, trigger_kind, trigger_config, stop_on_upgrade, stop_when_used_up,
                     send_window_start, send_window_end, created_by_user_id)
                VALUES
                    (@TenantId, @Name, @TriggerKind, @TriggerConfig::jsonb, @StopOnUpgrade, @StopWhenUsedUp,
                     @SendWindowStart, @SendWindowEnd, @CreatedByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, a)).First();
        }

        public async Task Update(MarketingAutomation a)
        {
            const string sql = @"
                UPDATE marketing_automation
                SET name              = @Name,
                    trigger_kind      = @TriggerKind,
                    trigger_config    = @TriggerConfig::jsonb,
                    stop_on_upgrade   = @StopOnUpgrade,
                    stop_when_used_up = @StopWhenUsedUp,
                    send_window_start = @SendWindowStart,
                    send_window_end   = @SendWindowEnd,
                    updated_at        = now()
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, a);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM marketing_automation WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        public async Task SetActive(Guid id, Guid tenantId, bool isActive, DateTime? enrolFromUtc)
        {
            // enrol_from_utc is only ever written on the way UP, and only when the caller asked
            // for it. Disarming leaves it alone so a re-arm doesn't silently re-open the back
            // catalogue the first arming excluded.
            const string sql = @"
                UPDATE marketing_automation
                SET is_active      = @isActive,
                    enrol_from_utc = CASE WHEN @isActive AND @enrolFromUtc IS NOT NULL
                                          THEN @enrolFromUtc ELSE enrol_from_utc END,
                    updated_at     = now()
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, isActive, enrolFromUtc });
        }

        public async Task<List<MarketingAutomationStep>> ListSteps(Guid automationId, Guid tenantId)
        {
            // Scoped through the parent rather than trusting the caller to have checked. Every
            // current call site does check, but this is a child-by-parent-id read and one future
            // caller that forgets would leak another tenant's email copy.
            const string sql = @"
                SELECT s.id AS Id, s.automation_id AS AutomationId, s.step_order AS StepOrder,
                       s.delay_days AS DelayDays, s.subject AS Subject,
                       s.body_html AS BodyHtml, s.body_text AS BodyText, s.created_at AS CreatedAt
                FROM marketing_automation_step s
                JOIN marketing_automation a ON a.id = s.automation_id AND a.tenant_id = @tenantId
                WHERE s.automation_id = @automationId
                ORDER BY s.step_order";
            return (await _db.Query<MarketingAutomationStep>(sql, new { automationId, tenantId })).ToList();
        }

        public async Task ReplaceSteps(Guid automationId, Guid tenantId, IEnumerable<MarketingAutomationStep> steps)
        {
            // Scoped through the parent: a foreign automation id writes nothing rather than
            // rewriting another tenant's sequence.
            const string ownedSql = @"
                SELECT EXISTS (SELECT 1 FROM marketing_automation
                               WHERE id = @automationId AND tenant_id = @tenantId)";
            if (!(await _db.Query<bool>(ownedSql, new { automationId, tenantId })).First()) return;

            var statements = new List<(string, object?)>
            {
                // Deleting a step cascades its send rows, so an edited automation can re-send its
                // history. Tolerable while steps are authored before arming; revisit if editing an
                // ARMED automation's steps becomes a normal thing to do.
                ("DELETE FROM marketing_automation_step WHERE automation_id = @automationId",
                    new { automationId }),
            };
            var order = 0;
            foreach (var s in steps)
            {
                statements.Add((@"
                    INSERT INTO marketing_automation_step
                        (automation_id, step_order, delay_days, subject, body_html, body_text)
                    VALUES (@automationId, @stepOrder, @delayDays, @subject, @bodyHtml, @bodyText)",
                    new
                    {
                        automationId,
                        stepOrder = order++,
                        delayDays = s.DelayDays,
                        subject = s.Subject,
                        bodyHtml = s.BodyHtml,
                        bodyText = s.BodyText,
                    }));
            }
            await _db.ExecuteBatch(statements);
        }

        public async Task<Dictionary<Guid, MarketingAutomationStats>> GetStats(Guid tenantId)
        {
            // Conversions join the emailed purchase to any pass that replaced it. An upgrade that
            // would have happened anyway still counts, which is the same attribution every email
            // platform reports and the same caveat.
            const string sql = @"
                SELECT s.automation_id                                          AS AutomationId,
                       COUNT(*) FILTER (WHERE s.status = 'sent')::int           AS Sent,
                       COUNT(*) FILTER (WHERE s.status = 'failed')::int         AS Failed,
                       COUNT(*) FILTER (WHERE s.status = 'skipped')::int        AS Skipped,
                       COUNT(DISTINCT up.upgraded_from_purchase_id)::int        AS Conversions
                FROM marketing_automation_send s
                LEFT JOIN season_pass_purchase up
                       ON up.upgraded_from_purchase_id = s.subject_id
                      AND up.tenant_id = s.tenant_id
                      AND up.status = 'paid'
                      AND s.status = 'sent'
                      AND s.subject_kind = 'season_pass_purchase'
                WHERE s.tenant_id = @tenantId
                GROUP BY s.automation_id";
            var rows = await _db.Query<MarketingAutomationStats>(sql, new { tenantId });
            return rows.ToDictionary(r => r.AutomationId);
        }

        public async Task<List<MarketingAutomation>> ListActiveAcrossTenants()
        {
            // Intentionally unscoped: the sweep runs outside any request and must see every
            // tenant. Every downstream query it drives carries the automation's own tenant_id.
            var sql = $"SELECT {Columns} FROM marketing_automation WHERE is_active ORDER BY tenant_id";
            return (await _db.Query<MarketingAutomation>(sql)).ToList();
        }

        /// <summary>
        /// The eligibility predicate shared by the sweep and the activation estimate, so the
        /// number a tenant is shown before arming is produced by the same rules that decide who
        /// actually gets emailed. Expects `sp` = season_pass_purchase, `pr` = season_pass_product.
        /// </summary>
        private const string SubjectEligibleExpr = @"
            sp.status = 'paid'
            AND sp.purchaser_email IS NOT NULL AND sp.purchaser_email <> ''
            AND (@fromProductId IS NULL OR sp.product_id = @fromProductId)
            AND sp.created_at <= @dueBefore
            AND (@enrolFromUtc IS NULL OR sp.created_at >= @enrolFromUtc)
            -- Exit conditions, evaluated HERE (send time) rather than at enrolment: state
            -- changing during the wait is the entire point of the wait.
            AND (NOT @stopOnUpgrade OR NOT EXISTS (
                    SELECT 1 FROM season_pass_purchase u2
                    WHERE u2.upgraded_from_purchase_id = sp.id
                      AND u2.status NOT IN ('failed', 'cancelled', 'refunded', 'abandoned')))
            AND (NOT @stopWhenUsedUp OR (
                    CURRENT_DATE BETWEEN sp.valid_from_date AND sp.valid_to_date
                    AND (pr.kind <> 'credits' OR COALESCE(sp.credits_remaining, 0) > 0)))
            -- Compliance: hard bounces and marketing opt-outs, global or this tenant's.
            AND NOT EXISTS (
                    SELECT 1 FROM email_suppression es
                    WHERE lower(es.email) = lower(sp.purchaser_email)
                      AND (es.tenant_id IS NULL OR es.tenant_id = sp.tenant_id)
                      AND es.scope IN ('all', 'marketing'))";

        public async Task<List<AutomationPassSubject>> ListDuePassSubjects(
            MarketingAutomation automation, MarketingAutomationStep step, Guid? fromProductId, int take)
        {
            var sql = $@"
                SELECT sp.id                AS PurchaseId,
                       sp.tenant_id         AS TenantId,
                       sp.purchaser_email   AS Email,
                       COALESCE(NULLIF(TRIM(CONCAT_WS(' ', sp.holder_first_name, sp.holder_last_name)), ''),
                                sp.purchaser_name)                AS HolderName,
                       pr.name              AS ProductName,
                       sp.created_at        AS PurchasedAtUtc,
                       sp.valid_to_date     AS ValidToDate,
                       sp.credits_remaining AS CreditsRemaining,
                       up.price_cents       AS UpgradePriceCents,
                       tp.name              AS UpgradeProductName
                FROM season_pass_purchase sp
                JOIN season_pass_product pr ON pr.id = sp.product_id
                -- Cheapest live upgrade off this pass, for the merge fields. LEFT so an automation
                -- can still send when no upgrade is configured; the price token renders empty.
                LEFT JOIN LATERAL (
                    SELECT u.price_cents, u.to_product_id
                    FROM season_pass_upgrade_path u
                    WHERE u.tenant_id = sp.tenant_id AND u.from_product_id = sp.product_id AND u.is_active
                    ORDER BY u.price_cents
                    LIMIT 1
                ) up ON true
                LEFT JOIN season_pass_product tp ON tp.id = up.to_product_id
                WHERE sp.tenant_id = @tenantId
                  AND {SubjectEligibleExpr}
                  AND NOT EXISTS (
                        SELECT 1 FROM marketing_automation_send ms
                        WHERE ms.step_id = @stepId
                          AND ms.subject_kind = 'season_pass_purchase'
                          AND ms.subject_id = sp.id)
                ORDER BY sp.created_at
                LIMIT @take";
            return (await _db.Query<AutomationPassSubject>(sql, new
            {
                tenantId = automation.TenantId,
                stepId = step.Id,
                fromProductId,
                dueBefore = DateTime.UtcNow.AddDays(-step.DelayDays),
                enrolFromUtc = automation.EnrolFromUtc,
                stopOnUpgrade = automation.StopOnUpgrade,
                stopWhenUsedUp = automation.StopWhenUsedUp,
                take,
            })).ToList();
        }

        public async Task<Guid?> RecordSend(MarketingAutomationSend send)
        {
            // ON CONFLICT DO NOTHING against uk_automation_send_once: two workers racing the same
            // (step, subject) both pass the NOT EXISTS above, and this is what makes exactly one
            // of them the sender. The loser gets null and must not send.
            const string sql = @"
                INSERT INTO marketing_automation_send
                    (tenant_id, automation_id, step_id, subject_kind, subject_id, email, status, skip_reason)
                VALUES
                    (@TenantId, @AutomationId, @StepId, @SubjectKind, @SubjectId, @Email, @Status, @SkipReason)
                ON CONFLICT (step_id, subject_kind, subject_id) DO NOTHING
                RETURNING id";
            var rows = await _db.Query<Guid>(sql, send);
            return rows.Cast<Guid?>().FirstOrDefault();
        }

        public async Task MarkSendFailed(Guid sendId, Guid tenantId, string reason)
        {
            await _db.Execute(@"
                UPDATE marketing_automation_send SET status = 'failed', skip_reason = @reason
                WHERE id = @sendId AND tenant_id = @tenantId",
                new { sendId, tenantId, reason });
        }

        public async Task<int> CountSentEmailsInMonth(Guid tenantId, DateTime monthStartUtc)
        {
            // Campaigns AND automations bill from one cumulative pool, so the tier a send lands in
            // depends on both. Nothing is excluded: an automation's own prior sends this month are
            // exactly what should have pushed it into a higher tier.
            const string sql = @"
                SELECT (
                    SELECT COUNT(*) FROM email_campaign_send cs
                    JOIN email_campaign c ON c.id = cs.campaign_id
                    WHERE c.tenant_id = @tenantId AND cs.status = 'sent' AND cs.sent_at >= @monthStartUtc
                ) + (
                    SELECT COUNT(*) FROM marketing_automation_send ms
                    WHERE ms.tenant_id = @tenantId AND ms.status = 'sent'
                      AND ms.sent_at >= @monthStartUtc
                )";
            return (await _db.Query<int>(sql, new { tenantId, monthStartUtc })).First();
        }

        public async Task<(int Backlog, int Last30Days)> EstimateAudience(
            Guid tenantId, Guid? fromProductId, int delayDays, bool stopOnUpgrade, bool stopWhenUsedUp,
            DateTime? enrolFromUtc)
        {
            // Backlog uses the SAME predicate as the sweep, minus the send-log check (nothing has
            // been sent yet), so the number shown before arming is the number that goes out.
            var sql = $@"
                SELECT (
                    SELECT COUNT(*)::int
                    FROM season_pass_purchase sp
                    JOIN season_pass_product pr ON pr.id = sp.product_id
                    WHERE sp.tenant_id = @tenantId AND {SubjectEligibleExpr}
                ) AS Backlog,
                (
                    SELECT COUNT(*)::int
                    FROM season_pass_purchase sp
                    WHERE sp.tenant_id = @tenantId AND sp.status = 'paid'
                      AND (@fromProductId IS NULL OR sp.product_id = @fromProductId)
                      AND sp.created_at >= now() - interval '30 days'
                ) AS Last30Days";
            var row = (await _db.Query<(int Backlog, int Last30Days)>(sql, new
            {
                tenantId,
                fromProductId,
                dueBefore = DateTime.UtcNow.AddDays(-delayDays),
                enrolFromUtc,
                stopOnUpgrade,
                stopWhenUsedUp,
            })).First();
            return row;
        }

        public async Task<AutomationPassSubject?> SampleSubject(Guid tenantId, Guid? fromProductId)
        {
            const string sql = @"
                SELECT sp.id                AS PurchaseId,
                       sp.tenant_id         AS TenantId,
                       sp.purchaser_email   AS Email,
                       COALESCE(NULLIF(TRIM(CONCAT_WS(' ', sp.holder_first_name, sp.holder_last_name)), ''),
                                sp.purchaser_name)                AS HolderName,
                       pr.name              AS ProductName,
                       sp.created_at        AS PurchasedAtUtc,
                       sp.valid_to_date     AS ValidToDate,
                       sp.credits_remaining AS CreditsRemaining,
                       up.price_cents       AS UpgradePriceCents,
                       tp.name              AS UpgradeProductName
                FROM season_pass_purchase sp
                JOIN season_pass_product pr ON pr.id = sp.product_id
                LEFT JOIN LATERAL (
                    SELECT u.price_cents, u.to_product_id
                    FROM season_pass_upgrade_path u
                    WHERE u.tenant_id = sp.tenant_id AND u.from_product_id = sp.product_id AND u.is_active
                    ORDER BY u.price_cents
                    LIMIT 1
                ) up ON true
                LEFT JOIN season_pass_product tp ON tp.id = up.to_product_id
                WHERE sp.tenant_id = @tenantId
                  AND sp.status = 'paid'
                  AND (@fromProductId IS NULL OR sp.product_id = @fromProductId)
                ORDER BY sp.created_at DESC
                LIMIT 1";
            return (await _db.Query<AutomationPassSubject>(sql, new { tenantId, fromProductId })).FirstOrDefault();
        }

        public async Task<List<MarketingAutomation>> ListByTriggerProduct(Guid tenantId)
        {
            var sql = $@"
                SELECT {Columns}
                FROM marketing_automation
                WHERE tenant_id = @tenantId AND trigger_kind = 'season_pass_purchased'
                ORDER BY is_active DESC, created_at DESC";
            return (await _db.Query<MarketingAutomation>(sql, new { tenantId })).ToList();
        }
    }
}
