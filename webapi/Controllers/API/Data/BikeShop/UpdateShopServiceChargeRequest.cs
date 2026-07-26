namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Who funds the platform service charge on a bike shop sale.
    ///
    /// Only one knob, deliberately: the RATE is the tenant's ServiceChargeBps, the same one events
    /// and rentals use, and it is not settable per surface. This decides funding, not amount.
    /// </summary>
    public class UpdateShopServiceChargeRequest
    {
        /// <summary>
        /// 10000 = the customer pays the whole charge as a line on their total.
        /// 0 = the shop absorbs it out of their own margin and the customer sees no fee.
        /// Either way the charge is owed and booked to the ledger.
        /// </summary>
        public int BuyerPaidBps { get; set; }
    }
}
