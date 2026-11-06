using Services.Repositories.Data.BikeShopData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Reads behind the bike shop "Today" sheet. The repository only fetches; ShopDayMath turns
    /// what it fetched into groups, phases and counts.
    /// </summary>
    public interface IShopDayRepository
    {
        /// <summary>
        /// Every rental that belongs to the business day [dayStartUtc, dayEndUtc), with its lines,
        /// package links and the events it hangs off. A rental belongs to the day when its window
        /// overlaps it, when it was returned during it, or when it was booked during it for a
        /// later start (the day's reservations). failed and abandoned are never returned;
        /// cancelled only when <paramref name="includeCancelled"/> asks for it.
        /// </summary>
        Task<ShopDayRaw> GetDay(Guid tenantId, DateTime dayStartUtc, DateTime dayEndUtc, bool includeCancelled);
    }
}
