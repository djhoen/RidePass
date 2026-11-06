using Services.Repositories.Data.ConcessionData;

namespace Services.Concessions
{
    /// <summary>Sale-level money figures recomputed from a tab's lines.</summary>
    public readonly record struct ConcessionTabTotals(int SubtotalCents, int DiscountCents, int TaxCents, int TotalCents);

    /// <summary>Which settlement path a close-tab request takes once the amount due is known.</summary>
    public enum TabClosePath
    {
        /// <summary>Nothing left to collect from a card: cash tender, or credit/comp covered it. Paid now.</summary>
        PaidNow,
        /// <summary>Collect on the reader with a NEW card-present PaymentIntent.</summary>
        CardPresent,
        /// <summary>Capture the card hold placed when the tab was opened.</summary>
        CaptureHold,
        /// <summary>The tab total exceeds the authorized hold; try an incremental authorization or fall back to a new card.</summary>
        HoldTooSmall,
        /// <summary>'hold' was asked for but no card is held on this tab.</summary>
        NoHold,
        /// <summary>Card path asked for less than the processor minimum.</summary>
        CardBelowMinimum,
        /// <summary>Unknown tender.</summary>
        Invalid,
    }

    /// <summary>
    /// What a Close/Void does after asking Stripe to cancel a PaymentIntent an earlier attempt left on
    /// the tab (its own card PI, or the hold). Derived from what CancelPaymentIntentAsync reports.
    /// </summary>
    public enum InFlightCardOutcome
    {
        /// <summary>Stripe confirmed 'canceled': the intent can never charge; the caller may proceed.</summary>
        Released,
        /// <summary>Stripe reports 'succeeded': the customer already paid on it. Finalize that payment; do not proceed.</summary>
        AlreadyPaid,
        /// <summary>Unreachable, still processing, or any other state: nothing is known to be safe. Refuse and leave the tab as it is.</summary>
        Blocked,
    }

    /// <summary>What the finalizer does with a payment_intent.succeeded for a concession sale.</summary>
    public enum PaidIntentOutcome
    {
        /// <summary>The sale is waiting on exactly this intent: flip it to paid and book the money.</summary>
        Book,
        /// <summary>Already paid (or later refunded) on this very intent: a duplicate webhook / finalize. Nothing to do.</summary>
        AlreadyBooked,
        /// <summary>Money landed that nothing will book, but the sale is still live (open/pending) or dead for another reason: record it loudly, keep the money for a human.</summary>
        UnbookedKeep,
        /// <summary>Money landed on a sale that was settled another way (cash, a different card) or voided: record it loudly and refund the stray charge.</summary>
        UnbookedRefund,
    }

    /// <summary>
    /// The pure decisions behind F&amp;B open tabs, kept out of ConcessionController so they can be unit
    /// tested the way RefundLedgerMath is: how a tab's totals are rebuilt from every round's lines, which
    /// tender path a close takes, and which statuses may be voided or refunded.
    /// </summary>
    public static class ConcessionTabRules
    {
        public const int CardMinimumCents = 50;

        /// <summary>
        /// Rebuilds subtotal / discount / tax / total from ALL of a tab's lines (every round). Lines carry
        /// NET totals (after their own discount and any allocated order discount), so the gross subtotal
        /// is net + discount per line, exactly how CreateSale derives it from ResolveCartLines. Inclusive
        /// pricing keeps tax inside the subtotal; exclusive adds it on top. Tip is added last.
        /// </summary>
        public static ConcessionTabTotals Recompute(IEnumerable<ConcessionSaleLine> lines, bool pricesIncludeTax, int tipCents)
        {
            var subtotal = 0; var discount = 0; var tax = 0;
            foreach (var l in lines)
            {
                subtotal += l.LineTotalCents + l.DiscountCents;
                discount += l.DiscountCents;
                tax += l.TaxCents;
            }
            var total = subtotal - discount + (pricesIncludeTax ? 0 : tax) + Math.Max(0, tipCents);
            return new ConcessionTabTotals(subtotal, discount, tax, Math.Max(0, total));
        }

        /// <summary>The next round number for a tab: one past the highest fired so far (1 for an empty tab).</summary>
        public static int NextRoundNumber(IEnumerable<ConcessionSaleLine> lines)
        {
            var max = 0;
            foreach (var l in lines) if (l.RoundNumber > max) max = l.RoundNumber;
            return max + 1;
        }

        /// <summary>
        /// Picks the settlement path. Anything with nothing due (cash, or credit/comp covering the whole
        /// tab) is paid on the spot. Both card tenders need the processor minimum: Stripe rejects a
        /// capture below it exactly as it rejects a charge below it, so a sub-minimum remainder on a
        /// held card is a cash remainder, not a capture. 'hold' then captures the authorization when it
        /// covers the total and reports HoldTooSmall otherwise so the caller can try an incremental
        /// authorization or fall back to collecting a card.
        /// </summary>
        public static TabClosePath ResolveClosePath(string? paymentMethod, int dueCents, string? holdPaymentIntentId, int? holdAmountCents)
        {
            var method = (paymentMethod ?? "").Trim().ToLowerInvariant();
            if (method is not ("cash" or "card" or "hold")) return TabClosePath.Invalid;
            if (method == "cash" || dueCents <= 0) return TabClosePath.PaidNow;
            if (method == "hold" && string.IsNullOrEmpty(holdPaymentIntentId)) return TabClosePath.NoHold;
            if (dueCents < CardMinimumCents) return TabClosePath.CardBelowMinimum;
            if (method == "hold")
                return dueCents > (holdAmountCents ?? 0) ? TabClosePath.HoldTooSmall : TabClosePath.CaptureHold;
            return TabClosePath.CardPresent;
        }

        /// <summary>
        /// True while a card close is in flight: the tab is still open but a PaymentIntent (a new card
        /// on the reader, or the hold being captured) is pointed at it. Nothing else may change the tab
        /// until that intent is settled at Stripe first (cancelled, or found to have succeeded).
        /// </summary>
        public static bool HasCardInFlight(string status, string? stripePaymentIntentId)
            => status == "open" && !string.IsNullOrEmpty(stripePaymentIntentId);

        /// <summary>
        /// Maps what Stripe said to a cancel request onto what the caller may do next. Only an explicit
        /// 'canceled' releases the tab; 'succeeded' means the money already moved; anything else
        /// (null = unreachable / unknown, 'processing', an unexpected state) blocks the action so a live
        /// intent can never be left behind a tab that moved on.
        /// </summary>
        public static InFlightCardOutcome ResolveInFlightCancel(string? cancelResult) => cancelResult switch
        {
            "canceled" => InFlightCardOutcome.Released,
            "succeeded" => InFlightCardOutcome.AlreadyPaid,
            _ => InFlightCardOutcome.Blocked,
        };

        /// <summary>
        /// Decides what a succeeded PaymentIntent means for the concession sale it was matched to,
        /// either as the sale's own intent or (for a tab) as its card hold. A sale waiting on this very
        /// intent is booked; a sale already paid on it is a duplicate. Everything else is money nothing
        /// will book: a tab that was voided, or paid by cash / another card, gets the stray charge
        /// refunded; a sale that is still live (open, pending) or dead for an unrelated reason keeps the
        /// money until a human looks, because a refund there could hand back a payment that is about to
        /// be booked by the close in progress.
        /// </summary>
        public static PaidIntentOutcome ClassifyPaidIntent(string saleStatus, string? salePaymentIntentId, string paymentIntentId)
        {
            var isOwnIntent = salePaymentIntentId == paymentIntentId;
            if (isOwnIntent)
            {
                return saleStatus switch
                {
                    "pending" or "open" => PaidIntentOutcome.Book,
                    "paid" or "refunded" => PaidIntentOutcome.AlreadyBooked,
                    "voided" => PaidIntentOutcome.UnbookedRefund,
                    _ => PaidIntentOutcome.UnbookedKeep,   // failed / abandoned: the order was never fulfilled; a human decides
                };
            }
            return saleStatus switch
            {
                "paid" or "refunded" or "voided" => PaidIntentOutcome.UnbookedRefund,
                _ => PaidIntentOutcome.UnbookedKeep,
            };
        }

        /// <summary>Null when a sale in this status can be voided as a tab; otherwise the reason it can't.</summary>
        public static string? VoidBlocker(string status) => status switch
        {
            "open" => null,
            "voided" => "This tab was already voided.",
            "paid" => "This tab was already closed and paid. Refund it instead.",
            _ => $"A {status} sale can't be voided.",
        };

        /// <summary>Null when a sale in this status can be refunded; otherwise the reason it can't. Open and voided tabs never can.</summary>
        public static string? RefundBlocker(string status) => status switch
        {
            "paid" => null,
            "open" => "This tab is still open. Close or void it instead of refunding.",
            "voided" => "A voided tab took no money, so there is nothing to refund.",
            _ => $"A {status} sale can't be refunded.",
        };

        /// <summary>
        /// Sale-level discount kind after another round or a close-time discount lands: the same kind
        /// stays itself, two different kinds become 'mixed', and nothing-yet takes the new one.
        /// </summary>
        public static string? MergeDiscountKind(string? existing, string? applied)
        {
            if (string.IsNullOrEmpty(applied)) return existing;
            if (string.IsNullOrEmpty(existing)) return applied;
            return existing == applied ? existing : "mixed";
        }

        /// <summary>"Visa ••4242 · SMITH" from the held card, or null when nothing useful is known.</summary>
        public static string? AutoTabName(string? brand, string? last4, string? cardholderName)
        {
            var label = HoldCardLabel(brand, last4);
            var holder = (cardholderName ?? "").Trim();
            if (label is null) return holder.Length > 0 ? holder.ToUpperInvariant() : null;
            return holder.Length > 0 ? $"{label} · {holder.ToUpperInvariant()}" : label;
        }

        /// <summary>"Visa ••4242" badge text, or null when no card is known.</summary>
        public static string? HoldCardLabel(string? brand, string? last4)
        {
            var b = (brand ?? "").Trim();
            var l = (last4 ?? "").Trim();
            if (b.Length == 0 && l.Length == 0) return null;
            var brandLabel = b.Length == 0 ? "Card" : b.ToLowerInvariant() switch
            {
                "visa" => "Visa",
                "mastercard" => "Mastercard",
                "amex" or "american_express" => "Amex",
                "discover" => "Discover",
                "diners" or "diners_club" => "Diners",
                "jcb" => "JCB",
                "unionpay" => "UnionPay",
                "interac" => "Interac",
                _ => char.ToUpperInvariant(b[0]) + b[1..],
            };
            return l.Length == 0 ? brandLabel : $"{brandLabel} ••{l}";
        }
    }
}
