using Services.Helpers.Interfaces;
using Services.Repositories.Data.WaiverData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class WaiverSignRequestRepository : IWaiverSignRequestRepository
    {
        // Joins carry the display fields the admin list shows; both are optional links.
        private const string RequestColumns = @"
            q.id, q.tenant_id AS TenantId, q.waiver_id AS WaiverId, q.token,
            q.recipient_email AS RecipientEmail, q.recipient_name AS RecipientName,
            q.event_id AS EventId, q.status, q.signature_id AS SignatureId,
            q.created_at AS CreatedAt, q.sent_at AS SentAt,
            q.opened_at AS OpenedAt, q.signed_at AS SignedAt,
            w.name AS WaiverName, w.version AS WaiverVersion,
            e.title AS EventTitle";
        private const string RequestJoins = @"
            FROM waiver_sign_request q
            LEFT JOIN tenant_waiver w ON w.id = q.waiver_id
            LEFT JOIN event e ON e.id = q.event_id";

        private readonly IDbHelper _db;

        public WaiverSignRequestRepository(IDbHelper db) => _db = db;

        public async Task<WaiverSignRequestRow> Create(Guid tenantId, Guid? waiverId, string token,
            string recipientEmail, string? recipientName, Guid? eventId, Guid? requestedByUserId)
        {
            const string sql = @"
                WITH ins AS (
                    INSERT INTO waiver_sign_request
                        (tenant_id, waiver_id, token, recipient_email, recipient_name,
                         event_id, requested_by_user_id)
                    VALUES (@tenantId, @waiverId, @token, @recipientEmail, @recipientName,
                            @eventId, @requestedByUserId)
                    RETURNING *
                )
                SELECT ins.id, ins.tenant_id AS TenantId, ins.waiver_id AS WaiverId, ins.token,
                       ins.recipient_email AS RecipientEmail, ins.recipient_name AS RecipientName,
                       ins.event_id AS EventId, ins.status, ins.signature_id AS SignatureId,
                       ins.created_at AS CreatedAt, ins.sent_at AS SentAt,
                       ins.opened_at AS OpenedAt, ins.signed_at AS SignedAt,
                       w.name AS WaiverName, w.version AS WaiverVersion,
                       e.title AS EventTitle
                FROM ins
                LEFT JOIN tenant_waiver w ON w.id = ins.waiver_id
                LEFT JOIN event e ON e.id = ins.event_id";
            var result = await _db.Query<WaiverSignRequestRow>(sql, new
            {
                tenantId, waiverId, token,
                recipientEmail = recipientEmail.Trim(),
                recipientName,
                eventId, requestedByUserId,
            });
            return result.First();
        }

        public async Task<(List<WaiverSignRequestRow> Rows, int Total)> List(Guid tenantId,
            string? search, string? status, int page, int pageSize)
        {
            var where = new List<string> { "q.tenant_id = @tenantId" };
            if (!string.IsNullOrWhiteSpace(search))
                where.Add(@"(lower(q.recipient_email) LIKE @search
                    OR lower(COALESCE(q.recipient_name, '')) LIKE @search)");
            if (!string.IsNullOrWhiteSpace(status)) where.Add("q.status = @status");
            var whereSql = string.Join("\n  AND ", where);
            var args = new
            {
                tenantId,
                search = $"%{search?.Trim().ToLowerInvariant()}%",
                status,
                pageSize,
                offset = (page - 1) * pageSize,
            };
            var total = (await _db.Query<int>(
                $"SELECT COUNT(*) FROM waiver_sign_request q WHERE {whereSql}", args)).First();
            var rows = await _db.Query<WaiverSignRequestRow>($@"
                SELECT {RequestColumns}
                {RequestJoins}
                WHERE {whereSql}
                ORDER BY q.created_at DESC
                LIMIT @pageSize OFFSET @offset", args);
            return (rows.ToList(), total);
        }

        public async Task<WaiverSignRequestRow?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"SELECT {RequestColumns} {RequestJoins}
                WHERE q.id = @id AND q.tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<WaiverSignRequestRow>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<WaiverSignRequestRow?> GetByToken(string token)
        {
            var sql = $@"SELECT {RequestColumns} {RequestJoins}
                WHERE q.token = @token LIMIT 1";
            return (await _db.Query<WaiverSignRequestRow>(sql, new { token })).FirstOrDefault();
        }

        public Task MarkSent(Guid id, Guid tenantId) => _db.Execute(@"
            UPDATE waiver_sign_request
               SET status = 'sent', sent_at = now()
             WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending', 'sent', 'opened')",
            new { id, tenantId });

        public Task MarkOpened(Guid id, Guid tenantId) => _db.Execute(@"
            UPDATE waiver_sign_request
               SET status = 'opened', opened_at = COALESCE(opened_at, now())
             WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending', 'sent')",
            new { id, tenantId });

        public Task MarkSigned(Guid id, Guid tenantId, Guid signatureId) => _db.Execute(@"
            UPDATE waiver_sign_request
               SET status = 'signed', signed_at = now(), signature_id = @signatureId
             WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending', 'sent', 'opened')",
            new { id, tenantId, signatureId });

        public Task Cancel(Guid id, Guid tenantId) => _db.Execute(@"
            UPDATE waiver_sign_request
               SET status = 'cancelled'
             WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending', 'sent', 'opened')",
            new { id, tenantId });

        public async Task<List<WaiverRequestCandidate>> CandidatesForEvent(Guid eventId, Guid tenantId)
        {
            // Paid ticket holders on the event, minus anyone whose purchase already carries a
            // signature, anyone with a signature on a currently active waiver (by account or
            // email), and anyone with a request still in flight. Deduped by email.
            const string sql = @"
                SELECT DISTINCT ON (lower(p.purchaser_email))
                       p.purchaser_email AS Email,
                       COALESCE(
                           NULLIF(TRIM(COALESCE(p.rider_first_name,'') || ' ' || COALESCE(p.rider_last_name,'')), ''),
                           p.purchaser_name) AS Name
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = @eventId
                  AND p.status = 'paid'
                  AND p.purchaser_email IS NOT NULL AND p.purchaser_email <> ''
                  AND p.waiver_signature_id IS NULL
                  AND NOT EXISTS (
                        SELECT 1
                        FROM rider_waiver_signature ws
                        JOIN tenant_waiver tw ON tw.id = ws.waiver_id
                        WHERE ws.tenant_id = @tenantId
                          AND tw.is_active AND (tw.expires_at IS NULL OR tw.expires_at > now())
                          AND ((p.purchaser_user_id IS NOT NULL AND ws.user_id = p.purchaser_user_id)
                            OR lower(ws.signer_email) = lower(p.purchaser_email)
                            OR ws.user_id IN (SELECT uu.id FROM users uu WHERE lower(uu.email) = lower(p.purchaser_email))))
                  AND NOT EXISTS (
                        SELECT 1 FROM waiver_sign_request q
                        WHERE q.tenant_id = @tenantId
                          AND lower(q.recipient_email) = lower(p.purchaser_email)
                          AND q.status IN ('pending', 'sent', 'opened'))
                ORDER BY lower(p.purchaser_email), p.created_at DESC";
            var rows = await _db.Query<WaiverRequestCandidate>(sql, new { eventId, tenantId });
            return rows.ToList();
        }

        public async Task<int> CountRosterEmails(Guid eventId, Guid tenantId)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT lower(p.purchaser_email))
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = @eventId
                  AND p.status = 'paid'
                  AND p.purchaser_email IS NOT NULL AND p.purchaser_email <> ''";
            return (await _db.Query<int>(sql, new { eventId, tenantId })).First();
        }
    }
}
