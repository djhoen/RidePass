namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>
    /// Gate: send a waiver signing link to a parent / guardian for one ticket or season pass, so a
    /// waiver block (a minor's self-signed waiver, or no waiver) can be fixed on the spot.
    /// </summary>
    public class GateWaiverSignRequestRequest
    {
        /// <summary>"ticket" or "season_pass".</summary>
        public string Kind { get; set; } = "ticket";
        /// <summary>The ticket id (event_ticket_purchase.id) or pass id (season_pass_purchase.id).</summary>
        public Guid PurchaseId { get; set; }
        /// <summary>Where the link goes: the parent / guardian's email.</summary>
        public string Email { get; set; } = string.Empty;
    }
}
