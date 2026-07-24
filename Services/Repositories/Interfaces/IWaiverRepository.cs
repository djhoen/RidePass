using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.WaiverData;

namespace Services.Repositories.Interfaces
{
    public interface IWaiverRepository
    {
        /// <summary>Tenant default fallback (newest non-expired active row).</summary>
        Task<TenantWaiver?> GetActive(Guid tenantId);
        Task<TenantWaiver?> GetById(Guid id, Guid tenantId);
        Task<List<TenantWaiver>> ListByTenant(Guid tenantId);
        Task<TenantWaiver> Create(Guid tenantId, string name, string title, string body,
            bool isActive, DateTime? expiresAt);
        Task Update(Guid id, Guid tenantId, string name, string title, string body,
            bool isActive, DateTime? expiresAt);
        Task<TenantWaiver> PublishNewVersion(Guid tenantId, string title, string body);
        Task<RiderWaiverSignature?> GetSignature(Guid userId, Guid waiverId);

        /// <summary>Tenant-scoped fetch of one signature row (with its image), for showing gate
        /// staff the signature a given ticket is backed by.</summary>
        Task<RiderWaiverSignature?> GetSignatureById(Guid id, Guid tenantId);
        Task<Guid> Sign(Guid tenantId, Guid userId, Guid waiverId, string? ipAddress, string? signatureDataUrl,
            bool signedByParent, string? parentName, string? parentPhone);

        /// <summary>Email lookup for guest spectator buyers — checks whether this
        /// email has already signed THIS waiver for themselves (not on behalf of a child).</summary>
        Task<RiderWaiverSignature?> GetSignatureBySignerEmailForSelf(string email, Guid waiverId);

        /// <summary>Captures a guest spectator signature with full attendee details.
        /// One row per attending spectator — purchaser signs for themselves once,
        /// then again for each minor on the same purchase.</summary>
        Task<Guid> SignSpectator(Guid tenantId, Guid waiverId, string? ipAddress,
            string signatureDataUrl, string signerEmail, string signerName,
            string spectatorFirstName, string spectatorLastName, DateTime? spectatorBirthdate,
            bool signedByParent, string? parentName, string? parentPhone);

        /// <summary>Signature captured during event-ticket registration (rider or spectator), written
        /// to the shared rider_waiver_signature store. Returns the new signature row id.</summary>
        Task<Guid> SignRegistrant(Guid tenantId, Guid waiverId, string? ipAddress,
            string signatureDataUrl, string? signerEmail, string? signerName,
            string attendeeFirstName, string attendeeLastName, DateTime? attendeeBirthdate,
            bool signedByParent, string? parentName, string? parentPhone);

        /// <summary>Admin Signed Waivers log: newest first, server-paged.
        /// <paramref name="context"/> filters by signing context: "ticket", "rental", or
        /// "account" (a bare account/kiosk signature with no purchase link).</summary>
        Task<(List<WaiverSignatureRow> Rows, int Total)> ListSignatures(Guid tenantId,
            string? search, DateTime? fromUtc, DateTime? toUtc, Guid? waiverId,
            bool minorsOnly, string? context, int page, int pageSize, string? personKey = null);

        /// <summary>Admin People view: signatures collapsed to person identities
        /// (rider account when present, else name + birthdate). Status filter:
        /// "current" / "outdated". agingOut = minors turning 18 within 90 days.</summary>
        Task<(List<WaiverPersonRow> Rows, int Total)> ListPeople(Guid tenantId,
            string? search, string? status, bool agingOut, bool minorsOnly, int page, int pageSize);

        /// <summary>Full detail for one signature (image, waiver, signing context) for the
        /// admin drill-in / print view.</summary>
        Task<WaiverSignatureDetailRow?> GetSignatureDetail(Guid id, Guid tenantId);

        /// <summary>Compliance Today: everyone on site in the window (ticket scans, season
        /// pass check-ins, active rentals, today's lesson rosters) with their waiver status.</summary>
        Task<List<WaiverComplianceRow>> ComplianceToday(Guid tenantId, DateTime dayStartUtc, DateTime dayEndUtc);
    }
}
