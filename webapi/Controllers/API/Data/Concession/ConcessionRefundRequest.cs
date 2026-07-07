namespace webapi.Controllers.API.Data.Concession
{
    // A refund reverses money and is a classic shrinkage vector (ring a cash sale, pocket it, refund
    // it), so it carries a manager PIN just like a comp/discount does. The server re-verifies the PIN
    // authoritatively and records which manager authorized the reversal.
    public class ConcessionRefundRequest
    {
        public string? ManagerPin { get; set; }
    }
}
