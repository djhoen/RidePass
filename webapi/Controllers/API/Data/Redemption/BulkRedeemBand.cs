namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>
    /// "Admit and band" on the order screen (Script0313): one band for one person in the order. Their tickets
    /// being admitted (any of PurchaseIds that are in the request's items) are all tied to that band.
    /// </summary>
    public class BulkRedeemBand
    {
        /// <summary>The person's tickets on this order (the people panel's attendee).</summary>
        public List<Guid> PurchaseIds { get; set; } = new();
        /// <summary>The scanned QR payload or typed band number.</summary>
        public string Code { get; set; } = string.Empty;
    }
}
