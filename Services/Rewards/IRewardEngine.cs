namespace Services.Rewards
{
    /// <summary>
    /// Called from the Stripe webhook after a successful purchase. Auto-enrolls the rider in
    /// every active "auto" program for the tenant, then for every program the rider is enrolled
    /// in: counts qualifying purchases since enrollment, mints a redemption when the threshold
    /// is reached, and sends a proximity email when the rider is the configured number of
    /// purchases away from a reward.
    /// </summary>
    public interface IRewardEngine
    {
        Task ProcessPaidPurchase(Guid tenantId, Guid userId, string riderEmail, string riderFirstName);

        /// <summary>
        /// Credit-back loyalty: pays each active credit_rate program's rate on the money
        /// collected as store credit, once per settled purchase (sourceKind + sourceId key the
        /// idempotency). Walk-ins earn by email when the program is auto-enroll. Best-effort:
        /// callers wrap in try/catch; the sale never depends on it.
        /// </summary>
        Task AwardCreditBack(Guid tenantId, Guid? userId, string? email, string? name,
            string sourceKind, Guid sourceId, int spentCents);
    }
}
