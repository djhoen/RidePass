using Services.Repositories.Data.FeedbackData;

namespace Services.Repositories.Interfaces
{
    public interface ITrackFeedbackRepository
    {
        Task<Guid> Create(TrackFeedback feedback);
        Task<TrackFeedback?> GetById(Guid id, Guid tenantId);
        Task<List<TrackFeedback>> ListByTenant(Guid tenantId, string? statusFilter, int limit, int offset);
        Task<int> CountByTenant(Guid tenantId, string? statusFilter);
        Task UpdateStatus(Guid id, Guid tenantId, string status, string? adminNotes, Guid actionedByUserId);
    }
}
