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
            e.title AS EventTitle,
            q.event_ticket_purchase_id AS EventTicketPurchaseId,
            q.season_pass_purchase_id AS SeasonPassPurchaseId,
            q.rider_user_id AS RiderUserId, q.rider_first_name AS RiderFirstName,
            q.rider_last_name AS RiderLastName, q.rider_birthdate AS RiderBirthdate,
            q.recipient_user_id AS RecipientUserId";
        private const string RequestJoins = @"
            FROM waiver_sign_request q
            LEFT JOIN tenant_waiver w ON w.id = q.waiver_id
            LEFT JOIN event e ON e.id = q.event_id";

        private readonly IDbHelper _db;

        public WaiverSignRequestRepository(IDbHelper db) => _db = db;

        public async Task<WaiverSignRequestRow> Create(Guid tenantId, Guid? waiverId, string token,
            string recipientEmail, string? recipientName, Guid? eventId, Guid? requestedByUserId,
            Guid? eventTicketPurchaseId = null, Guid? seasonPassPurchaseId = null, SignRequestRider? rider = null,
            Guid? recipientUserId = null)
        {
            // The ticket / pass targets (Script0304) are tenant-checked in the insert itself: a
            // target from another tenant is stored as NULL rather than linked. The rider identity
            // (Script0306) is what an adult-waiver link signs AS.
            const string sql = @"
                WITH ins AS (
                    INSERT INTO waiver_sign_request
                        (tenant_id, waiver_id, token, recipient_email, recipient_name,
                         event_id, requested_by_user_id, event_ticket_purchase_id, season_pass_purchase_id,
                         rider_user_id, rider_first_name, rider_last_name, rider_birthdate, recipient_user_id)
                    VALUES (@tenantId, @waiverId, @token, @recipientEmail, @recipientName,
                            @eventId, @requestedByUserId,
                            (SELECT id FROM event_ticket_purchase WHERE id = @eventTicketPurchaseId AND tenant_id = @tenantId),
                            (SELECT id FROM season_pass_purchase WHERE id = @seasonPassPurchaseId AND tenant_id = @tenantId),
                            @riderUserId, @riderFirstName, @riderLastName, @riderBirthdate::date,
                            (SELECT id FROM users WHERE id = @recipientUserId AND (tenant_id IS NULL OR tenant_id = @tenantId)))
                    RETURNING *
                )
                SELECT ins.id, ins.tenant_id AS TenantId, ins.waiver_id AS WaiverId, ins.token,
                       ins.recipient_email AS RecipientEmail, ins.recipient_name AS RecipientName,
                       ins.event_id AS EventId, ins.status, ins.signature_id AS SignatureId,
                       ins.created_at AS CreatedAt, ins.sent_at AS SentAt,
                       ins.opened_at AS OpenedAt, ins.signed_at AS SignedAt,
                       w.name AS WaiverName, w.version AS WaiverVersion,
                       e.title AS EventTitle,
                       ins.event_ticket_purchase_id AS EventTicketPurchaseId,
                       ins.season_pass_purchase_id AS SeasonPassPurchaseId,
                       ins.rider_user_id AS RiderUserId, ins.rider_first_name AS RiderFirstName,
                       ins.rider_last_name AS RiderLastName, ins.rider_birthdate AS RiderBirthdate,
                       ins.recipient_user_id AS RecipientUserId
                FROM ins
                LEFT JOIN tenant_waiver w ON w.id = ins.waiver_id
                LEFT JOIN event e ON e.id = ins.event_id";
            var result = await _db.Query<WaiverSignRequestRow>(sql, new
            {
                tenantId, waiverId, token,
                recipientEmail = recipientEmail.Trim(),
                recipientName,
                eventId, requestedByUserId,
                eventTicketPurchaseId, seasonPassPurchaseId,
                riderUserId = rider?.UserId,
                riderFirstName = rider?.FirstName,
                riderLastName = rider?.LastName,
                riderBirthdate = rider?.Birthdate,
                recipientUserId,
            });
            return result.First();
        }

        public async Task<List<WaiverSignRequestRow>> LatestForTickets(Guid tenantId, IReadOnlyCollection<Guid> ticketIds)
        {
            if (ticketIds.Count == 0) return new List<WaiverSignRequestRow>();
            var sql = $@"
                SELECT DISTINCT ON (q.event_ticket_purchase_id) {RequestColumns}
                {RequestJoins}
                WHERE q.tenant_id = @tenantId
                  AND q.event_ticket_purchase_id = ANY(@ids)
                  AND q.status <> 'cancelled'
                ORDER BY q.event_ticket_purchase_id, q.created_at DESC";
            return (await _db.Query<WaiverSignRequestRow>(sql, new { tenantId, ids = ticketIds.ToArray() })).ToList();
        }

        public async Task<HashSet<Guid>> RidersWithOpenRequests(Guid tenantId, IReadOnlyCollection<Guid> riderUserIds)
        {
            if (riderUserIds.Count == 0) return new HashSet<Guid>();
            const string sql = @"
                SELECT DISTINCT rider_user_id
                FROM waiver_sign_request
                WHERE tenant_id = @tenantId
                  AND rider_user_id = ANY(@ids)
                  AND status IN ('pending', 'sent', 'opened')";
            return (await _db.Query<Guid>(sql, new { tenantId, ids = riderUserIds.ToArray() })).ToHashSet();
        }

        public async Task<List<RiderCredentialRef>> UpcomingCredentialsForAccount(Guid tenantId, Guid userId)
        {
            // What the gate will check next for this account: paid tickets for events not yet over,
            // and paid passes still in season, soonest first. The caller keeps only the ones whose
            // rider / holder IS the account owner.
            const string sql = @"
                SELECT 'ticket' AS Kind, p.id AS Id, e.id AS EventId, e.starts_at AS SortAt,
                       p.rider_first_name AS FirstName, p.rider_last_name AS LastName
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id AND t.tenant_id = p.tenant_id
                JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                WHERE p.tenant_id = @tenantId AND p.purchaser_user_id = @userId
                  AND p.status = 'paid' AND e.ends_at >= now()
                UNION ALL
                SELECT 'season_pass', sp.id, NULL, sp.valid_from_date::timestamptz,
                       sp.holder_first_name, sp.holder_last_name
                FROM season_pass_purchase sp
                WHERE sp.tenant_id = @tenantId AND sp.purchaser_user_id = @userId
                  AND sp.status = 'paid' AND sp.valid_to_date >= current_date
                ORDER BY 4
                LIMIT 20";
            return (await _db.Query<RiderCredentialRef>(sql, new { tenantId, userId })).ToList();
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
