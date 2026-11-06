namespace Services.Cash
{
    /// <summary>
    /// Thrown from inside the close-out transaction when the session the turn-in belongs to is no
    /// longer 'open' by the time it is flipped: the same drawer was closed out by another request
    /// that slipped between the caller's read and its lock, or the row is simply not this
    /// tenant's. Throwing rolls the whole close back (IDbHelper.WithTransaction), so no second
    /// turn-in, no second claim and no second drawer payout can land against one session.
    /// </summary>
    public sealed class DrawerAlreadyClosedException : Exception
    {
        public Guid CashSessionId { get; }

        public DrawerAlreadyClosedException(Guid cashSessionId)
            : base($"Cash session {cashSessionId} is not open; the drawer was already closed out.")
        {
            CashSessionId = cashSessionId;
        }
    }
}
