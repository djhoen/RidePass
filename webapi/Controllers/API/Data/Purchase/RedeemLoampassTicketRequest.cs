namespace webapi.Controllers.API.Data.Purchase
{
    /// <summary>Redeem one Loam Pass credit to cover a rider's entry to an event (a race_entry tier).</summary>
    public class RedeemLoampassTicketRequest
    {
        public Guid TierId { get; set; }
    }
}
