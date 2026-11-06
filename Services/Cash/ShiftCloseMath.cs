namespace Services.Cash
{
    /// <summary>
    /// The arithmetic of closing out a drawer with tips in it. Pure and clock-free so it can be
    /// tested exhaustively; CashController owns the I/O around it.
    ///
    /// The rule is the industry one (Toast Shift Review, Square Cash Out): a worker takes their
    /// tips in cash out of their own drawer, no more than they earned this shift. Whatever the
    /// drawer could not cover is a debt from the house to the worker ("house owes"), and a manager
    /// settles that later in cash or through payroll, never for more than is outstanding.
    /// </summary>
    public static class ShiftCloseMath
    {
        /// <summary>
        /// Tips a worker may take from the drawer: never negative, never more than they earned.
        /// Negative "earned" (which the schema forbids anyway) reads as nothing to take.
        /// </summary>
        public static int ClampTipsFromDrawer(int requestedCents, int tipsEarnedCents) =>
            Math.Clamp(requestedCents, 0, Math.Max(0, tipsEarnedCents));

        /// <summary>
        /// The server-side validity rule for a turn-in: the request is accepted exactly when
        /// clamping it would change nothing. Rejecting rather than silently clamping matters,
        /// because the figure is what the worker says they physically took.
        /// </summary>
        public static bool IsValidTipsFromDrawer(int requestedCents, int tipsEarnedCents) =>
            ClampTipsFromDrawer(requestedCents, tipsEarnedCents) == requestedCents;

        /// <summary>What the house still owes after the drawer paid what it could. Never negative.</summary>
        public static int HouseOwes(int tipsEarnedCents, int tipsFromDrawerCents) =>
            Math.Max(0, Math.Max(0, tipsEarnedCents) - Math.Max(0, tipsFromDrawerCents));

        /// <summary>
        /// A worker's outstanding balance: house-owes accrued across their shifts minus what a
        /// manager has since paid them (cash or payroll). Drawer payouts are not subtracted: they
        /// already reduced house-owes at close. Never negative, so an over-payment made by hand in
        /// the database reads as "nothing owed" rather than as a debt the worker owes back.
        /// </summary>
        public static long OutstandingOwed(long houseOwesTotalCents, long paidByManagerTotalCents) =>
            Math.Max(0, houseOwesTotalCents - paidByManagerTotalCents);

        /// <summary>A manager payout must be positive and no more than what is outstanding.</summary>
        public static bool IsValidPayout(int amountCents, long outstandingCents) =>
            amountCents > 0 && amountCents <= outstandingCents;
    }
}
