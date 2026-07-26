namespace Services.Repositories.Data.NewsletterData
{
    /// <summary>
    /// A drip campaign: a trigger, a wait, and an email that goes out on its own from then on.
    /// Distinct from <see cref="EmailCampaign"/>, which is a one-shot broadcast that is sent and
    /// done. Merging the two is what makes a "sent_at" column meaningless.
    /// </summary>
    public class MarketingAutomation
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string TriggerKind { get; set; } = "season_pass_purchased";
        /// <summary>Raw jsonb, e.g. {"fromProductId":"..."}. Parsed by the trigger that owns it.</summary>
        public string TriggerConfig { get; set; } = "{}";
        public bool StopOnUpgrade { get; set; } = true;
        public bool StopWhenUsedUp { get; set; } = true;
        public TimeSpan? SendWindowStart { get; set; }
        public TimeSpan? SendWindowEnd { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Set when armed; the sweep ignores subjects created before it.</summary>
        public DateTime? EnrolFromUtc { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class MarketingAutomationStep
    {
        public Guid Id { get; set; }
        public Guid AutomationId { get; set; }
        public int StepOrder { get; set; }
        /// <summary>Days after the TRIGGER, not after the previous step.</summary>
        public int DelayDays { get; set; }
        public string Subject { get; set; } = null!;
        public string BodyHtml { get; set; } = null!;
        public string? BodyText { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// One row per (step, subject) attempt, whatever the outcome. Doubles as the enrolment record:
    /// a step is due only when no row exists for the pair, which is what makes the sweep
    /// re-runnable without a separate flow-state table.
    /// </summary>
    public class MarketingAutomationSend
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid AutomationId { get; set; }
        public Guid StepId { get; set; }
        public string SubjectKind { get; set; } = null!;
        public Guid SubjectId { get; set; }
        public string Email { get; set; } = null!;
        public string Status { get; set; } = "sent";
        public string? SkipReason { get; set; }
        public DateTime SentAt { get; set; }
    }

    /// <summary>Per-automation rollup for the admin list and the upgrades cross-link panel.</summary>
    public class MarketingAutomationStats
    {
        public Guid AutomationId { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        /// <summary>
        /// Emailed passes that were subsequently upgraded. The number that justifies the spend,
        /// and the reason the send log stores the purchase id rather than just an email address.
        /// </summary>
        public int Conversions { get; set; }
    }

    /// <summary>
    /// A pass that a 'season_pass_purchased' automation step is due to email. Carries everything
    /// the merge fields and the send need, so the sweep does not re-query per rider.
    /// </summary>
    public class AutomationPassSubject
    {
        public Guid PurchaseId { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = null!;
        public string? HolderName { get; set; }
        public string ProductName { get; set; } = null!;
        public DateTime PurchasedAtUtc { get; set; }
        public DateTime ValidToDate { get; set; }
        public int? CreditsRemaining { get; set; }
        /// <summary>Cheapest live upgrade off this pass, when one exists. Null renders the
        /// price merge field empty rather than "$0.00", which would read as a free upgrade.</summary>
        public int? UpgradePriceCents { get; set; }
        public string? UpgradeProductName { get; set; }
    }
}
