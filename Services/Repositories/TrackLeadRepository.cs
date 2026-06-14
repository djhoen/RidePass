using Services.Helpers.Interfaces;
using Services.Repositories.Data.LeadData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TrackLeadRepository : ITrackLeadRepository
    {
        private readonly IDbHelper _db;
        public TrackLeadRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Create(TrackLead lead)
        {
            const string sql = @"
                INSERT INTO track_lead
                    (contact_name, track_name, email, phone, message, ip_address, user_agent)
                VALUES
                    (@ContactName, @TrackName, @Email, @Phone, @Message, @IpAddress, @UserAgent)
                RETURNING id";
            return (await _db.Query<Guid>(sql, lead)).First();
        }
    }
}
