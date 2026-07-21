namespace webapi.Controllers.API.Data.Tenant
{
    /// <summary>Default shop labor rate in cents per hour. Null clears the rate, so labor lines fall
    /// back to a typed price.</summary>
    public class UpdateShopLaborRateRequest
    {
        /// <summary>Cents per hour (9000 = $90/hr). Null = no rate set.</summary>
        public int? RateCents { get; set; }
    }
}
