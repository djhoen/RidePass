using Services.Helpers.Interfaces;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class PlatformSettingRepository : IPlatformSettingRepository
    {
        private readonly IDbHelper _db;

        public PlatformSettingRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<string?> Get(string key)
        {
            const string sql = "SELECT value FROM platform_setting WHERE key = @key";
            var rows = await _db.Query<string>(sql, new { key });
            return rows.FirstOrDefault();
        }

        public async Task Set(string key, string value)
        {
            const string sql = @"
                INSERT INTO platform_setting (key, value, updated_at)
                VALUES (@key, @value, now())
                ON CONFLICT (key) DO UPDATE
                    SET value = EXCLUDED.value, updated_at = now()";
            await _db.Execute(sql, new { key, value });
        }
    }
}
