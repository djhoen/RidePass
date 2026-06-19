namespace Services.Repositories.Data.PlatformData
{
    /// <summary>
    /// A single global (non-tenant) platform setting. Backed by the platform_setting
    /// key/value table; values are plain text (callers parse as needed).
    /// </summary>
    public class PlatformSetting
    {
        public string Key { get; set; } = null!;
        public string Value { get; set; } = null!;
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>Well-known platform_setting keys.</summary>
    public static class PlatformSettingKeys
    {
        // Newline-separated origins allowed to embed ANY tenant's widgets (first-party).
        public const string EmbedGlobalAllowedOrigins = "embed_global_allowed_origins";
    }
}
