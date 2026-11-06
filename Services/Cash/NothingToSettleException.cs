namespace Services.Cash
{
    /// <summary>
    /// Thrown from inside the shift-close transaction when the claim found no tip to settle and
    /// the person declared no cash tips either: there is no shift report to write. Throwing rolls
    /// the transaction back (IDbHelper.WithTransaction), so no empty tip_shift lands. A close with
    /// nothing claimed but a declared amount is still a report and goes through.
    /// </summary>
    public sealed class NothingToSettleException : Exception
    {
        public NothingToSettleException()
            : base("Nothing to settle: no unclaimed tips and no declared cash tips.")
        {
        }
    }
}
