using Services.Email;
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
            const string sql = @"
                INSERT INTO marketing_automation
                    (tenant_id, name, trigger_kind, trigger_config, stop_on_upgrade, stop_when_used_up,
                     send_window_start, send_window_end, is_active, created_by_user_id)
                VALUES
                    (@TenantId, @Name, @TriggerKind, @TriggerConfig::jsonb, @StopOnUpgrade, @StopWhenUsedUp,
                     @SendWindowStart, @SendWindowEnd, false, @CreatedByUserId)
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
            // Arming with a cut-off stamps it; arming without one (include the backlog) clears it;
            // disarming leaves it alone so a later re-arm can keep the same enrolment boundary.
            const string sql = @"
                UPDATE marketing_automation
                SET is_active = @isActive,
                    enrol_from_utc = CASE WHEN @isActive THEN @enrolFromUtc ELSE enrol_from_utc END,
                    updated_at = now()
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
                       s.delay_days AS DelayDays, s.anchor AS Anchor, s.offset_days AS OffsetDays,
                       s.send_on AS SendOn, s.subject AS Subject,
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
                // history. Tolerable while steps are authored before arming; the API refuses to
                // edit an armed automation for exactly this reason.
                ("DELETE FROM marketing_automation_step WHERE automation_id = @automationId",
                    new { automationId }),
            };
            var order = 0;
            foreach (var s in steps)
            {
                statements.Add((@"
                    INSERT INTO marketing_automation_step
                        (automation_id, step_order, delay_days, anchor, offset_days, send_on, subject, body_html, body_text)
                    VALUES (@automationId, @stepOrder, @delayDays, @anchor, @offsetDays, CAST(@sendOn AS date), @subject, @bodyHtml, @bodyText)",
                    new
                    {
                        automationId,
                        stepOrder = order++,
                        // delay_days is the legacy column: it mirrors the offset for purchase-anchored
                        // steps and is 0 for every other anchor.
                        delayDays = s.Anchor == AutomationTriggers.Anchors.Purchase ? Math.Max(0, s.OffsetDays) : 0,
                        anchor = s.Anchor,
                        offsetDays = s.Anchor == AutomationTriggers.Anchors.FixedDate ? 0 : s.OffsetDays,
                        sendOn = s.Anchor == AutomationTriggers.Anchors.FixedDate ? s.SendOn : null,
                        subject = s.Subject,
                        bodyHtml = s.BodyHtml,
                        bodyText = s.BodyText,
                    }));
            }
            await _db.ExecuteBatch(statements);
        }

        public async Task<Dictionary<Guid, MarketingAutomationStats>> GetStats(Guid tenantId)
        {
            // Conversions join the emailed purchase to any pass that replaced it (pass trigger only).
            // An upgrade that would have happened anyway still counts, which is the same
            // attribution every email platform reports and the same caveat.
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

        // ── Timing ───────────────────────────────────────────────────────────────
        //
        // A step is due for a subject when anchor + offset has passed. The anchor is a column of
        // the subject row (purchase time, event start/end, pass expiry) or, for fixed_date, a
        // calendar day the C# side has already compared against the tenant's today. A subject
        // whose send time passed BEFORE it bought is reported with DueBeforePurchase = true so the
        // sweep records a skip instead of emailing a "two weeks out" reminder to someone who
        // bought yesterday.

        private static string AnchorExpr(string triggerKind, string anchor)
        {
            var isEvent = triggerKind == AutomationTriggers.EventTicketPurchased;
            return anchor switch
            {
                AutomationTriggers.Anchors.Purchase => isEvent ? "p.created_at" : "sp.created_at",
                AutomationTriggers.Anchors.EventStart => "e.starts_at",
                AutomationTriggers.Anchors.EventEnd => "e.ends_at",
                AutomationTriggers.Anchors.PassExpiry => "(sp.valid_to_date::timestamptz)",
                _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown anchor"),
            };
        }

        private static (string Due, string Skip) TimingClauses(string triggerKind, string anchor)
        {
            var purchase = triggerKind == AutomationTriggers.EventTicketPurchased ? "p.created_at" : "sp.created_at";
            if (anchor == AutomationTriggers.Anchors.FixedDate)
            {
                // Due-ness was decided in C# against the tenant's calendar; here only the
                // bought-after-the-date skip remains.
                return ("TRUE", $"(CAST(@sendOn AS date) < ({purchase})::date)");
            }
            var when = $"({AnchorExpr(triggerKind, anchor)} + make_interval(days => @offsetDays))";
            var skip = anchor == AutomationTriggers.Anchors.Purchase ? "FALSE" : $"({when} < {purchase})";
            return ($"{when} <= @nowUtc", skip);
        }

        private const string PassSelect = @"
                SELECT 'season_pass_purchase' AS SubjectKind,
                       sp.id                AS SubjectId,
                       sp.tenant_id         AS TenantId,
                       sp.purchaser_email   AS Email,
                       COALESCE(NULLIF(TRIM(CONCAT_WS(' ', sp.holder_first_name, sp.holder_last_name)), ''),
                                sp.purchaser_name)                AS HolderName,
                       pr.name              AS ProductName,
                       sp.created_at        AS PurchasedAtUtc,
                       sp.valid_to_date     AS ValidToDate,
                       sp.credits_remaining AS CreditsRemaining,
                       up.price_cents       AS UpgradePriceCents,
                       tp.name              AS UpgradeProductName,
                       {SKIP}               AS DueBeforePurchase
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
                LEFT JOIN season_pass_product tp ON tp.id = up.to_product_id";

        /// <summary>
        /// The eligibility predicate for pass subjects, shared by the sweep and the activation
        /// estimate so the number a tenant is shown before arming is produced by the same rules
        /// that decide who actually gets emailed. Expects sp = season_pass_purchase, pr = product.
        /// </summary>
        private const string PassEligible = @"
            sp.tenant_id = @tenantId
            AND sp.status = 'paid'
            AND sp.purchaser_email IS NOT NULL AND sp.purchaser_email <> ''
            AND (@fromProductId IS NULL OR sp.product_id = @fromProductId)
            -- CAST: a null DateTime? parameter otherwise reaches Postgres untyped (42P08).
            AND (CAST(@enrolFromUtc AS timestamptz) IS NULL OR sp.created_at >= CAST(@enrolFromUtc AS timestamptz))
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

        private const string EventSelect = @"
                SELECT 'event_ticket_purchase' AS SubjectKind,
                       p.id                 AS SubjectId,
                       p.tenant_id          AS TenantId,
                       p.purchaser_email    AS Email,
                       p.purchaser_name     AS HolderName,
                       e.title              AS ProductName,
                       p.created_at         AS PurchasedAtUtc,
                       e.id                 AS EventId,
                       e.starts_at          AS EventStartsAt,
                       e.ends_at            AS EventEndsAt,
                       e.all_day            AS EventAllDay,
                       e.location_label     AS EventLocation,
                       t.name               AS TicketTierName,
                       {SKIP}               AS DueBeforePurchase
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id";

        /// <summary>
        /// Eligibility for event-ticket subjects. 'redeemed' is a paid ticket that was scanned at
        /// the gate and still a purchaser; a refunded or cancelled ticket drops out, and so does
        /// every ticket to a cancelled event (a "what to bring" email for a cancelled camp is the
        /// one email nobody wants). Expects p = event_ticket_purchase, e = event.
        /// </summary>
        private const string EventEligible = @"
            p.tenant_id = @tenantId
            AND p.status IN ('paid', 'redeemed')
            AND p.purchaser_email IS NOT NULL AND p.purchaser_email <> ''
            AND e.status = 'scheduled'
            AND (@eventId IS NULL OR e.id = @eventId)
            AND (@eventTypeId IS NULL OR e.event_type_id = @eventTypeId)
            AND (CAST(@enrolFromUtc AS timestamptz) IS NULL OR p.created_at >= CAST(@enrolFromUtc AS timestamptz))
            AND NOT EXISTS (
                    SELECT 1 FROM email_suppression es
                    WHERE lower(es.email) = lower(p.purchaser_email)
                      AND (es.tenant_id IS NULL OR es.tenant_id = p.tenant_id)
                      AND es.scope IN ('all', 'marketing'))";

        private static bool FixedDateNotYetDue(MarketingAutomationStep step, DateTime tenantToday)
            => step.Anchor == AutomationTriggers.Anchors.FixedDate
               && (step.SendOn is null || step.SendOn.Value.Date > tenantToday.Date);

        public async Task<List<AutomationSubject>> ListDueSubjects(
            MarketingAutomation a, MarketingAutomationStep step, int take, DateTime nowUtc, DateTime tenantToday)
        {
            if (FixedDateNotYetDue(step, tenantToday)) return new List<AutomationSubject>();
            var cfg = AutomationTriggerConfig.For(a);
            var (due, skip) = TimingClauses(a.TriggerKind, step.Anchor);
            var subjectKind = AutomationTriggers.SubjectKindFor(a.TriggerKind);

            if (a.TriggerKind == AutomationTriggers.EventTicketPurchased)
            {
                var sql = EventSelect.Replace("{SKIP}", skip) + $@"
                WHERE {EventEligible}
                  AND {due}
                  AND NOT EXISTS (
                        SELECT 1 FROM marketing_automation_send ms
                        WHERE ms.step_id = @stepId AND ms.subject_kind = @subjectKind AND ms.subject_id = p.id)
                ORDER BY p.created_at
                LIMIT @take";
                return (await _db.Query<AutomationSubject>(sql, new
                {
                    tenantId = a.TenantId, stepId = step.Id, subjectKind, take, nowUtc,
                    eventId = cfg.EventId, eventTypeId = cfg.EventTypeId,
                    enrolFromUtc = a.EnrolFromUtc,
                    offsetDays = step.OffsetDays, sendOn = step.SendOn,
                })).ToList();
            }
            else
            {
                var sql = PassSelect.Replace("{SKIP}", skip) + $@"
                WHERE {PassEligible}
                  AND {due}
                  AND NOT EXISTS (
                        SELECT 1 FROM marketing_automation_send ms
                        WHERE ms.step_id = @stepId AND ms.subject_kind = @subjectKind AND ms.subject_id = sp.id)
                ORDER BY sp.created_at
                LIMIT @take";
                return (await _db.Query<AutomationSubject>(sql, new
                {
                    tenantId = a.TenantId, stepId = step.Id, subjectKind, take, nowUtc,
                    fromProductId = cfg.FromProductId,
                    enrolFromUtc = a.EnrolFromUtc,
                    stopOnUpgrade = a.StopOnUpgrade, stopWhenUsedUp = a.StopWhenUsedUp,
                    offsetDays = step.OffsetDays, sendOn = step.SendOn,
                })).ToList();
            }
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
            MarketingAutomation a, MarketingAutomationStep firstStep, DateTime nowUtc, DateTime tenantToday, DateTime? enrolFromUtc)
        {
            // Backlog uses the SAME predicate as the sweep, minus the send-log check (nothing has
            // been sent yet), so the number shown before arming is the number that goes out.
            var cfg = AutomationTriggerConfig.For(a);
            var (due, _) = TimingClauses(a.TriggerKind, firstStep.Anchor);
            if (FixedDateNotYetDue(firstStep, tenantToday)) due = "FALSE";

            if (a.TriggerKind == AutomationTriggers.EventTicketPurchased)
            {
                var sql = $@"
                    SELECT (
                        SELECT COUNT(*)::int
                        FROM event_ticket_purchase p
                        JOIN event_ticket_tier t ON t.id = p.tier_id
                        JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                        WHERE {EventEligible} AND {due}
                    ) AS Backlog,
                    (
                        SELECT COUNT(*)::int
                        FROM event_ticket_purchase p
                        JOIN event_ticket_tier t ON t.id = p.tier_id
                        JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                        WHERE p.tenant_id = @tenantId AND p.status IN ('paid', 'redeemed')
                          AND (@eventId IS NULL OR e.id = @eventId)
                          AND (@eventTypeId IS NULL OR e.event_type_id = @eventTypeId)
                          AND p.created_at >= now() - interval '30 days'
                    ) AS Last30Days";
                return (await _db.Query<(int Backlog, int Last30Days)>(sql, new
                {
                    tenantId = a.TenantId, nowUtc,
                    eventId = cfg.EventId, eventTypeId = cfg.EventTypeId,
                    enrolFromUtc,
                    offsetDays = firstStep.OffsetDays, sendOn = firstStep.SendOn,
                })).First();
            }
            else
            {
                var sql = $@"
                    SELECT (
                        SELECT COUNT(*)::int
                        FROM season_pass_purchase sp
                        JOIN season_pass_product pr ON pr.id = sp.product_id
                        WHERE {PassEligible} AND {due}
                    ) AS Backlog,
                    (
                        SELECT COUNT(*)::int
                        FROM season_pass_purchase sp
                        WHERE sp.tenant_id = @tenantId AND sp.status = 'paid'
                          AND (@fromProductId IS NULL OR sp.product_id = @fromProductId)
                          AND sp.created_at >= now() - interval '30 days'
                    ) AS Last30Days";
                return (await _db.Query<(int Backlog, int Last30Days)>(sql, new
                {
                    tenantId = a.TenantId, nowUtc,
                    fromProductId = cfg.FromProductId,
                    enrolFromUtc,
                    stopOnUpgrade = a.StopOnUpgrade, stopWhenUsedUp = a.StopWhenUsedUp,
                    offsetDays = firstStep.OffsetDays, sendOn = firstStep.SendOn,
                })).First();
            }
        }

        public async Task<AutomationSubject?> SampleSubject(MarketingAutomation a)
        {
            var cfg = AutomationTriggerConfig.For(a);
            if (a.TriggerKind == AutomationTriggers.EventTicketPurchased)
            {
                var sql = EventSelect.Replace("{SKIP}", "FALSE") + @"
                WHERE p.tenant_id = @tenantId
                  AND p.status IN ('paid', 'redeemed')
                  AND (@eventId IS NULL OR e.id = @eventId)
                  AND (@eventTypeId IS NULL OR e.event_type_id = @eventTypeId)
                ORDER BY p.created_at DESC
                LIMIT 1";
                return (await _db.Query<AutomationSubject>(sql,
                    new { tenantId = a.TenantId, eventId = cfg.EventId, eventTypeId = cfg.EventTypeId })).FirstOrDefault();
            }
            else
            {
                var sql = PassSelect.Replace("{SKIP}", "FALSE") + @"
                WHERE sp.tenant_id = @tenantId
                  AND sp.status = 'paid'
                  AND (@fromProductId IS NULL OR sp.product_id = @fromProductId)
                ORDER BY sp.created_at DESC
                LIMIT 1";
                return (await _db.Query<AutomationSubject>(sql,
                    new { tenantId = a.TenantId, fromProductId = cfg.FromProductId })).FirstOrDefault();
            }
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
