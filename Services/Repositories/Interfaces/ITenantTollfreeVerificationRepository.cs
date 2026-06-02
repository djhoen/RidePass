using Services.Repositories.Data.SmsData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Read/write access to a tenant's Toll-Free Verification submission
    /// state. One row per tenant (PK = tenant_id), so all calls are scoped
    /// by passing the tenantId; there's no list-across-tenants surface.
    /// </summary>
    public interface ITenantTollfreeVerificationRepository
    {
        /// <summary>
        /// Load the verification row for a tenant. Returns null when the
        /// admin has never opened the verification form for this tenant.
        /// </summary>
        Task<TenantTollfreeVerification?> Get(Guid tenantId);

        /// <summary>
        /// Upsert the editable fields (business info, use case, opt-in,
        /// volume). Status / TwilioVerificationSid are NOT written here —
        /// those belong to the submit + status-refresh paths so that a
        /// "save draft" can't accidentally clobber a real Twilio state.
        /// </summary>
        Task Upsert(TenantTollfreeVerification verification);

        /// <summary>
        /// Record the verification SID + initial status returned from a
        /// successful submission. Sets last_submitted_at_utc to now.
        /// </summary>
        Task SetSubmitted(Guid tenantId, string twilioVerificationSid, string status);

        /// <summary>
        /// Refresh the lifecycle fields after a status poll. Clears
        /// rejection_reason when status is no longer a *_REJECTED variant.
        /// </summary>
        Task SetStatus(Guid tenantId, string status, string? rejectionReason);
    }
}
