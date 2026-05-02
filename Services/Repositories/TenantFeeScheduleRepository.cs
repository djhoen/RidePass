using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantFeeScheduleRepository : IFeeScheduleRepository
    {
        private const string ScheduleColumns = @"
            id, tenant_id AS TenantId, effective_from_utc AS EffectiveFromUtc,
            effective_to_utc AS EffectiveToUtc, monthly_cap_cents AS MonthlyCapCents,
            created_at AS CreatedAt";

        private const string TierColumns = @"
            id, schedule_id AS ScheduleId, min_volume_cents AS MinVolumeCents,
            max_volume_cents AS MaxVolumeCents, rate_bps AS RateBps, sort_order AS SortOrder";

        private readonly IDbHelper _db;

        public TenantFeeScheduleRepository(IDbHelper db) => _db = db;

        public async Task<TenantFeeScheduleWithTiers?> GetActive(Guid tenantId, DateTime atUtc)
        {
            var sql = $@"
                SELECT {ScheduleColumns}
                FROM tenant_fee_schedule
                WHERE tenant_id = @tenantId
                  AND effective_from_utc <= @atUtc
                  AND (effective_to_utc IS NULL OR effective_to_utc > @atUtc)
                ORDER BY effective_from_utc DESC
                LIMIT 1";
            var schedule = (await _db.Query<TenantFeeSchedule>(sql, new { tenantId, atUtc })).FirstOrDefault();
            if (schedule is null) return null;
            return new TenantFeeScheduleWithTiers
            {
                Schedule = schedule,
                Tiers = await GetTiers(schedule.Id),
            };
        }

        public async Task<TenantFeeScheduleWithTiers?> GetById(Guid scheduleId, Guid tenantId)
        {
            var sql = $@"
                SELECT {ScheduleColumns}
                FROM tenant_fee_schedule
                WHERE id = @scheduleId AND tenant_id = @tenantId
                LIMIT 1";
            var schedule = (await _db.Query<TenantFeeSchedule>(sql, new { scheduleId, tenantId })).FirstOrDefault();
            if (schedule is null) return null;
            return new TenantFeeScheduleWithTiers
            {
                Schedule = schedule,
                Tiers = await GetTiers(schedule.Id),
            };
        }

        public async Task<List<TenantFeeSchedule>> ListByTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {ScheduleColumns}
                FROM tenant_fee_schedule
                WHERE tenant_id = @tenantId
                ORDER BY effective_from_utc DESC";
            return (await _db.Query<TenantFeeSchedule>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> Replace(TenantFeeSchedule schedule, IEnumerable<TenantFeeTier> tiers)
        {
            // Close any currently-active schedule for this tenant by setting its effective_to_utc.
            await _db.Execute(@"
                UPDATE tenant_fee_schedule
                SET effective_to_utc = @effectiveFromUtc
                WHERE tenant_id = @tenantId AND effective_to_utc IS NULL",
                new { tenantId = schedule.TenantId, effectiveFromUtc = schedule.EffectiveFromUtc });

            var insertSched = @"
                INSERT INTO tenant_fee_schedule (tenant_id, effective_from_utc, effective_to_utc, monthly_cap_cents)
                VALUES (@TenantId, @EffectiveFromUtc, @EffectiveToUtc, @MonthlyCapCents)
                RETURNING id";
            var newId = (await _db.Query<Guid>(insertSched, schedule)).First();

            const string insertTier = @"
                INSERT INTO tenant_fee_tier (schedule_id, min_volume_cents, max_volume_cents, rate_bps, sort_order)
                VALUES (@ScheduleId, @MinVolumeCents, @MaxVolumeCents, @RateBps, @SortOrder)";
            foreach (var tier in tiers)
            {
                tier.ScheduleId = newId;
                await _db.Execute(insertTier, tier);
            }
            return newId;
        }

        private async Task<List<TenantFeeTier>> GetTiers(Guid scheduleId)
        {
            var sql = $@"
                SELECT {TierColumns}
                FROM tenant_fee_tier
                WHERE schedule_id = @scheduleId
                ORDER BY sort_order";
            return (await _db.Query<TenantFeeTier>(sql, new { scheduleId })).ToList();
        }
    }
}
