using Services.Repositories.Data.RewardData;

namespace Services.Repositories.Interfaces
{
    public interface IRewardRepository
    {
        // Programs
        Task<List<RewardProgram>> ListProgramsForTenant(Guid tenantId, bool activeOnly);
        Task<RewardProgram?> GetProgram(Guid programId, Guid tenantId);
        Task<Guid> CreateProgram(RewardProgram p);
        Task UpdateProgram(RewardProgram p);
        Task DeleteProgram(Guid programId, Guid tenantId);

        // Enrollments
        Task<RewardEnrollment?> GetEnrollment(Guid programId, Guid userId);
        Task<List<RewardEnrollment>> ListEnrollmentsForUser(Guid userId);
        Task<Guid> CreateEnrollment(Guid programId, Guid userId);
        Task DeleteEnrollment(Guid programId, Guid userId);
        Task UpdateLastProximityEmailedAtCount(Guid enrollmentId, int count);

        // Redemptions
        Task<List<RewardRedemption>> ListRedemptionsForUser(Guid userId, bool unredeemedOnly);
        Task<List<RewardRedemption>> ListRedemptionsForProgram(Guid programId);
        Task<RewardRedemption?> GetRedemption(Guid redemptionId);
        Task<Guid> CreateRedemption(Guid programId, Guid userId);
        Task MarkRedemptionUsed(Guid redemptionId, string kind, Guid sourceId);

        // Progress
        /// <summary>
        /// How many qualifying purchases this user has paid for since enrollment, scoped by
        /// requirement_kind. For passes we sum quantity; for tickets we count rows.
        /// </summary>
        Task<int> CountQualifyingPurchases(Guid tenantId, Guid userId, string requirementKind, DateTime sinceUtc);
    }
}
