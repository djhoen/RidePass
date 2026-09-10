namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class UpdateMiscSettingsRequest
    {
        // Origins allowed to embed ANY tenant's widgets. Each must be a bare origin
        // (scheme + host, optional port), e.g. https://www.loampassmx.com. Invalid
        // entries are dropped server-side.
        public string[]? GlobalEmbedAllowedOrigins { get; set; }

        // Platform-wide outbound delivery gate. Every field is optional: omit one and the
        // stored value is left alone, so a partial client never silently re-opens a switch.
        public bool? OutboundEmailEnabled { get; set; }
        public string[]? OutboundEmailAllowlist { get; set; }   // addresses or domains; empty = everyone
        public bool? OutboundSmsEnabled { get; set; }
        public string[]? OutboundSmsAllowlist { get; set; }     // phone numbers; empty = everyone
    }
}
