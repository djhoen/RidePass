namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>QC sign-off on a work order. A staff user id records the reviewer (server stamps the
    /// time); null clears the check.</summary>
    public class SetWorkOrderQcRequest
    {
        public System.Guid? CheckedByUserId { get; set; }
    }
}
