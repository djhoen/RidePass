namespace webapi.Controllers.API.Data.Cash
{
    public class ConfirmTurnInRequest
    {
        // The manager's own count of the cash received from the worker.
        public int ManagerCountedCents { get; set; }
        public string? Note { get; set; }
    }
}
