using Services.Repositories.Data.LeadData;

namespace Services.Repositories.Interfaces
{
    public interface ITrackLeadRepository
    {
        Task<Guid> Create(TrackLead lead);
    }
}
