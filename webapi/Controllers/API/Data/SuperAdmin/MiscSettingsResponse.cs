namespace webapi.Controllers.API.Data.SuperAdmin
{
    /// <summary>Odds-and-ends global platform settings (super-admin Misc settings page).</summary>
    public class MiscSettingsResponse
    {
        // Origins allowed to embed ANY tenant's widgets (our first-party properties).
        public string[] GlobalEmbedAllowedOrigins { get; set; } = System.Array.Empty<string>();

        // Platform-wide outbound delivery gate (Services.Delivery.OutboundDeliveryGate).
        // Enforced at the last hop for every email / SMS on this environment.
        public bool OutboundEmailEnabled { get; set; } = true;
        public string[] OutboundEmailAllowlist { get; set; } = System.Array.Empty<string>();   // empty = everyone
        public bool OutboundSmsEnabled { get; set; } = true;
        public string[] OutboundSmsAllowlist { get; set; } = System.Array.Empty<string>();     // empty = everyone

        // Read-only context so the admin can see the whole picture on one card: whether the
        // relays are configured at all (env), and which environment they are looking at.
        public bool EmailConfigured { get; set; }
        public bool SmsConfigured { get; set; }
        public string EnvironmentName { get; set; } = string.Empty;
    }
}
