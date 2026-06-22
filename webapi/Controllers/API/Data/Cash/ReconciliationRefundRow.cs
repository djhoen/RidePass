namespace webapi.Controllers.API.Data.Cash
{
    // Refund volume per worker over the event's reconciliation window, split by tender.
    // Refund counts are a primary fraud signal, so the manager report surfaces them per worker.
    public class ReconciliationRefundRow
    {
        public Guid WorkerUserId { get; set; }
        public string? WorkerName { get; set; }
        public int CashRefundCount { get; set; }
        public long CashRefundCents { get; set; }
        public int CardRefundCount { get; set; }
        public long CardRefundCents { get; set; }
    }
}
