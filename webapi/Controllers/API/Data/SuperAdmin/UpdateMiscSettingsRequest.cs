namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class UpdateMiscSettingsRequest
    {
        // Origins allowed to embed ANY tenant's widgets. Each must be a bare origin
        // (scheme + host, optional port), e.g. https://www.loampassmx.com. Invalid
        // entries are dropped server-side.
        public string[]? GlobalEmbedAllowedOrigins { get; set; }
    }
}
