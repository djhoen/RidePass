using Services.Repositories.Data.QuickBooksData;

namespace Services.Accounting
{
    /// <summary>One side of one journal line. Positive cents, side chosen by the builder.</summary>
    public readonly record struct JournalLine(string AccountKey, bool IsDebit, int AmountCents)
    {
        public string Describe() => $"{(IsDebit ? "DR" : "CR")} {AccountKey} {AmountCents / 100m:0.00}";
    }

    /// <summary>A balanced journal entry ready to be posted as one QBO JournalEntry.</summary>
    public sealed record JournalDraft(DateOnly BusinessDate, IReadOnlyList<JournalLine> Lines, int EntryCount)
    {
        public int TotalDebitCents  => Lines.Where(l => l.IsDebit).Sum(l => l.AmountCents);
        public int TotalCreditCents => Lines.Where(l => !l.IsDebit).Sum(l => l.AmountCents);
        public bool IsEmpty => Lines.Count == 0;
    }

    public sealed class JournalImbalanceException : Exception
    {
        public JournalImbalanceException(string message) : base(message) { }
    }

    /// <summary>
    /// Turns one tenant-local business day of <see cref="AccountingEntry"/> rows into a single
    /// balanced double-entry journal. Pure and deterministic (no I/O, no clock, no DB), so it is
    /// exhaustively unit-testable, which matters more here than anywhere else in the codebase
    /// because the output lands in a customer's real books.
    ///
    /// ── The signed-accumulator design ────────────────────────────────────────────────────
    /// Rather than emit debit/credit lines per entry and hope they net out, every entry accumulates
    /// SIGNED cents into an account map (positive = debit, negative = credit). Lines are only
    /// materialised at the end, with the sign picking the side. This buys three things:
    ///
    ///   • Refunds need no special case. A refund is a sale with a negative gross, so it accumulates
    ///     the exact negatives and flips whichever accounts it needs to.
    ///   • Partial tender splits (a gift card covering part of a card sale) can drive an account
    ///     negative, and it becomes a credit instead of an illegal negative debit. QBO rejects
    ///     negative line amounts.
    ///   • The day nets to a handful of lines instead of thousands, which is the whole point of a
    ///     summary post.
    ///
    /// ── Why it always balances ───────────────────────────────────────────────────────────
    /// For any sale row the ledger guarantees net = gross - stripe_fee - ridepass_cut. So:
    ///
    ///     credits = (gross - tax - tip) + tax + tip                            = gross
    ///     debits  = gift_card + stripe_fee + ridepass_cut + (net - gift_card)  = gross
    ///
    /// The gift-card term cancels, which is what lets one formula cover no-gift-card, partial, and
    /// fully-covered sales. Cash adds a debit of gross to Cash on Hand and its receivable term is
    /// already -ridepass_cut (the tenant holds the cash and owes us our cut), so it cancels too.
    /// dispute_fee falls out for free: gross = 0, so the fee debit and the receivable credit are the
    /// only lines. Build() asserts the invariant anyway and refuses to return an unbalanced draft: /// posting a wrong entry to a customer's books is worse than posting nothing.
    /// </summary>
    public static class JournalEntryBuilder
    {
        /// <summary>
        /// RidePass charges the tenant for these rather than earning from them, so they are an
        /// expense against the receivable, NOT negative revenue (their gross is negative and their
        /// source_kind points at a billing artifact, not a sale).
        /// </summary>
        private static bool IsPlatformCharge(string entryKind) =>
            entryKind is "sms_charge" or "email_charge";

        private static bool IsDeposit(string entryKind) =>
            entryKind is "deposit_collected" or "deposit_released";

        /// <summary>
        /// Note there is deliberately no "is this tenant direct-charge?" parameter. The platform-vs-direct
        /// split is read per entry from its own payment_method, which the finalizer snapshots at charge
        /// time. A tenant's stripe_charge_mode can be flipped by a super admin, and the rest of the
        /// codebase is careful never to let that rewrite history (every purchase row snapshots
        /// stripe_connected_account_id for exactly this reason). Branching on the tenant's CURRENT mode
        /// would re-book old days under the new mode the moment someone re-synced them.
        /// </summary>
        public static JournalDraft Build(
            IReadOnlyList<AccountingEntry> entries,
            DateOnly businessDate)
        {
            var acc = new Dictionary<string, long>();
            void Add(string key, long cents)
            {
                if (cents == 0) return;
                acc[key] = acc.TryGetValue(key, out var cur) ? cur + cents : cents;
            }

            foreach (var e in entries)
            {
                if (IsDeposit(e.EntryKind)) { AccrueDeposit(Add, e); continue; }
                AccrueSale(Add, e);
            }

            // Materialise. Sign picks the side; zeroed accounts drop out entirely (an account that
            // nets to zero across the day carries no information and QBO rejects 0.00 lines).
            var lines = acc
                .Where(kv => kv.Value != 0)
                .OrderBy(kv => Array.IndexOf(QboAccountKeys.All, kv.Key))
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new JournalLine(kv.Key, kv.Value > 0, (int)Math.Abs(kv.Value)))
                .ToList();

            var draft = new JournalDraft(businessDate, lines, entries.Count);
            if (draft.TotalDebitCents != draft.TotalCreditCents)
            {
                throw new JournalImbalanceException(
                    $"Refusing to post {businessDate:yyyy-MM-dd}: debits {draft.TotalDebitCents} != credits {draft.TotalCreditCents} " +
                    $"across {entries.Count} entries. Lines: {string.Join(", ", lines.Select(l => l.Describe()))}");
            }
            return draft;
        }

        /// <summary>Sales, refunds, chargebacks, and the platform's own charges.</summary>
        private static void AccrueSale(Action<string, long> add, AccountingEntry e)
        {
            // ── Credit side: what was earned (or owed onward) ───────────────────────────
            if (IsPlatformCharge(e.EntryKind))
            {
                // gross is negative here, so -gross is the positive amount RidePass charged.
                add(QboAccountKeys.ExpenseRidepassFees, -e.GrossCents);
            }
            else
            {
                // Holds for every sale kind: concession total and event ticket amount are both
                // tax-inclusive, so subtracting tax backs it out whether it was added on top or
                // baked into the advertised price. See the Script0175 header.
                var revenue = e.GrossCents - e.TaxCents - e.TipCents;
                add(QboAccountKeys.RevenueForSourceKind(e.SourceKind), -revenue);
                add(QboAccountKeys.LiabilitySalesTax, -e.TaxCents);
                add(QboAccountKeys.LiabilityTips, -e.TipCents);
            }

            // ── Debit side: what funded it ──────────────────────────────────────────────
            // Drawn from the liability created when the card was sold, not from a card charge.
            // Covers the fully-covered case (payment_method='voucher') and the partial case
            // (payment_method='stripe' on the remainder) identically.
            add(QboAccountKeys.LiabilityGiftCard, e.GiftCardAppliedCents);

            if (e.PaymentMethod == "cash")
            {
                add(QboAccountKeys.AssetUndepositedCash, e.GrossCents);
            }

            if (e.PaymentMethod == "stripe_direct")
            {
                // Direct charge: the tenant is merchant of record, so the money landed in THEIR
                // Stripe balance and THEY bore the Stripe fee, which we deliberately don't know
                // (the ledger records stripe_fee_cents = 0 in direct mode), so we don't book it.
                // Their own Stripe→QBO feed accounts for it. We book only what we know: our
                // application fee came out, the rest is theirs.
                add(QboAccountKeys.ExpenseRidepassFees, e.RidepassCutCents);
                add(QboAccountKeys.AssetStripeClearing, e.GrossCents - e.RidepassCutCents - e.GiftCardAppliedCents);
            }
            else
            {
                add(QboAccountKeys.ExpenseStripeFees, e.StripeFeeCents);
                add(QboAccountKeys.ExpenseRidepassFees, e.RidepassCutCents);
                // net - gift_card. Goes NEGATIVE (a credit, i.e. we're owed) exactly when the tenant
                // already holds the money: a cash sale where net is -cut, or a gift-card-covered
                // sale where the float was collected back when the card was bought.
                add(QboAccountKeys.AssetRidepassReceivable, e.NetToTenantCents - e.GiftCardAppliedCents);
            }
        }

        /// <summary>
        /// Rental security deposits. Refundable money the payout ledger deliberately ignores (it
        /// isn't earnings) but a set of books must carry: cash in, and an obligation to hand it back.
        /// See the Part 2 note in Script0175.
        /// </summary>
        private static void AccrueDeposit(Action<string, long> add, AccountingEntry e)
        {
            var tenderAccount = e.PaymentMethod switch
            {
                "cash"          => QboAccountKeys.AssetUndepositedCash,
                "stripe_direct" => QboAccountKeys.AssetStripeClearing,
                _               => QboAccountKeys.AssetRidepassReceivable,
            };

            switch (e.EntryKind)
            {
                case "deposit_collected":
                    // Money in, but owed back. Never revenue at this point.
                    add(tenderAccount, e.GrossCents);
                    add(QboAccountKeys.LiabilityRentalDeposit, -e.GrossCents);
                    break;

                case "deposit_released":
                    // The hold ends on return: the WHOLE deposit stops being held, whatever its fate.
                    // Any damage kept is booked separately as a real 'rental_deposit' sale in the
                    // ledger (AccrueSale credits RevenueDepositForfeited for it), so crediting income
                    // here as well would count the same damage twice. This row only unwinds the hold.
                    add(QboAccountKeys.LiabilityRentalDeposit, e.GrossCents);
                    add(tenderAccount, -e.GrossCents);
                    break;
            }
        }
    }
}
