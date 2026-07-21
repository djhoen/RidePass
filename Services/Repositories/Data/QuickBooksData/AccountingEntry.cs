namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// One row of v_accounting_entries (Script0175): a ledger entry with the tax / tip / gift-card
    /// breakout the raw ledger can't give, plus rental-deposit rows the ledger never carries.
    ///
    /// Every derived amount is already prorated by the view against the source row, so a refund's
    /// TaxCents is negative and a partial refund's is a fraction. Consumers can treat sales and
    /// refunds identically, see JournalEntryBuilder.
    /// </summary>
    public class AccountingEntry
    {
        public Guid TenantId { get; set; }
        /// <summary>Null for rental-deposit rows, which have no backing ledger entry.</summary>
        public Guid? LedgerEntryId { get; set; }
        /// <summary>sale | refund | dispute_loss | dispute_fee | adjustment | sms_charge | email_charge | deposit_collected | deposit_captured | deposit_refunded</summary>
        public string EntryKind { get; set; } = null!;
        public string? SourceKind { get; set; }
        public Guid? SourceId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        /// <summary>Tenant-LOCAL calendar date. The bucket a day's journal entry is built from.</summary>
        public DateOnly BusinessDate { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        /// <summary>Negative on refunds. Tax- and tip-inclusive.</summary>
        public int GrossCents { get; set; }
        public int StripeFeeCents { get; set; }
        public int RidepassCutCents { get; set; }
        public int NetToTenantCents { get; set; }
        /// <summary>The tax portion contained in GrossCents. A liability, never revenue.</summary>
        public int TaxCents { get; set; }
        /// <summary>The tip portion contained in GrossCents. A liability, never revenue.</summary>
        public int TipCents { get; set; }
        /// <summary>How much of GrossCents was funded by drawing down a gift card rather than a card charge.</summary>
        public int GiftCardAppliedCents { get; set; }
    }
}
