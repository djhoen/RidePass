namespace webapi.Controllers.API.Data.BikeShop
{
    // Place a work-order part line on a supplier PO (the special-order flow). Either point at
    // an existing open PO or have a new one created (optionally against a supplier).
    public class OrderShopWoPartRequest
    {
        public Guid? PoId { get; set; }
        public Guid? SupplierId { get; set; }
        // Cost for the new PO line; defaults to the variant's last known cost.
        public int? UnitCostCents { get; set; }
    }
}
