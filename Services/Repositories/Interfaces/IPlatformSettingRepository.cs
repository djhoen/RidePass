namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Global (non-tenant) key/value platform settings. Backed by platform_setting.
    /// </summary>
    public interface IPlatformSettingRepository
    {
        Task<string?> Get(string key);
        Task Set(string key, string value);
    }
}
