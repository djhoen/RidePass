namespace webapi.Controllers.API.Data.Activity
{
    /// <summary>One recorded staff action, as the activity screen shows it. A projection of
    /// audit_log rather than the entity, so the tenant view never accidentally carries a column
    /// added for platform use.</summary>
    public class StaffActivityItem
    {
        public Guid Id { get; set; }
        public Guid? ActorUserId { get; set; }
        /// <summary>Snapshotted at write time, so it survives the staff account being deleted.</summary>
        public string? ActorEmail { get; set; }
        public string? ActorRole { get; set; }
        /// <summary>Dotted machine name, e.g. "purchase.refund". Drives filtering and grouping.</summary>
        public string Action { get; set; } = null!;
        /// <summary>Human-readable one-liner written at the time of the action.</summary>
        public string Summary { get; set; } = null!;
        public string? TargetKind { get; set; }
        public Guid? TargetId { get; set; }
        /// <summary>Where the action came from. Meaningful only for entries written after the
        /// forwarded-headers fix; older rows all read 127.0.0.1 (the proxy).</summary>
        public string? IpAddress { get; set; }
        /// <summary>Raw JSON detail for the action (amounts, tender, destination). Passed through
        /// as text so the screen can render it without the API pinning a shape per action.</summary>
        public string? Metadata { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
