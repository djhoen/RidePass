namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>
    /// Gate: record that staff checked a rider's photo ID and birthdate, from the scanned order.
    /// </summary>
    public class VerifyOrderRiderIdRequest
    {
        /// <summary>A rider-side ticket in the scanned order (event_ticket_purchase.id).</summary>
        public Guid PurchaseId { get; set; }
        /// <summary>The date of birth on the ID. Null falls back to the birthdate the rider gave at
        /// registration (the ID simply confirmed it); rejected when neither exists.</summary>
        public DateTime? VerifiedDob { get; set; }
    }
}
