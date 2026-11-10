namespace Services.Passes
{
    /// <summary>
    /// Passes are not transferable (Highland go-live item 12: "3 ride cards are non-transferrable").
    /// A pass admits the person registered on it, and a ride credit pays only for that person.
    /// Pure helpers so the rule is unit tested; the callers are SeasonPassController.CompleteRegistration
    /// and PurchaseController.CompleteTicketRegistration.
    /// </summary>
    public static class PassHolderRules
    {
        /// <summary>
        /// The same person by name: trimmed, inner spaces collapsed, case ignored. Typing "alex  rivera"
        /// for "Alex Rivera" is the same holder; "Sam Rivera" is not.
        /// </summary>
        public static bool SameName(string? firstA, string? lastA, string? firstB, string? lastB) =>
            Norm(firstA) == Norm(firstB) && Norm(lastA) == Norm(lastB);

        /// <summary>
        /// Same person: same name, and the same birthdate when both sides have one (a birthdate
        /// that was never given can't contradict one that was).
        /// </summary>
        public static bool SamePerson(string? firstA, string? lastA, DateTime? birthA,
            string? firstB, string? lastB, DateTime? birthB) =>
            SameName(firstA, lastA, firstB, lastB)
            && (birthA is null || birthB is null || birthA.Value.Date == birthB.Value.Date);

        /// <summary>
        /// Whether re-registering would hand the pass to someone else: it already has a holder and
        /// the new details name a different person or bring a different photo. Only refused once the
        /// pass has been used; before that, the buyer is fixing a typo or a bad photo.
        /// </summary>
        public static bool IsHolderChange(string? storedFirst, string? storedLast, DateTime? storedBirth, string? storedPhoto,
            string newFirst, string newLast, DateTime? newBirth, string newPhoto)
        {
            if (string.IsNullOrWhiteSpace(storedFirst) && string.IsNullOrWhiteSpace(storedLast)) return false;
            if (!SamePerson(storedFirst, storedLast, storedBirth, newFirst, newLast, newBirth)) return true;
            return storedPhoto is not null && storedPhoto != newPhoto;
        }

        private static string Norm(string? s) =>
            string.Join(' ', (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToLowerInvariant();
    }
}
