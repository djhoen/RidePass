namespace Services.Discounts
{
    /// <summary>
    /// Splits one sale-level discount across the lines it applies to, proportionally to what each
    /// line is worth.
    ///
    /// Separate from <see cref="DiscountStacking"/> because they answer different questions:
    /// stacking decides HOW MUCH comes off the sale, this decides WHICH LINES it comes off. The gate
    /// counter needs both, and needs the split specifically because a counter cart is mixed: a
    /// discount scoped to event tickets has to land on the ticket lines and leave a membership in
    /// the same sale untouched.
    ///
    /// The split has to be exact. Per-line amounts are what get stored, taxed and refunded, so if
    /// they do not sum to the discount the customer was told about, the sale silently disagrees with
    /// its own receipt.
    /// </summary>
    public static class DiscountSpread
    {
        /// <summary>
        /// Spreads <paramref name="discountCents"/> across <paramref name="lineBases"/> in proportion
        /// to each base, returning one amount per line that sums EXACTLY to the discount (after
        /// clamping, see below).
        ///
        /// Integer division always loses a few cents; rather than drop them, the remainder goes to
        /// the largest line, where a one-cent adjustment is least visible against the line's own
        /// price. Assigning it to the LARGEST rather than the last also keeps the result independent
        /// of cart order, so the same cart discounts the same way however the cashier rang it up.
        ///
        /// The discount is clamped to the total base first: a $20 discount against $15 of eligible
        /// goods takes $15, never $20. Without that clamp a fixed-amount discount larger than the
        /// eligible subtotal would drive a line negative and hand the customer money.
        /// </summary>
        public static int[] Across(IReadOnlyList<int> lineBases, int discountCents)
        {
            var result = new int[lineBases.Count];
            if (discountCents <= 0 || lineBases.Count == 0) return result;

            long totalBase = 0;
            foreach (var b in lineBases) totalBase += Math.Max(0, b);
            if (totalBase <= 0) return result;

            if (discountCents > totalBase) discountCents = (int)totalBase;

            var handedOut = 0;
            var largestIdx = 0;
            for (var i = 0; i < lineBases.Count; i++)
            {
                var b = Math.Max(0, lineBases[i]);
                result[i] = (int)((long)discountCents * b / totalBase);
                handedOut += result[i];
                if (b > Math.Max(0, lineBases[largestIdx])) largestIdx = i;
            }

            result[largestIdx] += discountCents - handedOut;
            return result;
        }
    }
}
