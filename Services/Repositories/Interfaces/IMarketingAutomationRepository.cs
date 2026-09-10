using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Drip campaigns. See <c>docs/drip-campaigns.md</c> and <c>docs/dynamic-campaigns-plan.md</c>.
    /// Every per-tenant read and write here is scoped by tenant_id; the one exception is
    /// <see cref="ListActiveAcrossTenants"/>, which the tenant-spanning sweep needs and which is
    /// never reachable from a request.
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
        /// Subjects a step is due to email, for whichever trigger the automation uses: the step's
        /// anchor plus offset has passed, the purchase is still eligible under the trigger's exit
        /// rules, the address is not suppressed for marketing, and no send row exists for this
        /// step yet. Subjects whose send time had already passed when they bought come back with
        /// <see cref="AutomationSubject.DueBeforePurchase"/> set so the sweep records a skip.
        /// <paramref name="tenantToday"/> is the tenant-local calendar day for fixed-date steps.
        /// </summary>
        Task<List<AutomationSubject>> ListDueSubjects(
            MarketingAutomation automation, MarketingAutomationStep step, int take,
            DateTime nowUtc, DateTime tenantToday);

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
        /// cumulative volume tier the next charge is priced at.
        /// </summary>
        Task<int> CountSentEmailsInMonth(Guid tenantId, DateTime monthStartUtc);

        // ── Activation estimate ──────────────────────────────────────────────────
        /// <summary>How many subjects match the first step right now (the backlog the first sweep
        /// would send), and how many matching purchases happened in the last 30 days (the ongoing
        /// rate).</summary>
        Task<(int Backlog, int Last30Days)> EstimateAudience(
            MarketingAutomation automation, MarketingAutomationStep firstStep,
            DateTime nowUtc, DateTime tenantToday, DateTime? enrolFromUtc);

        /// <summary>One real recent subject matching the trigger, for the test send's merge values.
        /// Null when the tenant has none, in which case the test uses placeholders.</summary>
        Task<AutomationSubject?> SampleSubject(MarketingAutomation automation);

        /// <summary>Pass-trigger automations, for the upgrades page cross-link panel.</summary>
        Task<List<MarketingAutomation>> ListByTriggerProduct(Guid tenantId);
    }
}
