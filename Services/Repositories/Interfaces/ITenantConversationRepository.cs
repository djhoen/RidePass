using Services.Repositories.Data.MessagingData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Read/write access to per-tenant SMS conversations and the messages
    /// inside them. Both the inbound webhook (creating new threads on first
    /// text from a customer) and the admin Inbox UI (listing + replying)
    /// go through this single repository.
    /// </summary>
    public interface ITenantConversationRepository
    {
        // ── Conversations ────────────────────────────────────────────────────

        /// <summary>
        /// Find an existing conversation for (tenant, customerPhone) or create
        /// one if none exists. Used by the inbound webhook on first text from
        /// a phone we haven't seen before. Returns the resolved row in both
        /// the create and the existing-match case.
        /// </summary>
        Task<TenantConversation> FindOrCreate(Guid tenantId, string customerPhone, Guid? customerUserId);

        Task<TenantConversation?> GetById(Guid id, Guid tenantId);

        /// <summary>
        /// Conversation list for the admin Inbox: most recent activity first,
        /// optionally excluding archived. Always tenant-scoped.
        /// </summary>
        Task<List<TenantConversation>> ListForTenant(Guid tenantId, int take = 100, bool includeArchived = false);

        /// <summary>
        /// Same shape as <see cref="ListForTenant"/> but joins the opt-out list
        /// so the admin Inbox can flag conversations whose customer has texted
        /// STOP. One round-trip instead of N+1 per-row lookups. The OptedOut
        /// flag is sticky-true only when an active opt-out exists right now —
        /// a customer who STOPped then STARTed comes back as false.
        /// </summary>
        Task<List<ConversationListRow>> ListForTenantWithOptOut(Guid tenantId, int take = 100, bool includeArchived = false);

        /// <summary>
        /// Stamp last_read_at = now for this conversation so it drops off the
        /// unread list. Tenant-scoped via the WHERE clause.
        /// </summary>
        Task MarkRead(Guid conversationId, Guid tenantId);

        Task SetStatus(Guid conversationId, Guid tenantId, string status);

        // ── Messages ─────────────────────────────────────────────────────────

        /// <summary>
        /// Append a message to a conversation. Updates the parent's
        /// last_message_at_utc, and last_inbound_at_utc for inbound messages,
        /// in a single transaction so the conversation row stays consistent.
        /// </summary>
        Task<Guid> AppendMessage(TenantMessage message);

        Task<List<TenantMessage>> ListForConversation(Guid conversationId, Guid tenantId, int take = 200);

        /// <summary>
        /// Update an outbound message's status when its Twilio StatusCallback
        /// fires (queued → sent → delivered, or → failed/undelivered).
        /// Looks up by twilio_message_sid which is globally unique within Twilio.
        /// </summary>
        Task UpdateStatusBySid(string twilioMessageSid, string status, int? numSegments,
            string? errorCode, string? errorMessage);
    }
}
