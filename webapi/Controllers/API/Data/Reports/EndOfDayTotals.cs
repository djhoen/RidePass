namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>The day's close in one object. Every figure is derived from v_accounting_entries.</summary>
    public class EndOfDayTotals
    {
        /// <summary>Sale rows only, tax and tips included.</summary>
        public long GrossSalesCents { get; set; }
        /// <summary>Refund rows. Negative.</summary>
        public long RefundsCents { get; set; }
        /// <summary>GrossSales + Refunds.</summary>
        public long NetSalesCents { get; set; }
        public long TaxCents { get; set; }
        public long TipsCents { get; set; }
        /// <summary>NetSales minus tax and tips: what the day actually earned.</summary>
        public long NetRevenueCents { get; set; }

        /// <summary>Face value of gift cards SOLD today. Not revenue, a liability the track now owes.</summary>
        public long GiftCardsSoldCents { get; set; }
        /// <summary>How much of today's sales was paid for by drawing down a gift card sold earlier.</summary>
        public long GiftCardsRedeemedCents { get; set; }
        public long DepositsCollectedCents { get; set; }
        public long DepositsReleasedCents { get; set; }
        /// <summary>Chargebacks lost. Carried with the ledger's own sign, which is negative.</summary>
        public long DisputeLossCents { get; set; }
        public long DisputeFeeCents { get; set; }
        /// <summary>SMS and email campaign charges billed by RidePass, reported POSITIVE as a cost.</summary>
        public long PlatformChargesCents { get; set; }

        public long StripeFeesCents { get; set; }
        public long RidepassFeesCents { get; set; }
        /// <summary>Sale + refund rows: gross minus processing fee minus the RidePass cut.</summary>
        public long NetToTenantCents { get; set; }

        public int TransactionCount { get; set; }
        public int RefundCount { get; set; }
    }
}
