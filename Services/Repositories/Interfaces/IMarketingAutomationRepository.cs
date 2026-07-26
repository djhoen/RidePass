using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Drip campaigns. See <c>docs/drip-campaigns.md</c>. Every per-tenant read and write here is
    /// scoped by tenant_id; the one exception is <see cref="ListActiveAcrossTenants"/>, which the
    /// tenant-spanning sweep needs and which is never reachable from a request.
    /// </summary>
    public interface IMarketingAutomationRepository
    {
        Task<List<MarketingAutomation>> ListForTenant(Guid tenantId);
        Task<MarketingAutomation?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(MarketingAutomation a);
        Task Update(MarketingAutomation a);
        Task Delete(Guid id, Guid tenantId);

        /// <summary>
        /// Arm or disarm. Arming stamps <c>enrol_from_utc</c> when <paramref name="enrolFromUtc"/>
        /// is given, which is what stops activation emailing the entire back catalogue.
        /// </summary>
        Task SetActive(Guid id, Guid tenantId, bool isActive, DateTime? enrolFromUtc);

        Task<List<MarketingAutomationStep>> ListSteps(Guid automationId, Guid tenantId);
        /// <summary>Replace the whole step list. Steps are edited as a set, so a diff would be
        /// more code for the same result.</summary>
        Task ReplaceSteps(Guid automationId, Guid tenantId, IEnumerable<MarketingAutomationStep> steps);

        Task<Dictionary<Guid, MarketingAutomationStats>> GetStats(Guid tenantId);

        // ── Sweep ────────────────────────────────────────────────────────────────
        /// <summary>Every armed automation across all tenants. Sweep only, never a request.</summary>
        Task<List<MarketingAutomation>> ListActiveAcrossTenants();

        /// <summary>
        /// Passes a 'season_pass_purchased' step is due to email: old enough, still eligible under
        /// the automation's exit conditions, not suppressed for marketing, and with no send row for
        /// this step yet. Everything the merge fields need comes back with the row.
        /// </summary>
        Task<List<AutomationPassSubject>> ListDuePassSubjects(
            MarketingAutomation automation, MarketingAutomationStep step, Guid? fromProductId, int take);

        /// <summary>
        /// Claim a (step, subject) BEFORE sending, returning the new row id. Null means the unique
        /// index rejected it, i.e. another worker owns this one and this caller must not send.
        /// The claim is written optimistically as 'sent' and corrected by
        /// <see cref="MarkSendFailed"/>: a crash between claim and send loses one email, which is
        /// the right way round for marketing mail.
        /// </summary>
        Task<Guid?> RecordSend(MarketingAutomationSend send);

        Task MarkSendFailed(Guid sendId, Guid tenantId, string reason);

        /// <summary>
        /// Every email sent this calendar month across campaigns AND automations, for the
        /// cumulative volume tier the next charge is priced at. Nothing is excluded: the tiers are
        /// cumulative, so dropping an automation's own history would hold a high-volume tenant on
        /// the cheapest tier forever.
        /// </summary>
        Task<int> CountSentEmailsInMonth(Guid tenantId, DateTime monthStartUtc);

        // ── Activation estimate ──────────────────────────────────────────────────
        /// <summary>How many passes match right now (the backlog the first sweep would send), and
        /// how many passes sold in the last 30 days (the ongoing rate).</summary>
        Task<(int Backlog, int Last30Days)> EstimateAudience(
            Guid tenantId, Guid? fromProductId, int delayDays, bool stopOnUpgrade, bool stopWhenUsedUp,
            DateTime? enrolFromUtc);

        /// <summary>One real eligible pass, for the test send's merge values. Null when the
        /// tenant has none, in which case the test uses placeholders.</summary>
        Task<AutomationPassSubject?> SampleSubject(Guid tenantId, Guid? fromProductId);

        /// <summary>Automations whose trigger targets this pass product, for the upgrades page
        /// cross-link panel. Keyed by from-product id; the "any product" ones key on null.</summary>
        Task<List<MarketingAutomation>> ListByTriggerProduct(Guid tenantId);
    }
}
