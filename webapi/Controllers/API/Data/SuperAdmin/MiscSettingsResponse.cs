namespace webapi.Controllers.API.Data.SuperAdmin
{
    /// <summary>Odds-and-ends global platform settings (super-admin Misc settings page).</summary>
    public class MiscSettingsResponse
    {
        // Origins allowed to embed ANY tenant's widgets (our first-party properties).
        public string[] GlobalEmbedAllowedOrigins { get; set; } = System.Array.Empty<string>();
    }
}
