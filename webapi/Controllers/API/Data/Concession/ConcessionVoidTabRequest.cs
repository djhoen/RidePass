namespace webapi.Controllers.API.Data.Concession
{
    // Tearing up a tab is a shrinkage vector (ring rounds, pocket the cash, void), so like a refund it
    // takes a manager PIN plus a reason that lands on the sale and in the audit log.
    public class ConcessionVoidTabRequest
    {
        public string? ManagerPin { get; set; }
        public string Reason { get; set; } = "";
    }
}
