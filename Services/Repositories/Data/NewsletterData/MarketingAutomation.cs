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
        /// <summary>One of Services.Email.AutomationTriggers.Kinds.</summary>
        public string TriggerKind { get; set; } = "season_pass_purchased";
        /// <summary>Raw jsonb; parsed by <c>AutomationTriggerConfig</c>.</summary>
        public string TriggerConfig { get; set; } = "{}";
        // Pass-trigger exit conditions. Ignored by other triggers.
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
        /// <summary>Legacy: days after purchase. Kept equal to OffsetDays for purchase-anchored steps.</summary>
        public int DelayDays { get; set; }
        /// <summary>What the wait is measured from: one of AutomationTriggers.Anchors.</summary>
        public string Anchor { get; set; } = "purchase";
        /// <summary>Signed days from the anchor; negative means before. Unused for fixed_date.</summary>
        public int OffsetDays { get; set; }
        /// <summary>The calendar date for a fixed_date step (tenant-local day).</summary>
        public DateTime? SendOn { get; set; }
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
        /// Emailed passes that were subsequently upgraded. Pass trigger only; the number that
        /// justifies the spend, and the reason the send log stores the purchase id.
        /// </summary>
        public int Conversions { get; set; }
    }

    /// <summary>
    /// A purchase an automation step is due to email: a season pass or an event ticket. Carries
    /// everything the merge fields and the send need, so the sweep does not re-query per rider.
    /// Fields that do not apply to the subject's kind stay null.
    /// </summary>
    public class AutomationSubject
    {
        /// <summary>'season_pass_purchase' or 'event_ticket_purchase' (the send log's subject_kind).</summary>
        public string SubjectKind { get; set; } = null!;
        public Guid SubjectId { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = null!;
        public string? HolderName { get; set; }
        /// <summary>The pass product name, or the event title.</summary>
        public string ProductName { get; set; } = null!;
        public DateTime PurchasedAtUtc { get; set; }

        // Pass subjects
        public DateTime? ValidToDate { get; set; }
        public int? CreditsRemaining { get; set; }
        /// <summary>Cheapest live upgrade off this pass, when one exists. Null renders the
        /// price merge field empty rather than "$0.00", which would read as a free upgrade.</summary>
        public int? UpgradePriceCents { get; set; }
        public string? UpgradeProductName { get; set; }

        // Event subjects
        public Guid? EventId { get; set; }
        public DateTime? EventStartsAt { get; set; }
        public DateTime? EventEndsAt { get; set; }
        public bool EventAllDay { get; set; }
        public string? EventLocation { get; set; }
        public string? TicketTierName { get; set; }

        /// <summary>
        /// True when this step's send time had already passed when the rider bought (a camp
        /// ticket bought the day before does not get the "two weeks out" email). The sweep records
        /// a skip instead of sending. Decided 2026-09-10.
        /// </summary>
        public bool DueBeforePurchase { get; set; }
    }
}
