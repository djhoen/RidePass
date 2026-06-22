namespace webapi.Controllers.API.Data.Redemption
{
    // A batch of admissions an operator device made offline, submitted on reconnect.
    public class BatchAdmitRequest
    {
        public List<BatchAdmitItem> Items { get; set; } = new();
    }
}
