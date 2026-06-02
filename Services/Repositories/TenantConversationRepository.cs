using Services.Helpers.Interfaces;
using Services.Repositories.Data.MessagingData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantConversationRepository : ITenantConversationRepository
    {
        private const string ConversationColumns = @"
            id, tenant_id AS TenantId, customer_phone AS CustomerPhone,
            customer_user_id AS CustomerUserId,
            last_message_at_utc AS LastMessageAt,
            last_inbound_at_utc AS LastInboundAt,
            last_read_at_utc AS LastReadAt,
            status, created_at_utc AS CreatedAt";

        private const string MessageColumns = @"
            id, conversation_id AS ConversationId, tenant_id AS TenantId,
            direction, body, twilio_message_sid AS TwilioMessageSid,
            status, num_segments AS NumSegments,
            sent_by_user_id AS SentByUserId,
            error_code AS ErrorCode, error_message AS ErrorMessage,
            created_at_utc AS CreatedAt";

        private readonly IDbHelper _db;

        public TenantConversationRepository(IDbHelper db) => _db = db;

        // ── Conversations ────────────────────────────────────────────────────

        public async Task<TenantConversation> FindOrCreate(Guid tenantId, string customerPhone, Guid? customerUserId)
        {
            // INSERT ... ON CONFLICT DO UPDATE returns the existing row when
            // the unique (tenant_id, customer_phone) hits. The DO UPDATE is a
            // no-op (setting status=status) so we get RETURNING in both cases.
            var sql = $@"
                INSERT INTO tenant_conversation (tenant_id, customer_phone, customer_user_id)
                VALUES (@tenantId, @customerPhone, @customerUserId)
                ON CONFLICT (tenant_id, customer_phone) DO UPDATE
                    SET customer_user_id = COALESCE(tenant_conversation.customer_user_id, EXCLUDED.customer_user_id)
                RETURNING {ConversationColumns}";
            var result = await _db.Query<TenantConversation>(sql, new { tenantId, customerPhone, customerUserId });
            return result.First();
        }

        public async Task<TenantConversation?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {ConversationColumns}
                FROM tenant_conversation
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<TenantConversation>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<List<TenantConversation>> ListForTenant(Guid tenantId, int take = 100, bool includeArchived = false)
        {
            var sql = $@"
                SELECT {ConversationColumns}
                FROM tenant_conversation
                WHERE tenant_id = @tenantId
                  AND (@includeArchived OR status = 'active')
                ORDER BY last_message_at_utc DESC
                LIMIT @take";
            return (await _db.Query<TenantConversation>(sql, new { tenantId, take, includeArchived })).ToList();
        }

        public async Task<List<ConversationListRow>> ListForTenantWithOptOut(Guid tenantId, int take = 100, bool includeArchived = false)
        {
            // Two LEFT JOINs:
            //   • tenant_sms_opt_out, keyed on (tenant_id, phone), gives the
            //     OptedOut flag. Both sides scoped by tenant_id so a stray
            //     opt-out row from another tenant can't leak in.
            //   • users, keyed on customer_user_id, gives the customer's name
            //     when the inbound webhook successfully matched their phone to
            //     a user. Users is global (no tenant_id) by design — the same
            //     rider can text from multiple tenants and the name comes
            //     along regardless.
            var sql = $@"
                SELECT
                    c.id, c.tenant_id AS TenantId, c.customer_phone AS CustomerPhone,
                    c.customer_user_id AS CustomerUserId,
                    c.last_message_at_utc AS LastMessageAt,
                    c.last_inbound_at_utc AS LastInboundAt,
                    c.last_read_at_utc AS LastReadAt,
                    c.status, c.created_at_utc AS CreatedAt,
                    COALESCE(o.opted_out, false) AS OptedOut,
                    u.first_name AS CustomerFirstName,
                    u.last_name AS CustomerLastName
                FROM tenant_conversation c
                LEFT JOIN tenant_sms_opt_out o
                    ON o.tenant_id = c.tenant_id AND o.phone = c.customer_phone
                LEFT JOIN users u
                    ON u.id = c.customer_user_id
                WHERE c.tenant_id = @tenantId
                  AND (@includeArchived OR c.status = 'active')
                ORDER BY c.last_message_at_utc DESC
                LIMIT @take";
            return (await _db.Query<ConversationListRow>(sql, new { tenantId, take, includeArchived })).ToList();
        }

        public async Task MarkRead(Guid conversationId, Guid tenantId)
        {
            const string sql = @"
                UPDATE tenant_conversation
                SET last_read_at_utc = now()
                WHERE id = @conversationId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { conversationId, tenantId });
        }

        public async Task SetStatus(Guid conversationId, Guid tenantId, string status)
        {
            const string sql = @"
                UPDATE tenant_conversation
                SET status = @status
                WHERE id = @conversationId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { conversationId, tenantId, status });
        }

        // ── Messages ─────────────────────────────────────────────────────────

        public async Task<Guid> AppendMessage(TenantMessage message)
        {
            // Insert message AND update the parent conversation's timestamps
            // in one round trip via a CTE. The conversation update is guarded
            // by tenant_id so a tampered message.TenantId can't write to a
            // different tenant's conversation row.
            const string sql = @"
                WITH inserted AS (
                    INSERT INTO tenant_message
                        (conversation_id, tenant_id, direction, body,
                         twilio_message_sid, status, num_segments,
                         sent_by_user_id, error_code, error_message)
                    VALUES
                        (@ConversationId, @TenantId, @Direction, @Body,
                         @TwilioMessageSid, @Status, @NumSegments,
                         @SentByUserId, @ErrorCode, @ErrorMessage)
                    RETURNING id, direction, created_at_utc
                ),
                upd AS (
                    UPDATE tenant_conversation c
                    SET last_message_at_utc = inserted.created_at_utc,
                        last_inbound_at_utc = CASE
                            WHEN inserted.direction = 'inbound' THEN inserted.created_at_utc
                            ELSE c.last_inbound_at_utc
                        END
                    FROM inserted
                    WHERE c.id = @ConversationId AND c.tenant_id = @TenantId
                    RETURNING 1
                )
                SELECT id FROM inserted";
            return (await _db.Query<Guid>(sql, message)).First();
        }

        public async Task<List<TenantMessage>> ListForConversation(Guid conversationId, Guid tenantId, int take = 200)
        {
            // Tenant scope enforced via tenant_id on the message row (denormalized
            // for exactly this reason — no join to conversation needed).
            var sql = $@"
                SELECT {MessageColumns}
                FROM tenant_message
                WHERE conversation_id = @conversationId AND tenant_id = @tenantId
                ORDER BY created_at_utc
                LIMIT @take";
            return (await _db.Query<TenantMessage>(sql, new { conversationId, tenantId, take })).ToList();
        }

        public async Task UpdateStatusBySid(string twilioMessageSid, string status, int? numSegments,
            string? errorCode, string? errorMessage)
        {
            // Twilio Message SIDs are globally unique within Twilio, so this
            // lookup is safe without tenant scope. Called only from the
            // StatusCallback webhook where the message identity comes from
            // Twilio itself, not from user input.
            const string sql = @"
                UPDATE tenant_message
                SET status = @status,
                    num_segments = COALESCE(@numSegments, num_segments),
                    error_code = COALESCE(@errorCode, error_code),
                    error_message = COALESCE(@errorMessage, error_message)
                WHERE twilio_message_sid = @twilioMessageSid";
            await _db.Execute(sql, new { twilioMessageSid, status, numSegments, errorCode, errorMessage });
        }
    }
}
