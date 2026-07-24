using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.WaiverData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class WaiverRepository : IWaiverRepository
    {
        private const string WaiverColumns = @"
            id, tenant_id AS TenantId, version, name, title, body,
            is_active AS IsActive,
            expires_at AS ExpiresAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public WaiverRepository(IDbHelper db) => _db = db;

        public async Task<TenantWaiver?> GetActive(Guid tenantId)
        {
            // Multi-waiver world: "active" is no longer guaranteed unique. Pick the
            // newest non-expired active row as the tenant's default fallback for
            // events that don't pin a specific waiver_id.
            var sql = $@"
                SELECT {WaiverColumns}
                FROM tenant_waiver
                WHERE tenant_id = @tenantId
                  AND is_active = true
                  AND (expires_at IS NULL OR expires_at > now())
                ORDER BY created_at DESC
                LIMIT 1";
            var result = await _db.Query<TenantWaiver>(sql, new { tenantId });
            return result.FirstOrDefault();
        }

        public async Task<List<TenantWaiver>> ListByTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {WaiverColumns}
                FROM tenant_waiver
                WHERE tenant_id = @tenantId
                ORDER BY is_active DESC, name, created_at DESC";
            var result = await _db.Query<TenantWaiver>(sql, new { tenantId });
            return result.ToList();
        }

        public async Task<TenantWaiver> Create(Guid tenantId, string name, string title, string body,
            bool isActive, DateTime? expiresAt)
        {
            // Every brand-new waiver starts at v1. Each waiver owns its own version
            // sequence — bumps would only happen if we add a "publish new version"
            // action later (we deliberately don't here so editing is in-place).
            const string sql = @"
                INSERT INTO tenant_waiver (tenant_id, version, name, title, body, is_active, expires_at)
                VALUES (@tenantId, 1, @name, @title, @body, @isActive, @expiresAt)
                RETURNING id, tenant_id AS TenantId, version, name, title, body,
                          is_active AS IsActive, expires_at AS ExpiresAt,
                          created_at AS CreatedAt, updated_at AS UpdatedAt";
            var result = await _db.Query<TenantWaiver>(sql,
                new { tenantId, name, title, body, isActive, expiresAt });
            return result.First();
        }

        public async Task Update(Guid id, Guid tenantId, string name, string title, string body,
            bool isActive, DateTime? expiresAt)
        {
            const string sql = @"
                UPDATE tenant_waiver SET
                    name = @name,
                    title = @title,
                    body = @body,
                    is_active = @isActive,
                    expires_at = @expiresAt
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, name, title, body, isActive, expiresAt });
        }

        public async Task<TenantWaiver?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {WaiverColumns} FROM tenant_waiver WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<TenantWaiver>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<TenantWaiver> PublishNewVersion(Guid tenantId, string title, string body)
        {
            // Multi-waiver world: this no longer auto-deactivates other waivers (the
            // partial unique index is gone). Treated as a thin wrapper around Create
            // for the legacy single-waiver admin endpoint.
            return await Create(tenantId, name: "Waiver", title: title, body: body,
                isActive: true, expiresAt: null);
        }

        private const string SignatureColumns = @"
            id, tenant_id AS TenantId, user_id AS UserId, waiver_id AS WaiverId,
            signed_at AS SignedAt, ip_address AS IpAddress,
            signature_data_url AS SignatureDataUrl,
            signed_by_parent AS SignedByParent,
            parent_name AS ParentName,
            parent_phone AS ParentPhone,
            signer_email AS SignerEmail,
            signer_name AS SignerName,
            spectator_first_name AS SpectatorFirstName,
            spectator_last_name AS SpectatorLastName,
            spectator_birthdate AS SpectatorBirthdate";

        public async Task<RiderWaiverSignature?> GetSignature(Guid userId, Guid waiverId)
        {
            var sql = $@"
                SELECT {SignatureColumns}
                FROM rider_waiver_signature
                WHERE user_id = @userId AND waiver_id = @waiverId
                LIMIT 1";
            var result = await _db.Query<RiderWaiverSignature>(sql, new { userId, waiverId });
            return result.FirstOrDefault();
        }

        public async Task<RiderWaiverSignature?> GetSignatureById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SignatureColumns}
                FROM rider_waiver_signature
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<RiderWaiverSignature>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<RiderWaiverSignature?> GetSignatureBySignerEmailForSelf(string email, Guid waiverId)
        {
            // Spectator email lookup: only counts a signature where the signer signed
            // for THEMSELVES (no child name on the row, and not signed_by_parent). The
            // buyer still needs to sign separately for each child they're bringing.
            var sql = $@"
                SELECT {SignatureColumns}
                FROM rider_waiver_signature
                WHERE waiver_id = @waiverId
                  AND lower(signer_email) = lower(@email)
                  AND signed_by_parent = false
                  AND spectator_first_name IS NULL
                ORDER BY signed_at DESC
                LIMIT 1";
            var result = await _db.Query<RiderWaiverSignature>(sql, new { email, waiverId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> SignSpectator(Guid tenantId, Guid waiverId, string? ipAddress,
            string signatureDataUrl, string signerEmail, string signerName,
            string spectatorFirstName, string spectatorLastName, DateTime? spectatorBirthdate,
            bool signedByParent, string? parentName, string? parentPhone)
        {
            const string sql = @"
                INSERT INTO rider_waiver_signature
                    (tenant_id, user_id, waiver_id, ip_address, signature_data_url,
                     signed_by_parent, parent_name, parent_phone,
                     signer_email, signer_name,
                     spectator_first_name, spectator_last_name, spectator_birthdate)
                VALUES (@tenantId, NULL, @waiverId, @ipAddress, @signatureDataUrl,
                        @signedByParent, @parentName, @parentPhone,
                        @signerEmail, @signerName,
                        @spectatorFirstName, @spectatorLastName, @spectatorBirthdate)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, new
            {
                tenantId, waiverId, ipAddress, signatureDataUrl,
                signedByParent, parentName, parentPhone,
                signerEmail, signerName,
                spectatorFirstName, spectatorLastName, spectatorBirthdate,
            });
            return result.First();
        }

        // Signature captured during event-ticket registration (a rider or spectator on a purchased
        // ticket), written to the shared rider_waiver_signature store so the "who has signed" report
        // and the check-in gate read one source of truth across sale paths. The attending person lands
        // in the generic (spectator_*) attendee columns; the signer (purchaser, or the parent for a
        // minor) is recorded separately. user_id stays NULL because registration attendees are
        // identified by name, not account, so there's no per-user uniqueness to enforce.
        public async Task<Guid> SignRegistrant(Guid tenantId, Guid waiverId, string? ipAddress,
            string signatureDataUrl, string? signerEmail, string? signerName,
            string attendeeFirstName, string attendeeLastName, DateTime? attendeeBirthdate,
            bool signedByParent, string? parentName, string? parentPhone)
        {
            const string sql = @"
                INSERT INTO rider_waiver_signature
                    (tenant_id, user_id, waiver_id, ip_address, signature_data_url,
                     signed_by_parent, parent_name, parent_phone,
                     signer_email, signer_name,
                     spectator_first_name, spectator_last_name, spectator_birthdate)
                VALUES (@tenantId, NULL, @waiverId, @ipAddress, @signatureDataUrl,
                        @signedByParent, @parentName, @parentPhone,
                        @signerEmail, @signerName,
                        @attendeeFirstName, @attendeeLastName, @attendeeBirthdate)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, new
            {
                tenantId, waiverId, ipAddress, signatureDataUrl,
                signedByParent, parentName, parentPhone,
                signerEmail, signerName,
                attendeeFirstName, attendeeLastName, attendeeBirthdate,
            });
            return result.First();
        }

        public async Task<Guid> Sign(Guid tenantId, Guid userId, Guid waiverId, string? ipAddress, string? signatureDataUrl,
            bool signedByParent, string? parentName, string? parentPhone)
        {
            // Don't overwrite an existing signature image on conflict — the original signature
            // is the legal artifact. Just refresh the timestamp.
            const string sql = @"
                INSERT INTO rider_waiver_signature
                    (tenant_id, user_id, waiver_id, ip_address, signature_data_url,
                     signed_by_parent, parent_name, parent_phone)
                VALUES (@tenantId, @userId, @waiverId, @ipAddress, @signatureDataUrl,
                        @signedByParent, @parentName, @parentPhone)
                ON CONFLICT (user_id, waiver_id) DO UPDATE SET signed_at = EXCLUDED.signed_at
                RETURNING id";
            var result = await _db.Query<Guid>(sql, new
            {
                tenantId, userId, waiverId, ipAddress, signatureDataUrl,
                signedByParent, parentName, parentPhone,
            });
            return result.First();
        }

        // Shared name/email/birthdate resolution for a signature row: rider account
        // fields when the signature has a user, else the spectator/registrant fields
        // captured at signing time.
        private const string SignerNameExpr = @"
            COALESCE(
                NULLIF(TRIM(COALESCE(u.first_name,'') || ' ' || COALESCE(u.last_name,'')), ''),
                NULLIF(TRIM(COALESCE(s.spectator_first_name,'') || ' ' || COALESCE(s.spectator_last_name,'')), ''),
                s.signer_name)";
        private const string SignerEmailExpr = "COALESCE(u.email, s.signer_email)";
        private const string BirthdateExpr = "COALESCE(u.birthdate, s.spectator_birthdate)";
        private const string WaiverIsCurrentExpr =
            "(w.is_active AND (w.expires_at IS NULL OR w.expires_at > now()))";

        public async Task<(List<WaiverSignatureRow> Rows, int Total)> ListSignatures(Guid tenantId,
            string? search, DateTime? fromUtc, DateTime? toUtc, Guid? waiverId,
            bool minorsOnly, string? context, int page, int pageSize, string? personKey = null)
        {
            var where = new List<string> { "s.tenant_id = @tenantId" };
            if (!string.IsNullOrWhiteSpace(search))
                where.Add($@"(lower({SignerNameExpr}) LIKE @search
                    OR lower(COALESCE({SignerEmailExpr}, '')) LIKE @search
                    OR lower(COALESCE(s.parent_name, '')) LIKE @search)");
            // Exact person filter for the merged Signed Waivers page: an expanded person row
            // lists exactly that person's signatures. The expression MUST stay identical to
            // ListPeople's person_key.
            if (!string.IsNullOrWhiteSpace(personKey))
                where.Add($@"COALESCE(s.user_id::text,
                    lower(COALESCE(
                        NULLIF(TRIM(COALESCE(s.spectator_first_name,'') || ' ' || COALESCE(s.spectator_last_name,'')), ''),
                        s.signer_name, s.signer_email, s.id::text))
                    || '|' || COALESCE({BirthdateExpr}::text, '')) = @personKey");
            if (fromUtc.HasValue) where.Add("s.signed_at >= @fromUtc");
            if (toUtc.HasValue) where.Add("s.signed_at < @toUtc");
            if (waiverId.HasValue) where.Add("s.waiver_id = @waiverId");
            if (minorsOnly) where.Add("s.signed_by_parent = true");
            switch (context)
            {
                case "ticket":
                    where.Add("EXISTS (SELECT 1 FROM event_ticket_purchase p WHERE p.waiver_signature_id = s.id AND p.tenant_id = s.tenant_id)");
                    break;
                case "rental":
                    where.Add("EXISTS (SELECT 1 FROM shop_rental_waiver rw WHERE rw.signature_id = s.id)");
                    break;
                case "account":
                    where.Add(@"NOT EXISTS (SELECT 1 FROM event_ticket_purchase p WHERE p.waiver_signature_id = s.id AND p.tenant_id = s.tenant_id)
                        AND NOT EXISTS (SELECT 1 FROM shop_rental_waiver rw WHERE rw.signature_id = s.id)");
                    break;
            }
            var whereSql = string.Join("\n  AND ", where);

            var args = new
            {
                tenantId,
                search = $"%{search?.Trim().ToLowerInvariant()}%",
                fromUtc,
                toUtc,
                waiverId,
                personKey,
                pageSize,
                offset = (page - 1) * pageSize,
            };

            var countSql = $@"
                SELECT COUNT(*)
                FROM rider_waiver_signature s
                LEFT JOIN users u ON u.id = s.user_id
                JOIN tenant_waiver w ON w.id = s.waiver_id
                WHERE {whereSql}";
            var total = (await _db.Query<int>(countSql, args)).First();

            var sql = $@"
                SELECT s.id,
                       s.signed_at AS SignedAt,
                       s.user_id AS UserId,
                       {SignerNameExpr} AS SignerName,
                       {SignerEmailExpr} AS SignerEmail,
                       {BirthdateExpr} AS Birthdate,
                       s.signed_by_parent AS SignedByParent,
                       s.parent_name AS ParentName,
                       s.parent_phone AS ParentPhone,
                       w.name AS WaiverName,
                       w.version AS WaiverVersion,
                       {WaiverIsCurrentExpr} AS WaiverIsCurrent,
                       EXISTS (SELECT 1 FROM event_ticket_purchase p WHERE p.waiver_signature_id = s.id AND p.tenant_id = s.tenant_id) AS FromTicket,
                       EXISTS (SELECT 1 FROM shop_rental_waiver rw WHERE rw.signature_id = s.id) AS FromRental
                FROM rider_waiver_signature s
                LEFT JOIN users u ON u.id = s.user_id
                JOIN tenant_waiver w ON w.id = s.waiver_id
                WHERE {whereSql}
                ORDER BY s.signed_at DESC
                LIMIT @pageSize OFFSET @offset";
            var rows = await _db.Query<WaiverSignatureRow>(sql, args);
            return (rows.ToList(), total);
        }

        public async Task<(List<WaiverPersonRow> Rows, int Total)> ListPeople(Guid tenantId,
            string? search, string? status, bool agingOut, bool minorsOnly, int page, int pageSize)
        {
            // Person identity: rider account id when the signature has one, else
            // lower(name)|birthdate so a walk-up's repeat visits collapse to one row.
            var baseCte = $@"
                WITH sigs AS (
                    SELECT s.user_id,
                           s.signed_at,
                           s.signed_by_parent,
                           s.parent_name,
                           s.parent_phone,
                           {SignerNameExpr} AS person_name,
                           {SignerEmailExpr} AS person_email,
                           {BirthdateExpr} AS birthdate,
                           {WaiverIsCurrentExpr} AS on_current_waiver,
                           COALESCE(s.user_id::text,
                               lower(COALESCE(
                                   NULLIF(TRIM(COALESCE(s.spectator_first_name,'') || ' ' || COALESCE(s.spectator_last_name,'')), ''),
                                   s.signer_name, s.signer_email, s.id::text))
                               || '|' || COALESCE({BirthdateExpr}::text, '')) AS person_key
                    FROM rider_waiver_signature s
                    LEFT JOIN users u ON u.id = s.user_id
                    JOIN tenant_waiver w ON w.id = s.waiver_id
                    WHERE s.tenant_id = @tenantId
                ),
                people AS (
                    SELECT person_key AS PersonKey,
                           (array_agg(user_id) FILTER (WHERE user_id IS NOT NULL))[1] AS UserId,
                           COALESCE((array_agg(person_name ORDER BY signed_at DESC) FILTER (WHERE person_name IS NOT NULL))[1], '(unnamed)') AS PersonName,
                           (array_agg(person_email ORDER BY signed_at DESC) FILTER (WHERE person_email IS NOT NULL))[1] AS PersonEmail,
                           MAX(birthdate) AS Birthdate,
                           bool_or(signed_by_parent) AS HasGuardianSignature,
                           (array_agg(parent_name ORDER BY signed_at DESC) FILTER (WHERE parent_name IS NOT NULL))[1] AS GuardianName,
                           (array_agg(parent_phone ORDER BY signed_at DESC) FILTER (WHERE parent_phone IS NOT NULL))[1] AS GuardianPhone,
                           MAX(signed_at) AS LastSignedAt,
                           COUNT(*)::int AS SignatureCount,
                           bool_or(on_current_waiver) AS HasCurrentWaiver
                    FROM sigs
                    GROUP BY person_key
                )";

            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                where.Add(@"(lower(PersonName) LIKE @search
                    OR lower(COALESCE(PersonEmail, '')) LIKE @search
                    OR lower(COALESCE(GuardianName, '')) LIKE @search)");
            if (status == "current") where.Add("HasCurrentWaiver");
            if (status == "outdated") where.Add("NOT HasCurrentWaiver");
            // Turns 18 within the next 90 days: their guardian-signed waiver is about to stop being valid.
            if (agingOut) where.Add(@"(Birthdate IS NOT NULL
                AND Birthdate + INTERVAL '18 years' > now()
                AND Birthdate + INTERVAL '18 years' <= now() + INTERVAL '90 days')");
            if (minorsOnly) where.Add(@"((Birthdate IS NOT NULL AND Birthdate + INTERVAL '18 years' > now())
                OR (Birthdate IS NULL AND HasGuardianSignature))");
            var whereSql = where.Count > 0 ? "WHERE " + string.Join("\n  AND ", where) : "";

            var args = new
            {
                tenantId,
                search = $"%{search?.Trim().ToLowerInvariant()}%",
                pageSize,
                offset = (page - 1) * pageSize,
            };

            var total = (await _db.Query<int>(
                $"{baseCte} SELECT COUNT(*) FROM people {whereSql}", args)).First();
            var rows = await _db.Query<WaiverPersonRow>($@"{baseCte}
                SELECT * FROM people
                {whereSql}
                ORDER BY LastSignedAt DESC
                LIMIT @pageSize OFFSET @offset", args);
            return (rows.ToList(), total);
        }

        public async Task<WaiverSignatureDetailRow?> GetSignatureDetail(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT s.id,
                       s.signed_at AS SignedAt,
                       s.user_id AS UserId,
                       {SignerNameExpr} AS SignerName,
                       {SignerEmailExpr} AS SignerEmail,
                       {BirthdateExpr} AS Birthdate,
                       s.signed_by_parent AS SignedByParent,
                       s.parent_name AS ParentName,
                       s.parent_phone AS ParentPhone,
                       s.ip_address AS IpAddress,
                       s.signature_data_url AS SignatureDataUrl,
                       w.name AS WaiverName,
                       w.title AS WaiverTitle,
                       w.version AS WaiverVersion,
                       u.emergency_contact_name AS EmergencyContactName,
                       u.emergency_contact_phone AS EmergencyContactPhone,
                       (SELECT e.title
                          FROM event_ticket_purchase p
                          JOIN event_ticket_tier t ON t.id = p.tier_id
                          JOIN event e ON e.id = t.event_id
                         WHERE p.waiver_signature_id = s.id AND p.tenant_id = s.tenant_id
                         ORDER BY p.created_at DESC LIMIT 1) AS TicketEventTitle,
                       (SELECT r.renter_name || ' - ' || to_char(r.starts_at, 'Mon DD, YYYY')
                          FROM shop_rental_waiver rw
                          JOIN shop_rental r ON r.id = rw.rental_id AND r.tenant_id = s.tenant_id
                         WHERE rw.signature_id = s.id
                         ORDER BY r.starts_at DESC LIMIT 1) AS RentalLabel
                FROM rider_waiver_signature s
                LEFT JOIN users u ON u.id = s.user_id
                JOIN tenant_waiver w ON w.id = s.waiver_id
                WHERE s.id = @id AND s.tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<WaiverSignatureDetailRow>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<List<WaiverComplianceRow>> ComplianceToday(Guid tenantId,
            DateTime dayStartUtc, DateTime dayEndUtc)
        {
            // EVERYONE EXPECTED TODAY, not just people who have already come through the
            // gate: every live ticket on an event running today (scanned or not), every
            // pass reservation on today's events, and every rental overlapping today.
            // Rows are annotated with the person's newest signature on a currently-active
            // waiver (SignedAt) so the screen can show when they signed, and with their
            // check-in moment when it has happened (CheckedInAt null = not here yet).
            // Tickets scanned today for an event on ANOTHER day (rare walk-up edge) are
            // kept via the redeemed_at leg of the ticket predicate.
            const string sql = @"
                WITH onsite AS (
                    SELECT CASE WHEN tet.code = 'lesson' THEN 'lesson' ELSE 'ticket' END AS Source,
                           e.title AS Label,
                           COALESCE(NULLIF(TRIM(COALESCE(p.rider_first_name,'') || ' ' || COALESCE(p.rider_last_name,'')), ''),
                                    p.purchaser_name, '(unknown)') AS PersonName,
                           p.purchaser_email AS Email, p.purchaser_user_id AS UserId,
                           COALESCE(p.redeemed_at_utc, e.starts_at) AS At,
                           p.redeemed_at_utc AS CheckedInAt,
                           (p.waiver_signature_id IS NOT NULL) AS SignedForThis,
                           (SELECT ws0.signed_at FROM rider_waiver_signature ws0
                             WHERE ws0.id = p.waiver_signature_id) AS OwnSignedAt
                    FROM event_ticket_purchase p
                    JOIN event_ticket_tier t ON t.id = p.tier_id
                    JOIN event e ON e.id = t.event_id
                    JOIN tenant_event_type tet ON tet.id = e.event_type_id
                    WHERE p.tenant_id = @tenantId AND p.status IN ('paid', 'redeemed')
                      AND ((e.starts_at < @dayEndUtc AND e.ends_at > @dayStartUtc)
                           OR (p.redeemed_at_utc >= @dayStartUtc AND p.redeemed_at_utc < @dayEndUtc))

                    UNION ALL
                    SELECT 'pass', e.title,
                           COALESCE(NULLIF(TRIM(COALESCE(sp.holder_first_name,'') || ' ' || COALESCE(sp.holder_last_name,'')), ''),
                                    sp.purchaser_name, '(unknown)'),
                           sp.purchaser_email, sp.purchaser_user_id,
                           COALESCE(r.checked_in_at, e.starts_at),
                           r.checked_in_at,
                           (sp.waiver_signature_id IS NOT NULL),
                           (SELECT ws0.signed_at FROM rider_waiver_signature ws0
                             WHERE ws0.id = sp.waiver_signature_id)
                    FROM season_pass_reservation r
                    JOIN season_pass_purchase sp ON sp.id = r.season_pass_purchase_id
                    JOIN event e ON e.id = r.event_id
                    WHERE sp.tenant_id = @tenantId
                      AND r.status <> 'cancelled'
                      AND ((e.starts_at < @dayEndUtc AND e.ends_at > @dayStartUtc)
                           OR (r.checked_in_at >= @dayStartUtc AND r.checked_in_at < @dayEndUtc))

                    UNION ALL
                    SELECT 'rental', 'Rental pickup',
                           COALESCE(r.renter_name, '(unnamed)'),
                           r.renter_email, NULL::uuid,
                           COALESCE(r.checked_out_at, r.starts_at),
                           r.checked_out_at,
                           ((SELECT COUNT(*) FROM shop_rental_waiver rw WHERE rw.rental_id = r.id)
                                >= GREATEST(COALESCE(r.riders_required, 1), 1)),
                           (SELECT MAX(ws0.signed_at) FROM shop_rental_waiver rw
                             JOIN rider_waiver_signature ws0 ON ws0.id = rw.signature_id
                             WHERE rw.rental_id = r.id)
                    FROM shop_rental r
                    WHERE r.tenant_id = @tenantId
                      AND r.status IN ('pending', 'paid', 'out')
                      AND r.starts_at < @dayEndUtc AND r.ends_at > @dayStartUtc
                )
                SELECT o.*,
                       COALESCE(o.OwnSignedAt, (
                           SELECT MAX(ws.signed_at)
                           FROM rider_waiver_signature ws
                           JOIN tenant_waiver tw ON tw.id = ws.waiver_id
                           WHERE ws.tenant_id = @tenantId
                             AND tw.is_active AND (tw.expires_at IS NULL OR tw.expires_at > now())
                             AND ((o.UserId IS NOT NULL AND ws.user_id = o.UserId)
                               OR (o.Email IS NOT NULL AND o.Email <> '' AND
                                    (lower(ws.signer_email) = lower(o.Email)
                                     OR ws.user_id IN (SELECT uu.id FROM users uu WHERE lower(uu.email) = lower(o.Email)))))
                       )) AS SignedAt
                FROM onsite o
                ORDER BY o.CheckedInAt DESC NULLS LAST, o.At";
            var rows = await _db.Query<WaiverComplianceRow>(sql, new { tenantId, dayStartUtc, dayEndUtc });
            return rows.ToList();
        }
    }
}
