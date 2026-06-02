using Services.Repositories.Data.MessagingData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Per-tenant SMS opt-out list. Written by the inbound webhook when a
    /// customer texts STOP/START; read by the outbound SMS path to suppress
    /// sends to numbers that have opted out, and by the admin Inbox UI to
    /// surface "opted out" beside conversations.
    /// </summary>
    public interface ITenantSmsOptOutRepository
    {
        /// <summary>
        /// Hot-path check used by every outbound send. Returns true only when
        /// an active opt-out exists for (tenant, phone). Phone must be
        /// already-normalized E.164 — callers shouldn't pass raw user input.
        /// </summary>
        Task<bool> IsOptedOut(Guid tenantId, string phone);

        /// <summary>
        /// Record (or refresh) an opt-out for (tenant, phone). Upsert: a row
        /// that was previously opted-in flips back to opted-out and stamps
        /// opted_out_at_utc. Idempotent — repeated STOPs from the same number
        /// don't create duplicate rows.
        /// </summary>
        Task RecordOptOut(Guid tenantId, string phone, string keyword);

        /// <summary>
        /// Record an opt-in (customer texted START/UNSTOP/YES). Upsert: if no
        /// prior row exists this creates one with opted_out=false; if a row
        /// exists it clears the suppression and stamps opted_in_at_utc.
        /// </summary>
        Task RecordOptIn(Guid tenantId, string phone, string keyword);

        /// <summary>
        /// Admin-list read: all opt-out rows for a tenant, newest activity
        /// first. Includes opted-in rows too so admins can see the full
        /// history (a customer who STOPped then STARTed is still relevant
        /// context). Tenant-scoped.
        /// </summary>
        Task<List<TenantSmsOptOut>> ListForTenant(Guid tenantId, int take = 500);
    }
}
