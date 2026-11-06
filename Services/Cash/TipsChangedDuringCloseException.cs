namespace Services.Cash
{
    /// <summary>
    /// Thrown from inside the close-out transaction when the tips the turn-in actually claimed no
    /// longer cover what the worker said they took from the drawer: a tab was closed, refunded or
    /// re-rung between the shift review and the submit. Throwing is what rolls the transaction
    /// back (CashRepository.SubmitTurnIn runs under IDbHelper.WithTransaction), so nothing of the
    /// close lands and the worker is asked to review again. TipsEarnedCents is the figure the claim
    /// found, for the message.
    /// </summary>
    public sealed class TipsChangedDuringCloseException : Exception
    {
        public long TipsEarnedCents { get; }
        public long CardTipsCents { get; }

        public TipsChangedDuringCloseException(long tipsEarnedCents, long cardTipsCents)
            : base($"Tips changed while the drawer was being closed out; {tipsEarnedCents} cents are now to settle.")
        {
            TipsEarnedCents = tipsEarnedCents;
            CardTipsCents = cardTipsCents;
        }
    }
}
