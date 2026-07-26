using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>Row in the Automations list.</summary>
    public class AutomationListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TriggerKind { get; set; } = string.Empty;
        public Guid? FromProductId { get; set; }
        public string? FromProductName { get; set; }
        public bool IsActive { get; set; }
        public int StepCount { get; set; }
        /// <summary>Delay on the first step, which is what the list needs to say
        /// "30 days after purchase" without loading every step.</summary>
        public int? FirstDelayDays { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int Conversions { get; set; }
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
        public int DelayDays { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string? BodyText { get; set; }
    }

    public class UpsertAutomationRequest
    {
        [Required, StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Null means "any pass product".</summary>
        public Guid? FromProductId { get; set; }

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
        [Range(0, 3650)] public int DelayDays { get; set; }
        [Required, StringLength(200, MinimumLength = 1)] public string Subject { get; set; } = string.Empty;
        [Required, MinLength(1)] public string BodyHtml { get; set; } = string.Empty;
        public string? BodyText { get; set; }
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
        /// <summary>Passes sold in the last 30 days, as the ongoing rate.</summary>
        public int Last30DayRate { get; set; }
        public int OngoingChargeCents { get; set; }
    }

    public class ActivateAutomationRequest
    {
        public bool IsActive { get; set; }
        /// <summary>True (the default) enrols only passes bought from now on, so arming does not
        /// blast the back catalogue.</summary>
        public bool NewPurchasesOnly { get; set; } = true;
    }

    public class TestSendRequest
    {
        [Range(0, 100)] public int StepIndex { get; set; }
        [Required, EmailAddress] public string ToEmail { get; set; } = string.Empty;
    }

    public class MergeFieldItem
    {
        public string Token { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
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
