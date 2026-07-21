namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Manually set a work order's accumulated actual labor minutes.</summary>
    public class SetActualMinutesRequest
    {
        public int Minutes { get; set; }
    }
}
