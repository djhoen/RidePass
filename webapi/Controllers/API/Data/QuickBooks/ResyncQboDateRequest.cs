namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>Re-run one business date by hand after fixing whatever made it fail.</summary>
    public class ResyncQboDateRequest
    {
        public DateOnly BusinessDate { get; set; }
    }
}
