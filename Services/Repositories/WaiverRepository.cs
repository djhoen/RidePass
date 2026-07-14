using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
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
    }
}
