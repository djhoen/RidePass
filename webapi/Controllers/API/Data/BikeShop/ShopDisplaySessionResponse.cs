namespace webapi.Controllers.API.Data.BikeShop
{
    public class ShopDisplaySessionResponse
    {
        public Guid Id { get; set; }
        public string PairCode { get; set; } = null!;
        public string? StateJson { get; set; }
        public string? ResponseJson { get; set; }
    }
}
