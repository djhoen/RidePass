using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class IdRepository : IIdRepository
    {
        private readonly IDbHelper _dbHelper;
        public IdRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<bool> IdAvailable(string id, string tableName)
        {
            if (tableName.Contains(" ") || tableName.Contains(";"))
            {
                return false;
            }

            var sql = $@"SELECT * FROM ""{tableName}"" WHERE ""id"" = @id";
            var result = await _dbHelper.Query<string>(sql, new { id });

            if (result != null && !string.IsNullOrEmpty(result.FirstOrDefault()))
            {
                return false;
            }
            return true;
        }

        public async Task<string> GetUniqueId(string table, int idLength)
        {
            var id = IdHelper.GenerateId(idLength);

            if (await IdAvailable(id, table))
            {
                return id;
            }
            else return await GetUniqueId(table, idLength);
        }
    }
}
