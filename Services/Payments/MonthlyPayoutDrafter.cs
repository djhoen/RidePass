using Microsoft.Extensions.Logging;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Payments
{
    public interface IMonthlyPayoutDrafter
    {
        /// <summary>
        /// For each tenant, drafts a 'pending' payout covering the previous UTC calendar month
        /// if one doesn't already exist for that period. Idempotent: existing payouts for the
        /// period are detected by period_start_utc and skipped. Empty drafts (no unpaid entries
        /// in the period) are voided so the payouts list isn't cluttered.
        /// </summary>
        Task<DraftSummary> Run();
    }

    public record DraftSummary(int Drafted, int Skipped, int VoidedEmpty, int TotalNetCents);

    public class MonthlyPayoutDrafter : IMonthlyPayoutDrafter
    {
        private readonly ITenantRepository _tenants;
        private readonly ITenantPayoutRepository _payouts;
        private readonly ILogger<MonthlyPayoutDrafter> _logger;

        public MonthlyPayoutDrafter(
            ITenantRepository tenants,
            ITenantPayoutRepository payouts,
            ILogger<MonthlyPayoutDrafter> logger)
        {
            _tenants = tenants;
            _payouts = payouts;
            _logger = logger;
        }

        public async Task<DraftSummary> Run()
        {
            var now = DateTime.UtcNow;
            var periodEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodStart = periodEnd.AddMonths(-1);

            var tenants = await _tenants.ListAll();
            int drafted = 0, skipped = 0, voidedEmpty = 0, totalNet = 0;

            foreach (var tenant in tenants)
            {
                try
                {
                    // Idempotency: is there already a payout for this exact period?
                    var existing = await _payouts.ListByTenant(tenant.Id, take: 24);
                    if (existing.Any(p => p.PeriodStartUtc == periodStart && p.PeriodEndUtc == periodEnd))
                    {
                        skipped++;
                        continue;
                    }

                    var payout = new TenantPayout
                    {
                        TenantId = tenant.Id,
                        Status = "pending",
                        PeriodStartUtc = periodStart,
                        PeriodEndUtc = periodEnd,
                        Memo = "Auto-drafted by TaskRunner",
                    };
                    payout.Id = await _payouts.Create(payout);

                    var attached = await _payouts.AttachUnpaidEntries(payout.Id, tenant.Id, periodStart, periodEnd);
                    if (attached == 0)
                    {
                        await _payouts.Void(payout.Id, tenant.Id);
                        voidedEmpty++;
                        continue;
                    }

                    await _payouts.RefreshTotals(payout.Id);
                    var fresh = await _payouts.GetById(payout.Id, tenant.Id);
                    drafted++;
                    if (fresh is not null) totalNet += fresh.NetPaidCents;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to draft payout for tenant {TenantId}", tenant.Id);
                }
            }

            return new DraftSummary(drafted, skipped, voidedEmpty, totalNet);
        }
    }
}
