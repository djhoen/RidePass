using Services.Helpers.Interfaces;
using Services.Repositories.Data.SiteData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class SiteRepository : ISiteRepository
    {
        private readonly IDbHelper _dbHelper;
        public SiteRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<int> CreateBanner(Banner banner)
        {
            var sql = @"INSERT INTO ""site.banner"" (""isActive"", ""text"", ""actionUrl"", ""class"", ""name"")
                        VALUES (@isActive, @text, @actionUrl, @class, @name)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var id = await _dbHelper.Query<int>(sql, banner);

            return id.FirstOrDefault();
        }

        public async Task<Banner> GetBanner()
        {
            var sql = @"SELECT * FROM ""site.banner"" WHERE ""isActive"" IS TRUE";
            var result = await _dbHelper.Query<Banner>(sql);
            return result.FirstOrDefault();
        }

        public async Task<List<Banner>> GetBanners()
        {
            var sql = @"SELECT * FROM ""site.banner""";
            var result = await _dbHelper.Query<Banner>(sql);
            return result.ToList();
        }

        public async Task<Setting> GetSetting(int id)
        {
            var sql = @"SELECT * FROM ""setting"" WHERE ""id"" = @id";
            var result = await _dbHelper.Query<Setting>(sql, new { id });
            return result.FirstOrDefault();
        }

        public async Task<Setting> GetSettingByName(string name)
        {
            var sql = @"SELECT * FROM ""setting"" WHERE ""name"" = @name";
            var result = await _dbHelper.Query<Setting>(sql, new { name });
            return result.FirstOrDefault();
        }

        public async Task<List<Setting>> GetSettingsByCateogry(string category)
        {
            var sql = @"SELECT * FROM ""setting"" WHERE ""category"" = @category";
            var result = await _dbHelper.Query<Setting>(sql, new { category });
            return result.ToList();
        }

        public async Task UpdateBanner(Banner banner)
        {
            if (banner.IsActive)
            {
                var updateSql = @"UPDATE ""site.banner"" SET ""isActive"" = false";

                await _dbHelper.Execute(updateSql);
            }

            var sql = @"UPDATE ""site.banner""
                        SET ""isActive"" = @isActive,
                            ""text"" = @text,
                            ""actionUrl"" = @actionUrl,
                            ""name"" = @name,
                            ""class"" = @class
                        WHERE ""id"" = @id";

            await _dbHelper.Execute(sql, banner);
        }

        public async Task<int> SaveSetting(Setting setting)
        {
            var sql = string.Empty;
            if (setting.Id != 0)
            {
                sql = @"UPDATE ""setting"" SET ""value"" = @value WHERE ""id"" = @id";
                await _dbHelper.Execute(sql, setting);
                return setting.Id;
            }
            else
            {
                sql = @"INSERT INTO ""setting"" (""category"", ""name"", ""value"")
                        VALUES (@category, @name, @value)
                        RETURNING ""id""";
                var result = await _dbHelper.Query<int>(sql, setting);
                return result.FirstOrDefault();
            }
        }
    }
}
