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
            last_seen_at AS LastSeenAt, last_poll_at AS LastPollAt, revoked_at AS RevokedAt, revoked_by_user_id AS RevokedByUserId,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string SignupColumns = @"
            s.id, s.tenant_id AS TenantId, s.kiosk_device_id AS KioskDeviceId, s.user_id AS UserId,
            s.dependent_id AS DependentId, s.guardian_user_id AS GuardianUserId,
            s.first_name AS FirstName, s.last_name AS LastName, s.birthdate AS Birthdate, s.is_minor AS IsMinor,
            s.signature_id AS SignatureId, s.photo_id AS PhotoId, s.created_at AS CreatedAt,
            s.handled_at AS HandledAt, s.handled_by_user_id AS HandledByUserId, s.sale_ref AS SaleRef,
            s.sale_ticket_id AS SaleTicketId,
            s.dismissed_at AS DismissedAt, s.dismissed_by_user_id AS DismissedByUserId,
            s.match_pending AS MatchPending, s.matched_user_id AS MatchedUserId,
            s.matched_guardian_user_id AS MatchedGuardianUserId, s.email AS Email, s.phone AS Phone,
            s.guardian_first_name AS GuardianFirstName, s.guardian_last_name AS GuardianLastName,
            s.guardian_email AS GuardianEmail, s.guardian_phone AS GuardianPhone, s.guardian_birthdate AS GuardianBirthdate,
            s.emergency_contact_name AS EmergencyContactName, s.emergency_contact_phone AS EmergencyContactPhone,
            s.held_signature_data_url AS HeldSignatureDataUrl, s.held_waiver_id AS HeldWaiverId,
            s.held_signed_at AS HeldSignedAt, s.confirmed_at AS ConfirmedAt, s.confirmed_by_user_id AS ConfirmedByUserId";

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
                     birthdate, is_minor, signature_id, photo_id,
                     match_pending, matched_user_id, matched_guardian_user_id, email, phone,
                     guardian_first_name, guardian_last_name, guardian_email, guardian_phone, guardian_birthdate,
                     emergency_contact_name, emergency_contact_phone,
                     held_signature_data_url, held_waiver_id, held_signed_at)
                VALUES (@TenantId, @KioskDeviceId, @UserId, @DependentId, @GuardianUserId, @FirstName, @LastName,
                        @Birthdate::date, @IsMinor, @SignatureId, @PhotoId,
                        @MatchPending, @MatchedUserId, @MatchedGuardianUserId, @Email, @Phone,
                        @GuardianFirstName, @GuardianLastName, @GuardianEmail, @GuardianPhone, @GuardianBirthdate::date,
                        @EmergencyContactName, @EmergencyContactPhone,
                        @HeldSignatureDataUrl, @HeldWaiverId, @HeldSignedAt)
                RETURNING id";
            return (await _db.Query<Guid>(sql, s)).First();
        }

        public async Task<KioskSignup?> GetSignup(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {SignupColumns} FROM kiosk_signup s WHERE s.id = @id AND s.tenant_id = @tenantId";
            return (await _db.Query<KioskSignup>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public Task SetPhoto(Guid id, Guid tenantId, Guid? photoId) =>
            _db.Execute("UPDATE kiosk_signup SET photo_id = @photoId WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId, photoId });

        public async Task<List<KioskSignupRow>> ListSignups(Guid tenantId, DateTime sinceUtc, int take)
        {
            // A card sale's sign-ups count as handled once its ticket is paid (the payment webhook flips it).
            await _db.Execute(@"
                UPDATE kiosk_signup k SET handled_at = now()
                  FROM event_ticket_purchase p
                 WHERE k.tenant_id = @tenantId AND k.handled_at IS NULL AND k.sale_ticket_id = p.id
                   AND p.tenant_id = @tenantId AND p.status IN ('paid', 'redeemed')", new { tenantId });
            var sql = $@"
                SELECT {SignupColumns},
                       d.name AS KioskName, u.email AS RiderEmail,
                       g.first_name AS GuardianAccountFirstName, g.last_name AS GuardianAccountLastName,
                       g.email AS GuardianAccountEmail,
                       p.taken_at AS PhotoTakenAt,
                       mu.first_name AS MatchedFirstName, mu.last_name AS MatchedLastName,
                       mg.first_name AS MatchedGuardianFirstName, mg.last_name AS MatchedGuardianLastName
                FROM kiosk_signup s
                LEFT JOIN kiosk_device d ON d.id = s.kiosk_device_id AND d.tenant_id = s.tenant_id
                LEFT JOIN users u ON u.id = s.user_id
                LEFT JOIN users g ON g.id = s.guardian_user_id
                LEFT JOIN users mu ON mu.id = s.matched_user_id
                LEFT JOIN users mg ON mg.id = s.matched_guardian_user_id
                LEFT JOIN rider_photo p ON p.id = s.photo_id AND p.tenant_id = s.tenant_id
                WHERE s.tenant_id = @tenantId AND s.created_at >= @sinceUtc
                ORDER BY s.created_at DESC
                LIMIT @take";
            return (await _db.Query<KioskSignupRow>(sql, new { tenantId, sinceUtc, take })).ToList();
        }

        public async Task<int> SetPendingSale(Guid tenantId, IReadOnlyCollection<Guid> ids, Guid saleTicketId, Guid? byUserId, string saleRef)
        {
            if (ids.Count == 0) return 0;
            return await _db.Execute(@"
                UPDATE kiosk_signup SET sale_ticket_id = @saleTicketId, sale_ref = @saleRef, handled_by_user_id = @byUserId
                 WHERE tenant_id = @tenantId AND id = ANY(@ids) AND handled_at IS NULL",
                new { tenantId, ids = ids.ToArray(), saleTicketId, byUserId, saleRef });
        }

        public async Task<int> MarkHandled(Guid tenantId, IReadOnlyCollection<Guid> ids, Guid? byUserId, string saleRef)
        {
            if (ids.Count == 0) return 0;
            return await _db.Execute(@"
                UPDATE kiosk_signup SET handled_at = now(), handled_by_user_id = @byUserId, sale_ref = @saleRef
                 WHERE tenant_id = @tenantId AND id = ANY(@ids) AND handled_at IS NULL",
                new { tenantId, ids = ids.ToArray(), byUserId, saleRef });
        }

        public async Task<bool> Confirm(Guid id, Guid tenantId, Guid? userId, Guid? dependentId, Guid? guardianUserId,
            Guid? signatureId, Guid? photoId, Guid? byUserId) =>
            await _db.Execute(@"
                UPDATE kiosk_signup
                   SET match_pending = false, confirmed_at = now(), confirmed_by_user_id = @byUserId,
                       user_id = @userId, dependent_id = @dependentId, guardian_user_id = @guardianUserId,
                       signature_id = @signatureId, photo_id = @photoId,
                       held_signature_data_url = NULL
                 WHERE id = @id AND tenant_id = @tenantId AND match_pending",
                new { id, tenantId, userId, dependentId, guardianUserId, signatureId, photoId, byUserId }) == 1;

        public async Task<bool> Dismiss(Guid id, Guid tenantId, Guid? byUserId) =>
            await _db.Execute(@"
                UPDATE kiosk_signup SET dismissed_at = now(), dismissed_by_user_id = @byUserId,
                       held_signature_data_url = NULL
                 WHERE id = @id AND tenant_id = @tenantId AND dismissed_at IS NULL",
                new { id, tenantId, byUserId }) == 1;

        // ── Pushed steps (Script0311) ────────────────────────────────────────────────────────────

        private const string PushColumns = @"
            id, tenant_id AS TenantId, kiosk_device_id AS KioskDeviceId, created_by_user_id AS CreatedByUserId,
            created_at AS CreatedAt, expires_at AS ExpiresAt, want_waiver AS WantWaiver, want_photo AS WantPhoto,
            target_kind AS TargetKind, event_ticket_purchase_id AS EventTicketPurchaseId,
            season_pass_purchase_id AS SeasonPassPurchaseId, event_id AS EventId, kiosk_signup_id AS KioskSignupId,
            sign_mode AS SignMode, waiver_id AS WaiverId, rider_first_name AS RiderFirstName, status,
            started_at AS StartedAt, completed_at AS CompletedAt, cancelled_at AS CancelledAt,
            cancelled_by_user_id AS CancelledByUserId, result_signature_id AS ResultSignatureId,
            result_photo_id AS ResultPhotoId";

        /// <summary>Requests past their expiry are expired (lazily, whenever pushes are read).</summary>
        private Task ExpireStale(Guid tenantId) => _db.Execute(@"
            UPDATE kiosk_push SET status = 'expired'
             WHERE tenant_id = @tenantId AND status IN ('pending', 'in_progress') AND expires_at <= now()",
            new { tenantId });

        public async Task<KioskPush?> CreatePush(KioskPush p)
        {
            await ExpireStale(p.TenantId);
            var sql = $@"
                INSERT INTO kiosk_push
                    (tenant_id, kiosk_device_id, created_by_user_id, expires_at, want_waiver, want_photo, target_kind,
                     event_ticket_purchase_id, season_pass_purchase_id, event_id, kiosk_signup_id,
                     sign_mode, waiver_id, rider_first_name)
                VALUES (@TenantId, @KioskDeviceId, @CreatedByUserId, @ExpiresAt, @WantWaiver, @WantPhoto, @TargetKind,
                        @EventTicketPurchaseId, @SeasonPassPurchaseId, @EventId, @KioskSignupId,
                        @SignMode, @WaiverId, @RiderFirstName)
                ON CONFLICT (kiosk_device_id) WHERE status IN ('pending', 'in_progress', 'completing') DO NOTHING
                RETURNING {PushColumns}";
            return (await _db.Query<KioskPush>(sql, p)).FirstOrDefault();
        }

        public async Task<KioskPush?> GetPush(Guid id, Guid tenantId)
        {
            await ExpireStale(tenantId);
            var sql = $"SELECT {PushColumns} FROM kiosk_push WHERE id = @id AND tenant_id = @tenantId";
            return (await _db.Query<KioskPush>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<KioskPush?> GetActivePush(Guid tenantId, Guid kioskDeviceId)
        {
            await ExpireStale(tenantId);
            var sql = $@"SELECT {PushColumns} FROM kiosk_push
                         WHERE tenant_id = @tenantId AND kiosk_device_id = @kioskDeviceId
                           AND status IN ('pending', 'in_progress', 'completing')
                         LIMIT 1";
            return (await _db.Query<KioskPush>(sql, new { tenantId, kioskDeviceId })).FirstOrDefault();
        }

        public async Task<bool> StartPush(Guid id, Guid tenantId, Guid kioskDeviceId) =>
            await _db.Execute(@"
                UPDATE kiosk_push SET status = 'in_progress', started_at = COALESCE(started_at, now())
                 WHERE id = @id AND tenant_id = @tenantId AND kiosk_device_id = @kioskDeviceId
                   AND status IN ('pending', 'in_progress') AND expires_at > now()",
                new { id, tenantId, kioskDeviceId }) == 1;

        public async Task<KioskPush?> ClaimPush(Guid id, Guid tenantId, Guid kioskDeviceId)
        {
            var sql = $@"
                UPDATE kiosk_push SET status = 'completing'
                 WHERE id = @id AND tenant_id = @tenantId AND kiosk_device_id = @kioskDeviceId
                   AND status IN ('pending', 'in_progress') AND expires_at > now()
                RETURNING {PushColumns}";
            return (await _db.Query<KioskPush>(sql, new { id, tenantId, kioskDeviceId })).FirstOrDefault();
        }

        public Task FinishPush(Guid id, Guid tenantId, Guid? signatureId, Guid? photoId) => _db.Execute(@"
            UPDATE kiosk_push SET status = 'done', completed_at = now(),
                   result_signature_id = @signatureId, result_photo_id = @photoId
             WHERE id = @id AND tenant_id = @tenantId AND status = 'completing'",
            new { id, tenantId, signatureId, photoId });

        public Task ReleasePush(Guid id, Guid tenantId) => _db.Execute(@"
            UPDATE kiosk_push SET status = 'in_progress'
             WHERE id = @id AND tenant_id = @tenantId AND status = 'completing'",
            new { id, tenantId });

        public async Task<bool> CancelPush(Guid id, Guid tenantId, Guid? byUserId) =>
            await _db.Execute(@"
                UPDATE kiosk_push SET status = 'cancelled', cancelled_at = now(), cancelled_by_user_id = @byUserId
                 WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending', 'in_progress')",
                new { id, tenantId, byUserId }) == 1;

        public Task TouchPoll(Guid kioskDeviceId, Guid tenantId) => _db.Execute(
            "UPDATE kiosk_device SET last_poll_at = now() WHERE id = @kioskDeviceId AND tenant_id = @tenantId",
            new { kioskDeviceId, tenantId });
    }
}
