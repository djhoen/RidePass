namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>One photo's new position in a product gallery reorder.</summary>
    public class ShopImageReorderItem
    {
        public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
