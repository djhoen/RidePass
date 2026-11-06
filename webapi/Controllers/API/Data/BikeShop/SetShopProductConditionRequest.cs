namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Mark a product new or used (the storefront badge and filter).</summary>
    public class SetShopProductConditionRequest
    {
        /// <summary>new | used</summary>
        public string? Condition { get; set; }
    }
}
