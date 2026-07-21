namespace webapi.Controllers.API.Data.Tenant
{
    /// <summary>
    /// Rentals -> Settings. RiderPaidBps is who funds the service fee (10000 = renter pays all,
    /// 0 = the track absorbs it); the fee RATE is tenant.service_charge_bps and is not set here.
    /// TaxBps is the rental sales tax rate: null means "not configured" (the UI warns), while 0
    /// means deliberately untaxed. The refundable deposit is never taxed either way.
    /// </summary>
    public class UpdateRentalSettingsRequest
    {
        public int RiderPaidBps { get; set; }
        public int? TaxBps { get; set; }
        public bool ServiceChargeTaxable { get; set; } = true;
    }
}
