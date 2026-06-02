namespace webapi.Controllers.API.Data.SmsSettings
{
    public class SmsStatusResponse
    {
        // True if the tenant has flipped the feature on. False if either never
        // provisioned or explicitly paused (credentials retained either way).
        public bool Enabled { get; set; }

        // True when the tenant has bought a number and credentials are persisted.
        // The UI uses this to decide whether to show "Provision" vs "Enable/Disable".
        public bool HasProvisionedNumber { get; set; }

        // E.164 number the tenant's customers see. Null when not yet provisioned.
        public string? PhoneNumber { get; set; }

        public DateTime? EnabledAtUtc { get; set; }

        // True when the global master Twilio credentials are present on the
        // server. UI surfaces a clear error when false ("contact RidePass support")
        // so tenants don't blame themselves for misconfiguration.
        public bool MasterConfigured { get; set; }

        // Per-segment outbound rate the tenant pays RidePass, in cents.
        // Echoed here so the UI doesn't need a separate pricing endpoint.
        public int OutboundPerSegmentCents { get; set; }
    }
}
