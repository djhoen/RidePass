using Services.Helpers.Interfaces;
using Services.Repositories.Data.FaqData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class FaqRepository : IFaqRepository
    {
        private readonly IDbHelper _dbHelper;
        public FaqRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task CreateFaq(Faq faq)
        {
            var sql = @"INSERT INTO ""faq"" (""question"", ""answer"", ""faqTypeId"")
                        VALUES (@question, @answer, @faqTypeId)";

            await _dbHelper.Execute(sql, faq);
        }

        public async Task DeleteFaq(int id)
        {
            var sql = @"DELETE FROM ""faq"" WHERE ""id"" = @id";

            await _dbHelper.Query<int>(sql, new { id });
        }

        public async Task<List<Faq>> GetFaqs()
        {
            var sql = @"SELECT * FROM ""faq"" ORDER BY ""id"" ASC";
            var result = await _dbHelper.Query<Faq>(sql);
            return result.ToList();
        }

        public async Task UpdateFaq(Faq faq)
        {
            var sql = @"UPDATE ""faq""
                        SET ""question"" = @question,
                            ""answer"" = @answer,
                            ""faqTypeId"" = @faqTypeId
                        WHERE ""id"" = @id";

            await _dbHelper.Execute(sql, faq);
        }
    }
}
