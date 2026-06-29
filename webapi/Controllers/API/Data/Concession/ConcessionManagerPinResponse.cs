namespace webapi.Controllers.API.Data.Concession
{
    // Result of verifying a manager PIN: who approved it, for the POS to display. Returned only on a
    // successful match; a bad PIN is a 400 so the digits never leak which manager (if any) they hit.
    public class ConcessionManagerPinResponse
    {
        public Guid ManagerUserId { get; set; }
        public string ManagerName { get; set; } = null!;
    }
}
