using Services.Helpers.Interfaces;
using Services.Repositories.Data.RewardData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RewardRepository : IRewardRepository
    {
        private const string ProgramColumns = @"
            id, tenant_id AS TenantId, name, description,
            enrollment_mode AS EnrollmentMode,
            requirement_kind AS RequirementKind,
            requirement_count AS RequirementCount,
            reward_percent_off AS RewardPercentOff,
            proximity_email_threshold AS ProximityEmailThreshold,
            is_active AS IsActive,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string EnrollmentColumns = @"
            id, program_id AS ProgramId, user_id AS UserId,
            enrolled_at AS EnrolledAt,
            last_proximity_emailed_at_count AS LastProximityEmailedAtCount";

        private const string RedemptionColumns = @"
            id, program_id AS ProgramId, user_id AS UserId,
            earned_at AS EarnedAt, redeemed_at AS RedeemedAt,
            redeemed_on_kind AS RedeemedOnKind, redeemed_on_id AS RedeemedOnId";

        private readonly IDbHelper _db;

        public RewardRepository(IDbHelper db) => _db = db;

        public async Task<List<RewardProgram>> ListProgramsForTenant(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? " AND is_active = true" : "";
            var sql = $@"
                SELECT {ProgramColumns}
                FROM reward_program
                WHERE tenant_id = @tenantId {filter}
                ORDER BY created_at DESC";
            return (await _db.Query<RewardProgram>(sql, new { tenantId })).ToList();
        }

        public async Task<RewardProgram?> GetProgram(Guid programId, Guid tenantId)
        {
            var sql = $"SELECT {ProgramColumns} FROM reward_program WHERE id = @programId AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<RewardProgram>(sql, new { programId, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateProgram(RewardProgram p)
        {
            const string sql = @"
                INSERT INTO reward_program
                    (tenant_id, name, description, enrollment_mode, requirement_kind,
                     requirement_count, reward_percent_off, proximity_email_threshold, is_active)
                VALUES
                    (@TenantId, @Name, @Description, @EnrollmentMode, @RequirementKind,
                     @RequirementCount, @RewardPercentOff, @ProximityEmailThreshold, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task UpdateProgram(RewardProgram p)
        {
            const string sql = @"
                UPDATE reward_program
                SET name = @Name,
                    description = @Description,
                    enrollment_mode = @EnrollmentMode,
                    requirement_kind = @RequirementKind,
                    requirement_count = @RequirementCount,
                    reward_percent_off = @RewardPercentOff,
                    proximity_email_threshold = @ProximityEmailThreshold,
                    is_active = @IsActive,
                    updated_at = now()
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task DeleteProgram(Guid programId, Guid tenantId)
        {
            const string sql = "DELETE FROM reward_program WHERE id = @programId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { programId, tenantId });
        }

        public async Task<RewardEnrollment?> GetEnrollment(Guid programId, Guid userId)
        {
            var sql = $"SELECT {EnrollmentColumns} FROM reward_enrollment WHERE program_id = @programId AND user_id = @userId LIMIT 1";
            return (await _db.Query<RewardEnrollment>(sql, new { programId, userId })).FirstOrDefault();
        }

        public async Task<List<RewardEnrollment>> ListEnrollmentsForUser(Guid userId)
        {
            var sql = $"SELECT {EnrollmentColumns} FROM reward_enrollment WHERE user_id = @userId";
            return (await _db.Query<RewardEnrollment>(sql, new { userId })).ToList();
        }

        public async Task<Guid> CreateEnrollment(Guid programId, Guid userId)
        {
            // Idempotent: returning the existing id on conflict so callers can blindly enroll.
            const string sql = @"
                INSERT INTO reward_enrollment (program_id, user_id)
                VALUES (@programId, @userId)
                ON CONFLICT (program_id, user_id) DO UPDATE SET program_id = EXCLUDED.program_id
                RETURNING id";
            return (await _db.Query<Guid>(sql, new { programId, userId })).First();
        }

        public async Task DeleteEnrollment(Guid programId, Guid userId)
        {
            const string sql = "DELETE FROM reward_enrollment WHERE program_id = @programId AND user_id = @userId";
            await _db.Execute(sql, new { programId, userId });
        }

        public async Task UpdateLastProximityEmailedAtCount(Guid enrollmentId, int count)
        {
            const string sql = "UPDATE reward_enrollment SET last_proximity_emailed_at_count = @count WHERE id = @enrollmentId";
            await _db.Execute(sql, new { enrollmentId, count });
        }

        public async Task<List<RewardRedemption>> ListRedemptionsForUser(Guid userId, bool unredeemedOnly)
        {
            var filter = unredeemedOnly ? " AND redeemed_at IS NULL" : "";
            var sql = $@"
                SELECT {RedemptionColumns}
                FROM reward_redemption
                WHERE user_id = @userId {filter}
                ORDER BY earned_at DESC";
            return (await _db.Query<RewardRedemption>(sql, new { userId })).ToList();
        }

        public async Task<List<RewardRedemption>> ListRedemptionsForProgram(Guid programId)
        {
            var sql = $"SELECT {RedemptionColumns} FROM reward_redemption WHERE program_id = @programId ORDER BY earned_at DESC";
            return (await _db.Query<RewardRedemption>(sql, new { programId })).ToList();
        }

        public async Task<RewardRedemption?> GetRedemption(Guid redemptionId)
        {
            var sql = $"SELECT {RedemptionColumns} FROM reward_redemption WHERE id = @redemptionId LIMIT 1";
            return (await _db.Query<RewardRedemption>(sql, new { redemptionId })).FirstOrDefault();
        }

        public async Task<Guid> CreateRedemption(Guid programId, Guid userId)
        {
            const string sql = @"
                INSERT INTO reward_redemption (program_id, user_id)
                VALUES (@programId, @userId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, new { programId, userId })).First();
        }

        public async Task MarkRedemptionUsed(Guid redemptionId, string kind, Guid sourceId)
        {
            const string sql = @"
                UPDATE reward_redemption
                SET redeemed_at = now(), redeemed_on_kind = @kind, redeemed_on_id = @sourceId
                WHERE id = @redemptionId AND redeemed_at IS NULL";
            await _db.Execute(sql, new { redemptionId, kind, sourceId });
        }

        public async Task<int> CountQualifyingPurchases(Guid tenantId, Guid userId, string requirementKind, DateTime sinceUtc)
        {
            // Day passes: sum quantity. Tickets: count rows. For 'any', combine both.
            // 'paid' and 'redeemed' both count; 'pending' / 'cancelled' / 'refunded' don't.
            const string passSql = @"
                SELECT COALESCE(SUM(quantity), 0)
                FROM pass_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                  AND status IN ('paid','redeemed')
                  AND created_at >= @sinceUtc";
            const string ticketSql = @"
                SELECT COUNT(*)
                FROM event_ticket_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                  AND status IN ('paid','redeemed')
                  AND created_at >= @sinceUtc";

            var passCount = requirementKind is "pass" or "any"
                ? await _db.ExecuteScalar(passSql, new { tenantId, userId, sinceUtc })
                : 0;
            var ticketCount = requirementKind is "event_ticket" or "any"
                ? await _db.ExecuteScalar(ticketSql, new { tenantId, userId, sinceUtc })
                : 0;
            return passCount + ticketCount;
        }
    }
}
