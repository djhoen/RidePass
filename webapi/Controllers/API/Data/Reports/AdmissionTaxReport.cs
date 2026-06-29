namespace webapi.Controllers.API.Data.Reports
{
    // Admission/amusement tax collected on event tickets in a date range, for the tenant's
    // remittance. NetTaxCents = collected minus tax refunded on cancelled/refunded tickets.
    public class AdmissionTaxReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public long TaxCollectedCents { get; set; }
        public long RefundedTaxCents { get; set; }
        public long NetTaxCents { get; set; }
        public long TaxableSalesCents { get; set; }
        public int TaxedTicketCount { get; set; }
        public int CurrentRateBps { get; set; }
        public string? JurisdictionLabel { get; set; }
    }
}
