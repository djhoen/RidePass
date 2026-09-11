using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>Row in the Automations list.</summary>
    public class AutomationListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>One of the registry's kinds: season_pass_purchased, event_ticket_purchased.</summary>
        public string TriggerKind { get; set; } = string.Empty;
        /// <summary>"Buys a ticket to Spring Camp", "Buys any pass".</summary>
        public string TriggerLabel { get; set; } = string.Empty;
        // Trigger target. Pass trigger: FromProductId (null = any). Event trigger: one of EventId / EventTypeId.
        public Guid? FromProductId { get; set; }
        public string? FromProductName { get; set; }
        public Guid? EventId { get; set; }
        public Guid? EventTypeId { get; set; }
        public bool IsActive { get; set; }
        public int StepCount { get; set; }
        /// <summary>Delay on the first step when it is purchase-anchored; kept for the upgrades panel.</summary>
        public int? FirstDelayDays { get; set; }
        /// <summary>"7 days before the event starts", ready for the list.</summary>
        public string? FirstStepLabel { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int Conversions { get; set; }
        /// <summary>Distinct sends opened / clicked across every step. Opens are a ceiling (Apple Mail pre-fetch).</summary>
        public int UniqueOpens { get; set; }
        public int UniqueClicks { get; set; }
        public DateTime? EnrolFromUtc { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AutomationDetail : AutomationListItem
    {
        public bool StopOnUpgrade { get; set; }
        public bool StopWhenUsedUp { get; set; }
        /// <summary>"09:00", tenant local. Null with SendWindowEnd means any hour.</summary>
        public string? SendWindowStart { get; set; }
        public string? SendWindowEnd { get; set; }
        public List<AutomationStepItem> Steps { get; set; } = new();
    }

    public class AutomationStepItem
    {
        public Guid Id { get; set; }
        public int StepOrder { get; set; }
        /// <summary>Legacy mirror of OffsetDays for purchase-anchored steps.</summary>
        public int DelayDays { get; set; }
        /// <summary>purchase | event_start | event_end | pass_expiry | fixed_date</summary>
        public string Anchor { get; set; } = "purchase";
        /// <summary>Signed days from the anchor; negative = before.</summary>
        public int OffsetDays { get; set; }
        /// <summary>yyyy-MM-dd for a fixed_date step.</summary>
        public string? SendOn { get; set; }
        /// <summary>"3 days before the event starts".</summary>
        public string Label { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string? BodyText { get; set; }
        public string? PreviewText { get; set; }
        // Reporting: how this email is doing.
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public DateTime? LastSentAtUtc { get; set; }
        public List<AutomationSkipReasonItem> SkipReasons { get; set; } = new();
        public int UniqueOpens { get; set; }
        public int UniqueClicks { get; set; }
    }

    public class AutomationSkipReasonItem
    {
        /// <summary>skipped | failed</summary>
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class UpsertAutomationRequest
    {
        [Required, StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Defaults to season_pass_purchased for older clients.</summary>
        public string? TriggerKind { get; set; }

        /// <summary>Pass trigger: null means "any pass product".</summary>
        public Guid? FromProductId { get; set; }
        /// <summary>Event trigger: exactly one of these.</summary>
        public Guid? EventId { get; set; }
        public Guid? EventTypeId { get; set; }

        public bool StopOnUpgrade { get; set; } = true;
        public bool StopWhenUsedUp { get; set; } = true;

        /// <summary>"09:00" / "18:00", tenant local. Both null means any hour; one null is
        /// rejected, since half a window is ambiguous.</summary>
        public string? SendWindowStart { get; set; }
        public string? SendWindowEnd { get; set; }

        [MinLength(1, ErrorMessage = "An automation needs at least one email.")]
        public List<UpsertAutomationStep> Steps { get; set; } = new();
    }

    public class UpsertAutomationStep
    {
        /// <summary>The existing step's id when editing, so its send history is kept. Omit for a new email.</summary>
        public Guid? Id { get; set; }
        /// <summary>Defaults to purchase. See AutomationTriggers.Anchors.</summary>
        public string? Anchor { get; set; }
        /// <summary>Signed days from the anchor. Older clients send DelayDays instead.</summary>
        public int? OffsetDays { get; set; }
        [Range(0, 3650)] public int DelayDays { get; set; }
        /// <summary>yyyy-MM-dd, required for the fixed_date anchor.</summary>
        public string? SendOn { get; set; }
        [Required, StringLength(200, MinimumLength = 1)] public string Subject { get; set; } = string.Empty;
        [Required, MinLength(1)] public string BodyHtml { get; set; } = string.Empty;
        public string? BodyText { get; set; }
        /// <summary>Inbox snippet under the subject line; merge fields apply. Optional.</summary>
        public string? PreviewText { get; set; }
    }

    /// <summary>
    /// What arming would cost, shown BEFORE the confirm. Automations bill per email continuously,
    /// so the backlog is the sharp edge: a "30 days after purchase" automation at a track with two
    /// seasons of history matches every holder who ever bought.
    /// </summary>
    public class AutomationEstimate
    {
        /// <summary>Matches right now: what goes out on the first sweep if the back catalogue
        /// is included.</summary>
        public int BacklogCount { get; set; }
        public int BacklogChargeCents { get; set; }
        /// <summary>Matching purchases in the last 30 days, as the ongoing rate.</summary>
        public int Last30DayRate { get; set; }
        public int OngoingChargeCents { get; set; }
    }

    public class ActivateAutomationRequest
    {
        public bool IsActive { get; set; }
        /// <summary>True (the default) enrols only purchases from now on, so arming does not
        /// blast the back catalogue.</summary>
        public bool NewPurchasesOnly { get; set; } = true;
    }

    public class TestSendRequest
    {
        [Range(0, 100)] public int StepIndex { get; set; }
        [Required, EmailAddress] public string ToEmail { get; set; } = string.Empty;
    }

    public class TestSendResponse
    {
        /// <summary>Merge values came from a real purchase rather than placeholders.</summary>
        public bool UsedRealSubject { get; set; }
        /// <summary>The pass or event the sample came from, so "no upgrade price" can be told
        /// apart from a template bug.</summary>
        public string? SampleName { get; set; }
        /// <summary>When this step would actually send for that sample, tenant-local date; null
        /// when it already would have been skipped (bought after the send time).</summary>
        public string? WouldSendOn { get; set; }
        public bool WouldSkip { get; set; }
    }

    public class MergeFieldItem
    {
        public string Token { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>Everything the editor needs to offer triggers, targets, anchors, and tokens.</summary>
    public class AutomationTriggerOptionsResponse
    {
        public List<AutomationTriggerOption> Triggers { get; set; } = new();
        public List<CampaignAudienceEventOptionDto> Events { get; set; } = new();
        public List<CampaignAudienceNamedOptionDto> EventTypes { get; set; } = new();
        public List<CampaignAudienceNamedOptionDto> PassProducts { get; set; } = new();
    }

    public class AutomationTriggerOption
    {
        public string Kind { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<AutomationAnchorOption> Anchors { get; set; } = new();
        public List<MergeFieldItem> MergeFields { get; set; } = new();
    }

    public class AutomationAnchorOption
    {
        public string Value { get; set; } = string.Empty;
        /// <summary>The phrase after "N days before/after": "they buy", "the event starts".</summary>
        public string Phrase { get; set; } = string.Empty;
    }

    /// <summary>
    /// The status-aware panel on the upgrades page: is anyone actually being told about these
    /// offers? A bare "go to marketing" link would answer nothing.
    /// </summary>
    public class UpgradeAutomationStatus
    {
        public Guid? FromProductId { get; set; }
        public Guid AutomationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int? FirstDelayDays { get; set; }
        public int Sent { get; set; }
        public int Conversions { get; set; }
    }
}
