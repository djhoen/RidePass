namespace webapi.Controllers.API.Data.Cash
{
    public class SubmitTurnInRequest
    {
        // Identifies which open session to turn in (matched by worker + event).
        public Guid? EventId { get; set; }
        // Blind count: the worker enters their counted cash without seeing the expected total.
        public int WorkerCountedCents { get; set; }
        public string? Note { get; set; }
    }
}
