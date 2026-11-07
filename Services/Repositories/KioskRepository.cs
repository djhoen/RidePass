using Services.Helpers.Interfaces;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class KioskRepository : IKioskRepository
    {
        private const string DeviceColumns = @"
            id, tenant_id AS TenantId, name, credential_hash AS CredentialHash,
            pairing_code_hash AS PairingCodeHash, pairing_expires_at AS PairingExpiresAt, paired_at AS PairedAt,
            last_seen_at AS LastSeenAt, revoked_at AS RevokedAt, revoked_by_user_id AS RevokedByUserId,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string SignupColumns = @"
            s.id, s.tenant_id AS TenantId, s.kiosk_device_id AS KioskDeviceId, s.user_id AS UserId,
            s.dependent_id AS DependentId, s.guardian_user_id AS GuardianUserId,
            s.first_name AS FirstName, s.last_name AS LastName, s.birthdate AS Birthdate, s.is_minor AS IsMinor,
            s.signature_id AS SignatureId, s.photo_id AS PhotoId, s.created_at AS CreatedAt,
            s.handled_at AS HandledAt, s.handled_by_user_id AS HandledByUserId, s.sale_ref AS SaleRef,
            s.dismissed_at AS DismissedAt, s.dismissed_by_user_id AS DismissedByUserId";

        private readonly IDbHelper _db;

        public KioskRepository(IDbHelper db) => _db = db;

        public async Task<KioskDevice> CreateDevice(Guid tenantId, string name, string pairingCodeHash,
            DateTime pairingExpiresAtUtc, Guid? createdByUserId)
        {
            var sql = $@"
                INSERT INTO kiosk_device (tenant_id, name, pairing_code_hash, pairing_expires_at, created_by_user_id)
                VALUES (@tenantId, @name, @pairingCodeHash, @pairingExpiresAtUtc, @createdByUserId)
                RETURNING {DeviceColumns}";
            return (await _db.Query<KioskDevice>(sql, new { tenantId, name, pairingCodeHash, pairingExpiresAtUtc, createdByUserId })).First();
        }

        public async Task<List<KioskDevice>> ListDevices(Guid tenantId)
        {
            var sql = $"SELECT {DeviceColumns} FROM kiosk_device WHERE tenant_id = @tenantId ORDER BY revoked_at IS NOT NULL, lower(name)";
            return (await _db.Query<KioskDevice>(sql, new { tenantId })).ToList();
        }

        public async Task<KioskDevice?> GetDevice(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {DeviceColumns} FROM kiosk_device WHERE id = @id AND tenant_id = @tenantId";
            return (await _db.Query<KioskDevice>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<bool> RenameDevice(Guid id, Guid tenantId, string name) =>
            await _db.Execute("UPDATE kiosk_device SET name = @name WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId, name }) == 1;

        public async Task<bool> RevokeDevice(Guid id, Guid tenantId, Guid? byUserId) =>
            await _db.Execute(@"
                UPDATE kiosk_device
                   SET revoked_at = now(), revoked_by_user_id = @byUserId,
                       pairing_code_hash = NULL, pairing_expires_at = NULL
                 WHERE id = @id AND tenant_id = @tenantId AND revoked_at IS NULL",
                new { id, tenantId, byUserId }) == 1;

        public async Task<bool> SetPairingCode(Guid id, Guid tenantId, string pairingCodeHash, DateTime expiresAtUtc) =>
            await _db.Execute(@"
                UPDATE kiosk_device SET pairing_code_hash = @pairingCodeHash, pairing_expires_at = @expiresAtUtc
                 WHERE id = @id AND tenant_id = @tenantId AND revoked_at IS NULL",
                new { id, tenantId, pairingCodeHash, expiresAtUtc }) == 1;

        public async Task<KioskDevice?> RedeemPairingCode(Guid tenantId, string pairingCodeHash, string credentialHash)
        {
            var sql = $@"
                UPDATE kiosk_device
                   SET credential_hash = @credentialHash, pairing_code_hash = NULL, pairing_expires_at = NULL,
                       paired_at = now(), last_seen_at = now()
                 WHERE tenant_id = @tenantId AND pairing_code_hash = @pairingCodeHash
                   AND pairing_expires_at > now() AND revoked_at IS NULL
                RETURNING {DeviceColumns}";
            return (await _db.Query<KioskDevice>(sql, new { tenantId, pairingCodeHash, credentialHash })).FirstOrDefault();
        }

        public async Task<KioskDevice?> GetByCredential(Guid tenantId, string credentialHash)
        {
            var sql = $@"SELECT {DeviceColumns} FROM kiosk_device
                         WHERE tenant_id = @tenantId AND credential_hash = @credentialHash AND revoked_at IS NULL";
            return (await _db.Query<KioskDevice>(sql, new { tenantId, credentialHash })).FirstOrDefault();
        }

        public Task TouchLastSeen(Guid id, Guid tenantId) => _db.Execute(@"
            UPDATE kiosk_device SET last_seen_at = now()
             WHERE id = @id AND tenant_id = @tenantId
               AND (last_seen_at IS NULL OR last_seen_at < now() - interval '1 minute')",
            new { id, tenantId });

        public async Task<Guid> CreateSignup(KioskSignup s)
        {
            const string sql = @"
                INSERT INTO kiosk_signup
                    (tenant_id, kiosk_device_id, user_id, dependent_id, guardian_user_id, first_name, last_name,
                     birthdate, is_minor, signature_id, photo_id)
                VALUES (@TenantId, @KioskDeviceId, @UserId, @DependentId, @GuardianUserId, @FirstName, @LastName,
                        @Birthdate::date, @IsMinor, @SignatureId, @PhotoId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, s)).First();
        }

        public async Task<KioskSignup?> GetSignup(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {SignupColumns} FROM kiosk_signup s WHERE s.id = @id AND s.tenant_id = @tenantId";
            return (await _db.Query<KioskSignup>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<List<KioskSignupRow>> ListSignups(Guid tenantId, DateTime sinceUtc, int take)
        {
            var sql = $@"
                SELECT {SignupColumns},
                       d.name AS KioskName, u.email AS RiderEmail,
                       g.first_name AS GuardianFirstName, g.last_name AS GuardianLastName, g.email AS GuardianEmail,
                       p.taken_at AS PhotoTakenAt
                FROM kiosk_signup s
                LEFT JOIN kiosk_device d ON d.id = s.kiosk_device_id AND d.tenant_id = s.tenant_id
                LEFT JOIN users u ON u.id = s.user_id
                LEFT JOIN users g ON g.id = s.guardian_user_id
                LEFT JOIN rider_photo p ON p.id = s.photo_id AND p.tenant_id = s.tenant_id
                WHERE s.tenant_id = @tenantId AND s.created_at >= @sinceUtc
                ORDER BY s.created_at DESC
                LIMIT @take";
            return (await _db.Query<KioskSignupRow>(sql, new { tenantId, sinceUtc, take })).ToList();
        }

        public async Task<int> MarkHandled(Guid tenantId, IReadOnlyCollection<Guid> ids, Guid? byUserId, string saleRef)
        {
            if (ids.Count == 0) return 0;
            return await _db.Execute(@"
                UPDATE kiosk_signup SET handled_at = now(), handled_by_user_id = @byUserId, sale_ref = @saleRef
                 WHERE tenant_id = @tenantId AND id = ANY(@ids) AND handled_at IS NULL",
                new { tenantId, ids = ids.ToArray(), byUserId, saleRef });
        }

        public async Task<bool> Dismiss(Guid id, Guid tenantId, Guid? byUserId) =>
            await _db.Execute(@"
                UPDATE kiosk_signup SET dismissed_at = now(), dismissed_by_user_id = @byUserId
                 WHERE id = @id AND tenant_id = @tenantId AND dismissed_at IS NULL",
                new { id, tenantId, byUserId }) == 1;
    }
}
