using Services.Helpers.Interfaces;
using Services.Repositories.Data.CurrencyData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class CurrencyRepository : ICurrencyRepository
    {
        private readonly IDbHelper _dbHelper;
        public CurrencyRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<List<Currency>> GetCurrencies()
        {
            var result = await _dbHelper.Query<Currency>(@"SELECT * FROM ""currency""");
            return result.ToList();
        }

        public async Task SaveCurrencies(List<Currency> currencies)
        {
            foreach (var currency in currencies)
            {
                currency.LastUpdated = DateTime.Now;
                var sql = $@"INSERT INTO ""currency"" (""name"", ""exchangeRate"", ""lastUpdated"", ""symbol"")
                                VALUES (@name, @exchangeRate, @lastUpdated, @symbol)
                                ON CONFLICT (""name"")
                                DO UPDATE SET
                                    ""exchangeRate"" = @exchangeRate,
                                    ""lastUpdated"" = @lastUpdated";

                await _dbHelper.Execute(sql, currency);
            }
        }
    }
}
