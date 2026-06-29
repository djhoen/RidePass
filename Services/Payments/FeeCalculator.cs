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
        /// <summary>
        /// Apply the monthly cap (if set on the tenant) to the snapshotted service charge for this
        /// sale, then compute net-to-tenant. The caller is responsible for snapshotting the
        /// service charge at sale time so historical entries remain stable when the tenant's
        /// settings later change.
        /// </summary>
        Task<FeeCalculation> Calculate(Guid tenantId, int grossCents, int stripeFeeCents, int serviceChargeCents, DateTime occurredAtUtc, bool isDirect = false);
    }

    public class FeeCalculator : IFeeCalculator
    {
        private readonly ITenantRepository _tenants;
        private readonly ITenantLedgerRepository _ledger;

        public FeeCalculator(ITenantRepository tenants, ITenantLedgerRepository ledger)
        {
            _tenants = tenants;
            _ledger = ledger;
        }

        public async Task<FeeCalculation> Calculate(Guid tenantId, int grossCents, int stripeFeeCents, int serviceChargeCents, DateTime occurredAtUtc, bool isDirect = false)
        {
            var cumulativeMonthly = await _ledger.GetMonthlyGrossVolumeCents(tenantId, occurredAtUtc);

            // Direct charge: the charge ran on the tenant's own account, so they already hold the
            // funds (NetToTenant is not something we owe them) and they bore the Stripe fee. Our cut
            // is the application fee Stripe routed to us, which equals the service charge. The monthly
            // cap does not apply because there is no platform settlement to cap.
            if (isDirect)
            {
                return new FeeCalculation(serviceChargeCents, NetToTenantCents: 0, AppliedTierId: null, cumulativeMonthly);
            }

            var ridepassCut = serviceChargeCents;

            // Honor monthly cap if set on the tenant.
            var tenant = await _tenants.GetById(tenantId);
            if (tenant?.MonthlyServiceChargeCapCents is int cap && cap > 0)
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
            return new FeeCalculation(ridepassCut, net, AppliedTierId: null, cumulativeMonthly);
        }
    }
}
