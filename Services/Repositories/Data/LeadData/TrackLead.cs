namespace Services.Repositories.Data.LeadData
{
    /// <summary>
    /// A prospective track operator who submitted the public "For Tracks" lead
    /// form on the apex marketing page. Platform-level (no tenant_id) by design:
    /// the submitter is not yet a tenant.
    /// </summary>
    public class TrackLead
    {
        public Guid Id { get; set; }
        public string ContactName { get; set; } = null!;
        public string TrackName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string? Message { get; set; }
        public string Status { get; set; } = "new";   // 'new' | 'contacted' | 'closed'
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
