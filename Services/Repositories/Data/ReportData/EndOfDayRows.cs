namespace Services.Repositories.Data.ReportData
{
    /// <summary>
    /// One aggregate cell of v_accounting_entries for a single tenant-local business date:
    /// every row that shares an (entry_kind, source_kind, payment_method) triple, summed.
    ///
    /// Deliberately NOT bucketed into QuickBooks account slots here. The bucketing rule lives in
    /// Services.Accounting.QboAccountKeys.RevenueForSourceKind, which is the same function the
    /// journal entry the sync posts is built from, so the End of Day report and the journal entry
    /// cannot drift apart. SQL groups; C# labels.
    /// </summary>
    public class AccountingBucketRow
    {
        /// <summary>sale | refund | dispute_loss | dispute_fee | sms_charge | email_charge | deposit_collected | deposit_released | gift_card_sold</summary>
        public string EntryKind { get; set; } = null!;
        /// <summary>Null on platform-charge rows (sms/email) that have no sale behind them.</summary>
        public string? SourceKind { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public int EntryCount { get; set; }
        /// <summary>Negative on refunds. Tax- and tip-inclusive.</summary>
        public long GrossCents { get; set; }
        public long StripeFeeCents { get; set; }
        public long RidepassCutCents { get; set; }
        public long NetToTenantCents { get; set; }
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        public long GiftCardAppliedCents { get; set; }
    }

    /// <summary>Who rang it up. Only ledger rows that carry a sold_by_user_id appear.</summary>
    public class EndOfDayStaffRow
    {
        public Guid UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int SaleCount { get; set; }
        public int RefundCount { get; set; }
        public long GrossCents { get; set; }
        public long CashCents { get; set; }
    }

    /// <summary>
    /// A cash_session opened on the business date. cash_session carries no counted or expected
    /// column of its own (the count lives on cash_turn_in, and "expected" is derived by the
    /// reconciliation report, never persisted), so the End of Day report shows the float and the
    /// session window here and the counts on the turn-in rows.
    /// </summary>
    public class EndOfDayCashSessionRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? UserName { get; set; }
        public string? EventTitle { get; set; }
        public string? DeviceId { get; set; }
        public long OpeningFloatCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    /// <summary>A blind-count hand-off submitted on the business date, with the manager's confirmation if it happened.</summary>
    public class EndOfDayTurnInRow
    {
        public Guid Id { get; set; }
        public string? WorkerName { get; set; }
        public string? ManagerName { get; set; }
        public long? ExpectedCents { get; set; }
        public long WorkerCountedCents { get; set; }
        public long? ManagerCountedCents { get; set; }
        public long? VarianceCents { get; set; }
        public string Status { get; set; } = null!;
        public string? Note { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
    }

    /// <summary>
    /// Sales tax collected in a range, grouped finely enough to be rolled up two ways in C#:
    /// by tenant-local day and by QuickBooks revenue category. One query, two tables.
    /// </summary>
    public class SalesTaxBucketRow
    {
        public DateOnly BusinessDate { get; set; }
        public string? SourceKind { get; set; }
        /// <summary>sale | refund. Refund rows carry negative tax, so a plain SUM nets correctly.</summary>
        public string EntryKind { get; set; } = null!;
        public long TaxCents { get; set; }
        public long GrossCents { get; set; }
        public int EntryCount { get; set; }
    }
}
