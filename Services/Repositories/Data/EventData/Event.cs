namespace Services.Repositories.Data.EventData
{
    public class Event
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventTypeId { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public bool AllDay { get; set; }
        public int? Capacity { get; set; }
        public string? LocationLabel { get; set; }
        public string Status { get; set; } = "scheduled";
        // Who the event admits. Drives which entry options and waivers apply.
        // At least one is always true.
        public bool AllowsRiders { get; set; } = true;
        public bool AllowsSpectators { get; set; }
        // Per-audience waiver-required flags. When true and a waiver id is set
        // (or a tenant default exists), the corresponding buy flow forces a
        // signature before checkout.
        public bool RequiresRiderWaiver { get; set; }
        public bool RequiresSpectatorWaiver { get; set; }
        // Per-audience waiver attachments. Null = fall back to the tenant's
        // active default waiver. Spectators and racers can have different ones
        // (e.g. simpler spectator liability vs. full race-day waiver).
        public Guid? SpectatorWaiverId { get; set; }
        public Guid? RacerWaiverId { get; set; }
        public string? ImageUrl { get; set; }
        // Per-event override for the rider-facing gate-fee section headings.
        // NULL = inherit the tenant setting, which itself falls back to the
        // platform defaults ("Riding Pass" / "Spectator Pass").
        public string? RiderGateLabel { get; set; }
        public string? SpectatorGateLabel { get; set; }
        // jsonb array of {time, label} schedule rows, stored as text. '[]' = none.
        public string? ScheduleJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventWithTypeContext : Event
    {
        public string EventTypeName { get; set; } = null!;
        public string EventTypeColor { get; set; } = null!;
    }

    /// <summary>
    /// Row shape returned by IEventRepository.ListByWaiverId — the event with
    /// flags indicating which role(s) on it the waiver currently fills.
    /// </summary>
    public class EventWaiverAssociation
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public bool AsRider { get; set; }
        public bool AsSpectator { get; set; }
    }
}
