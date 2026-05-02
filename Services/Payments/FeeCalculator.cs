using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Payments
{
    public record FeeCalculation(
        int RidepassCutCents,
        int NetToTenantCents,
        Guid? AppliedTierId,
        long CumulativeMonthlyVolumeAtSaleCents);

    public interface IFeeCalculator
    {
        Task<FeeCalculation> Calculate(Guid tenantId, int grossCents, int stripeFeeCents, DateTime occurredAtUtc);
    }

    /// <summary>
    /// Per-transaction tiered-fee calculator with optional monthly cap. The tenant's UTC-month
    /// cumulative volume up to (but not including) the current transaction selects the applicable
    /// tier. The result is snapshotted onto the ledger entry so historical entries are immutable
    /// even when the schedule is later replaced.
    /// </summary>
    public class FeeCalculator : IFeeCalculator
    {
        private readonly IFeeScheduleRepository _schedules;
        private readonly ITenantLedgerRepository _ledger;

        public FeeCalculator(IFeeScheduleRepository schedules, ITenantLedgerRepository ledger)
        {
            _schedules = schedules;
            _ledger = ledger;
        }

        public async Task<FeeCalculation> Calculate(Guid tenantId, int grossCents, int stripeFeeCents, DateTime occurredAtUtc)
        {
            var cumulativeMonthly = await _ledger.GetMonthlyGrossVolumeCents(tenantId, occurredAtUtc);

            var scheduleWithTiers = await _schedules.GetActive(tenantId, occurredAtUtc);
            if (scheduleWithTiers is null || scheduleWithTiers.Tiers.Count == 0)
            {
                // No schedule configured — RidePass takes nothing. Tenant gets gross minus Stripe fees.
                return new FeeCalculation(0, grossCents - stripeFeeCents, null, cumulativeMonthly);
            }

            var tier = scheduleWithTiers.Tiers.FirstOrDefault(t =>
                cumulativeMonthly >= t.MinVolumeCents &&
                (t.MaxVolumeCents is null || cumulativeMonthly < t.MaxVolumeCents.Value));

            if (tier is null)
            {
                // Volume exceeds the highest tier's max — defensive fallback to the top tier.
                tier = scheduleWithTiers.Tiers.OrderByDescending(t => t.MinVolumeCents).First();
            }

            var ridepassCut = (int)((long)grossCents * tier.RateBps / 10_000L);

            // Honor monthly cap if set.
            if (scheduleWithTiers.Schedule.MonthlyCapCents is int cap && cap > 0)
            {
                var alreadyTaken = await _ledger.GetMonthlyRidepassCutCents(tenantId, occurredAtUtc);
                var remainingBudget = cap - (int)Math.Min(int.MaxValue, alreadyTaken);
                if (remainingBudget <= 0)
                {
                    ridepassCut = 0;
                }
                else if (ridepassCut > remainingBudget)
                {
                    ridepassCut = remainingBudget;
                }
            }

            var net = grossCents - stripeFeeCents - ridepassCut;
            return new FeeCalculation(ridepassCut, net, tier.Id, cumulativeMonthly);
        }
    }
}
