using Services.Repositories.Data.CurrencyData;

namespace Services.Repositories.Interfaces
{
    public interface ICurrencyRepository
    {
        Task<List<Currency>> GetCurrencies();
        Task SaveCurrencies(List<Currency> currencies);
    }
}
