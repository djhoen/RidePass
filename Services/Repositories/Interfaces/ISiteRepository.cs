using Services.Repositories.Data.SiteData;

namespace Services.Repositories.Interfaces
{
    public interface ISiteRepository
    {
        Task<int> CreateBanner(Banner banner);
        Task<Banner> GetBanner();
        Task<List<Banner>> GetBanners();
        Task<Setting> GetSetting(int id);
        Task<Setting> GetSettingByName(string name);
        Task<List<Setting>> GetSettingsByCateogry(string category);
        Task UpdateBanner(Banner banner);
        Task<int> SaveSetting(Setting setting);
    }
}
