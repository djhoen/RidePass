namespace Services.Cash
{
    /// <summary>
    /// How a tip jar is divided (Script0298): evenly, in whole cents, among the people who
    /// worked, with the cents that do not divide handed one each to the first people in the
    /// order given. Pure, so the rule is pinned by tests rather than discovered at the till.
    /// The caller decides who is in the list and in what order; this only does the arithmetic.
    /// </summary>
    public static class TipJarMath
    {
        /// <summary>
        /// Splits <paramref name="totalCents"/> across <paramref name="userIds"/> in order. The
        /// shares always sum to the total, differ by at most one cent, and the larger shares go
        /// to the earlier entries. Throws on a non-positive total, an empty list or a repeated
        /// user, since each of those is a caller bug rather than something to round away.
        /// </summary>
        public static IReadOnlyList<(Guid UserId, int AmountCents)> Split(int totalCents, IReadOnlyList<Guid> userIds)
        {
            if (totalCents <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalCents), totalCents, "A tip jar split needs a positive total.");
            if (userIds is null || userIds.Count == 0)
                throw new ArgumentException("A tip jar split needs at least one person.", nameof(userIds));
            if (userIds.Distinct().Count() != userIds.Count)
                throw new ArgumentException("A person can only appear once in a tip jar split.", nameof(userIds));

            var each = totalCents / userIds.Count;
            var remainder = totalCents % userIds.Count;
            var shares = new (Guid UserId, int AmountCents)[userIds.Count];
            for (var i = 0; i < userIds.Count; i++)
                shares[i] = (userIds[i], each + (i < remainder ? 1 : 0));
            return shares;
        }
    }
}
