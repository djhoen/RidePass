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
    }
}
