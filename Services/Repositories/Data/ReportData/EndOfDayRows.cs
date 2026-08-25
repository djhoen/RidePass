namespace Services.Repositories.Data.ReportData
{
    /// <summary>
    /// One aggregate cell of v_accounting_entries for a single tenant-local business date:
    /// every row that shares an (entry_kind, source_kind, revenue_key_override, payment_method)
    /// quadruple, summed.
    ///
    /// Deliberately NOT bucketed into QuickBooks account slots here. The bucketing rule lives in
    /// Services.Accounting.QboAccountKeys.EffectiveRevenueKey, which is the same function the
    /// journal entry the sync posts is built from, so the End of Day report and the journal entry
    /// cannot drift apart. SQL groups; C# labels.
    /// </summary>
    public class AccountingBucketRow
    {
        /// <summary>sale | refund | dispute_loss | dispute_fee | sms_charge | email_charge | deposit_collected | deposit_released | gift_card_sold</summary>
        public string EntryKind { get; set; } = null!;
        /// <summary>Null on platform-charge rows (sms/email) that have no sale behind them.</summary>
        public string? SourceKind { get; set; }
        /// <summary>
        /// The event type's own QuickBooks revenue slot, when it names one (Script0274). Part of
        /// the GROUP BY, not just carried along: it is what keeps a track's Training Center
        /// (lessons, camps, clinics) as its own line instead of folded into the gate. Null for
        /// every row with no event behind it.
        /// </summary>
        public string? RevenueKeyOverride { get; set; }
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
        /// <summary>
        /// How many of this bucket's rows actually drew on a gift card. NOT derivable from
        /// GiftCardAppliedCents != 0: that is the bucket's SUM, so testing it and then taking
        /// EntryCount credits the gift-card tender with every row in the bucket. A day of 178 card
        /// event-ticket sales where one rider paid $90 on a gift card reported "178" that way.
        /// </summary>
        public int GiftCardEntryCount { get; set; }
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
    /// Earned revenue in a range, grouped only as finely as it has to be to resolve a QuickBooks
    /// revenue slot: the source kind, the event type's override, and whether it was a sale or a
    /// refund. The caller turns each group into a slot and then a business unit.
    ///
    /// Gross is tax- and tip-inclusive and negative on refunds, exactly as the ledger stores it, so
    /// summing a department's rows nets refunds off without a special case, and net revenue is
    /// gross minus tax minus tip, the same identity JournalEntryBuilder.AccrueSale uses.
    /// </summary>
    public class RevenueBucketRow
    {
        public string? SourceKind { get; set; }
        /// <summary>tenant_event_type.revenue_key for the event behind the row, when it names one.</summary>
        public string? RevenueKeyOverride { get; set; }
        /// <summary>sale | refund.</summary>
        public string EntryKind { get; set; } = null!;
        public long GrossCents { get; set; }
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        public int EntryCount { get; set; }
    }

    /// <summary>
    /// Sales tax collected in a range, grouped finely enough to be rolled up two ways in C#:
    /// by tenant-local day and by QuickBooks revenue category. One query, two tables.
    /// </summary>
    public class SalesTaxBucketRow
    {
        public DateOnly BusinessDate { get; set; }
        public string? SourceKind { get; set; }
        /// <summary>
        /// The event type's own QuickBooks revenue slot, when it names one (Script0274). Grouped on
        /// so the by-category table splits a training department out of gate revenue exactly the
        /// way the End of Day report and the posted journal entry do.
        /// </summary>
        public string? RevenueKeyOverride { get; set; }
        /// <summary>sale | refund. Refund rows carry negative tax, so a plain SUM nets correctly.</summary>
        public string EntryKind { get; set; } = null!;
        public long TaxCents { get; set; }
        public long GrossCents { get; set; }
        public int EntryCount { get; set; }
        /// <summary>
        /// Rows in this bucket that actually carried tax, and their gross. Same trap as
        /// AccountingBucketRow.GiftCardEntryCount: TaxCents is the bucket's SUM, so a bucket
        /// holding one taxed sale and forty untaxed ones would otherwise report forty-one taxed
        /// sales and count every untaxed dollar as taxable.
        /// </summary>
        public int TaxedEntryCount { get; set; }
        public long TaxedGrossCents { get; set; }
    }

    /// <summary>
    /// Gross sale revenue for one tenant-local business date and one revenue slot, for the Sales
    /// Summary chart's per-profit-center series.
    ///
    /// Deliberately the SAME population the chart's total line is drawn from
    /// (ReportsRepository.GetDailyRevenue: entry_kind = 'sale', SUM(gross_cents), bucketed on the
    /// tenant's local day), so the center series add up to the total line exactly rather than
    /// approximately. Net-of-tax figures would be more "correct" accounting and would NOT sum to
    /// the blue line, which is worse: a chart whose parts visibly miss its whole is a bug report
    /// waiting to happen.
    /// </summary>
    public class DailyRevenueBucketRow
    {
        /// <summary>The tenant-local business date, yyyy-MM-dd.</summary>
        public string Date { get; set; } = null!;
        public string? SourceKind { get; set; }
        public string? RevenueKeyOverride { get; set; }
        public long GrossCents { get; set; }
    }
}
