using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Event
{
    public class UpsertEventRequest
    {
        [Required]
        public Guid EventTypeId { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required]
        public DateTime StartsAtUtc { get; set; }

        [Required]
        public DateTime EndsAtUtc { get; set; }

        public bool AllDay { get; set; }

        [Range(1, int.MaxValue)]
        public int? Capacity { get; set; }

        [MaxLength(120)]
        public string? LocationLabel { get; set; }

        [RegularExpression("^(scheduled|cancelled)$")]
        public string Status { get; set; } = "scheduled";

        // Who the event admits. At least one must be true (validated server-side).
        public bool AllowsRiders { get; set; } = true;
        public bool AllowsSpectators { get; set; } = false;

        public bool RequiresRiderWaiver { get; set; } = true;
        public bool RequiresSpectatorWaiver { get; set; } = false;

        // Per-audience waiver attachments. Either may be null to fall back to
        // the tenant's active default. Spectators and racers can have different
        // waivers (e.g. light spectator liability vs. race-day waiver).
        public Guid? SpectatorWaiverId { get; set; }
        public Guid? RacerWaiverId { get; set; }

        // Per-event cover image URL (optional). When null, falls back to the event
        // type's default image, then to a flat color card on the public home page.
        public string? ImageUrl { get; set; }

        // Per-event override for the rider-facing gate-fee section headings.
        // Blank/null = inherit the tenant setting ("Checkout headings"), which
        // falls back to "Riding Pass" / "Spectator Pass".
        [MaxLength(40)]
        public string? RiderGateLabel { get; set; }

        [MaxLength(40)]
        public string? SpectatorGateLabel { get; set; }

        // Allow-list of pass product ids that may be redeemed at this event.
        // Empty / null → no pass reservation option for this event.
        public List<Guid>? EligiblePassProductIds { get; set; }

        // Per-event add-ons (camping/parking/pit-vehicle/custom). Each entry binds
        // an extra product to this event with optional per-event inventory cap.
        // Empty / null → no add-ons offered at this event.
        public List<EligibleExtraInput>? EligibleExtras { get; set; }

        // Ordered schedule rows ({time, label}). Null / empty = no schedule.
        public List<ScheduleItem>? Schedule { get; set; }
    }

    public class EligibleExtraInput
    {
        public Guid ProductId { get; set; }
        public int? Inventory { get; set; }
    }
}
