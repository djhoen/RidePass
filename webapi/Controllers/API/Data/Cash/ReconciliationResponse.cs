namespace webapi.Controllers.API.Data.Cash
{
    public class ReconciliationResponse
    {
        public List<ReconciliationRow> Rows { get; set; } = new();
        public List<ReconciliationRefundRow> RefundsByWorker { get; set; } = new();
    }
}
