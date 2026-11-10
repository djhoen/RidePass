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
            // Handled is computed here, not written on read: stamped by a cash / free sale (handled_at), or the sale
            // the sign-up was sold on is paid (a card sale: its ticket, else its season pass or membership).
            var columns = SignupColumns.Replace("s.handled_at AS HandledAt", @"COALESCE(s.handled_at,
                       CASE WHEN st.status IN ('paid', 'redeemed') THEN st.updated_at
                            WHEN sp.status = 'paid' THEN sp.updated_at
                            WHEN sm.status = 'paid' THEN sm.updated_at END) AS HandledAt");
            var sql = $@"
                SELECT {columns},
                       d.name AS KioskName, u.email AS RiderEmail,
                       g.first_name AS GuardianAccountFirstName, g.last_name AS GuardianAccountLastName,
                       g.email AS GuardianAccountEmail,
                       p.taken_at AS PhotoTakenAt,
                       mu.first_name AS MatchedFirstName, mu.last_name AS MatchedLastName, mu.birthdate AS MatchedBirthdate,
                       mg.first_name AS MatchedGuardianFirstName, mg.last_name AS MatchedGuardianLastName,
                       mg.birthdate AS MatchedGuardianBirthdate
                FROM kiosk_signup s
                LEFT JOIN kiosk_device d ON d.id = s.kiosk_device_id AND d.tenant_id = s.tenant_id
                LEFT JOIN users u ON u.id = s.user_id
                LEFT JOIN users g ON g.id = s.guardian_user_id
                LEFT JOIN users mu ON mu.id = s.matched_user_id
                LEFT JOIN users mg ON mg.id = s.matched_guardian_user_id
                LEFT JOIN event_ticket_purchase st ON st.id = s.sale_ticket_id AND st.tenant_id = s.tenant_id
                LEFT JOIN season_pass_purchase sp ON sp.id = s.sale_pass_purchase_id AND sp.tenant_id = s.tenant_id
                LEFT JOIN membership_purchase sm ON sm.id = s.sale_membership_purchase_id AND sm.tenant_id = s.tenant_id
                LEFT JOIN rider_photo p ON p.id = s.photo_id AND p.tenant_id = s.tenant_id
                WHERE s.tenant_id = @tenantId AND s.created_at >= @sinceUtc
                ORDER BY s.created_at DESC
                LIMIT @take";
            return (await _db.Query<KioskSignupRow>(sql, new { tenantId, sinceUtc, take })).ToList();
        }

        public async Task<int> SetPendingSale(Guid tenantId, IReadOnlyCollection<Guid> ids, Guid? saleTicketId, Guid? salePassId,
            Guid? saleMembershipId, Guid? byUserId, string saleRef)
        {
            if (ids.Count == 0) return 0;
            return await _db.Execute(@"
                UPDATE kiosk_signup SET sale_ticket_id = @saleTicketId, sale_pass_purchase_id = @salePassId,
                       sale_membership_purchase_id = @saleMembershipId, sale_ref = @saleRef, handled_by_user_id = @byUserId
                 WHERE tenant_id = @tenantId AND id = ANY(@ids) AND handled_at IS NULL",
                new { tenantId, ids = ids.ToArray(), saleTicketId, salePassId, saleMembershipId, byUserId, saleRef });
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
                 WHERE id = @id AND tenant_id = @tenantId AND NOT match_pending AND confirmed_at IS NULL",
                new { id, tenantId, userId, dependentId, guardianUserId, signatureId, photoId, byUserId }) == 1;

        public async Task<bool> ClaimForConfirm(Guid id, Guid tenantId) =>
            await _db.Execute(@"
                UPDATE kiosk_signup SET match_pending = false
                 WHERE id = @id AND tenant_id = @tenantId AND match_pending AND dismissed_at IS NULL AND confirmed_at IS NULL",
                new { id, tenantId }) == 1;

        public Task ReleaseConfirm(Guid id, Guid tenantId) => _db.Execute(@"
            UPDATE kiosk_signup SET match_pending = true
             WHERE id = @id AND tenant_id = @tenantId AND NOT match_pending AND confirmed_at IS NULL AND user_id IS NULL
               AND dependent_id IS NULL",
            new { id, tenantId });

        public async Task<bool> Dismiss(Guid id, Guid tenantId, Guid? byUserId) =>
            await _db.Execute(@"
                UPDATE kiosk_signup SET dismissed_at = now(), dismissed_by_user_id = @byUserId,
                       held_signature_data_url = NULL
                 WHERE id = @id AND tenant_id = @tenantId AND dismissed_at IS NULL",
                new { id, tenantId, byUserId }) == 1;

        // ── Pushed steps (Script0311) ────────────────────────────────────────────────────────────
        // Time is applied when a request is read, never by sweeping the tenant: a pending / on-screen request
        // past expires_at reads as expired, and a 'completing' one whose claim is older than the lease reads as
        // on screen again (or expired, past expires_at), so a crashed completion never holds the kiosk.
        // Only CreatePush writes those states back, for the one kiosk it inserts for (to free the unique slot).

        private const string Lease = "interval '2 minutes'"; // KioskPush.ClaimLease

        private const string EffectiveStatus = @"
            CASE WHEN p.status IN ('pending', 'in_progress') AND p.expires_at <= now() THEN 'expired'
                 WHEN p.status = 'completing' AND p.claimed_at <= now() - " + Lease + @"
                      THEN CASE WHEN p.expires_at <= now() THEN 'expired' ELSE 'in_progress' END
                 ELSE p.status END";

        /// <summary>Holds its kiosk: waiting or on screen before expiry, or claimed within the lease.</summary>
        private const string IsOpen = @"
            ((p.status IN ('pending', 'in_progress') AND p.expires_at > now())
             OR (p.status = 'completing' AND (p.claimed_at > now() - " + Lease + @" OR p.expires_at > now())))";

        /// <summary>The kiosk may (re)take it, or staff / the kiosk may end it: waiting or on screen before
        /// expiry, or a stale claim before expiry. A live claim (a save in flight) is never interrupted.</summary>
        private const string IsClaimable = @"
            p.expires_at > now()
            AND (p.status IN ('pending', 'in_progress')
                 OR (p.status = 'completing' AND p.claimed_at <= now() - " + Lease + @"))";

        private const string PushColumns = @"
            p.id, p.tenant_id AS TenantId, p.kiosk_device_id AS KioskDeviceId, p.created_by_user_id AS CreatedByUserId,
            p.created_at AS CreatedAt, p.expires_at AS ExpiresAt, p.want_waiver AS WantWaiver, p.want_photo AS WantPhoto,
            p.target_kind AS TargetKind, p.event_ticket_purchase_id AS EventTicketPurchaseId,
            p.photo_ticket_purchase_id AS PhotoTicketPurchaseId,
            p.season_pass_purchase_id AS SeasonPassPurchaseId, p.event_id AS EventId, p.kiosk_signup_id AS KioskSignupId,
            p.sign_mode AS SignMode, p.waiver_id AS WaiverId, p.rider_first_name AS RiderFirstName,
            " + EffectiveStatus + @" AS Status,
            p.started_at AS StartedAt, p.completed_at AS CompletedAt, p.cancelled_at AS CancelledAt,
            p.cancelled_by_user_id AS CancelledByUserId, p.result_signature_id AS ResultSignatureId,
            p.result_photo_id AS ResultPhotoId, p.claimed_at AS ClaimedAt, p.claim_id AS ClaimId, p.end_reason AS EndReason";

        /// <summary>Only for the kiosk's open request: whether its waiver still exists and has not expired.</summary>
        private const string WaiverUsable = @"
            (NOT p.want_waiver OR EXISTS (SELECT 1 FROM tenant_waiver w
                WHERE w.id = p.waiver_id AND w.tenant_id = p.tenant_id
                  AND (w.expires_at IS NULL OR w.expires_at > now()))) AS WaiverUsable";

        public async Task<KioskPush?> CreatePush(KioskPush p)
        {
            // Settle this kiosk's lapsed request (if any) so it stops holding the one-open-request slot.
            await _db.Execute(@"
                UPDATE kiosk_push p SET status = CASE WHEN p.expires_at <= now() THEN 'expired' ELSE 'in_progress' END
                 WHERE p.tenant_id = @TenantId AND p.kiosk_device_id = @KioskDeviceId
                   AND ((p.status IN ('pending', 'in_progress') AND p.expires_at <= now())
                        OR (p.status = 'completing' AND p.claimed_at <= now() - " + Lease + "))",
                new { p.TenantId, p.KioskDeviceId });
            var sql = $@"
                INSERT INTO kiosk_push AS p
                    (tenant_id, kiosk_device_id, created_by_user_id, expires_at, want_waiver, want_photo, target_kind,
                     event_ticket_purchase_id, photo_ticket_purchase_id, season_pass_purchase_id, event_id, kiosk_signup_id,
                     sign_mode, waiver_id, rider_first_name)
                VALUES (@TenantId, @KioskDeviceId, @CreatedByUserId, @ExpiresAt, @WantWaiver, @WantPhoto, @TargetKind,
                        @EventTicketPurchaseId, @PhotoTicketPurchaseId, @SeasonPassPurchaseId, @EventId, @KioskSignupId,
                        @SignMode, @WaiverId, @RiderFirstName)
                ON CONFLICT (kiosk_device_id) WHERE status IN ('pending', 'in_progress', 'completing') DO NOTHING
                RETURNING {PushColumns}";
            return (await _db.Query<KioskPush>(sql, p)).FirstOrDefault();
        }

        public async Task<KioskPush?> GetPush(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {PushColumns} FROM kiosk_push p WHERE p.id = @id AND p.tenant_id = @tenantId";
            return (await _db.Query<KioskPush>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<KioskPush?> GetActivePush(Guid tenantId, Guid kioskDeviceId)
        {
            var sql = $@"SELECT {PushColumns}, {WaiverUsable} FROM kiosk_push p
                         WHERE p.tenant_id = @tenantId AND p.kiosk_device_id = @kioskDeviceId
                           AND p.status IN ('pending', 'in_progress', 'completing') AND {IsOpen}
                         LIMIT 1";
            return (await _db.Query<KioskPush>(sql, new { tenantId, kioskDeviceId })).FirstOrDefault();
        }

        public async Task<List<KioskPush>> ListOpenPushes(Guid tenantId)
        {
            var sql = $@"SELECT {PushColumns} FROM kiosk_push p
                         WHERE p.tenant_id = @tenantId
                           AND p.status IN ('pending', 'in_progress', 'completing') AND {IsOpen}";
            return (await _db.Query<KioskPush>(sql, new { tenantId })).ToList();
        }

        public async Task<bool> StartPush(Guid id, Guid tenantId, Guid kioskDeviceId) =>
            await _db.Execute(@"
                UPDATE kiosk_push p SET status = 'in_progress', started_at = COALESCE(p.started_at, now())
                 WHERE p.id = @id AND p.tenant_id = @tenantId AND p.kiosk_device_id = @kioskDeviceId
                   AND " + IsClaimable, new { id, tenantId, kioskDeviceId }) == 1;

        public async Task<KioskPush?> ClaimPush(Guid id, Guid tenantId, Guid kioskDeviceId)
        {
            var sql = $@"
                UPDATE kiosk_push p SET status = 'completing', claimed_at = now(), claim_id = uuid_generate_v4()
                 WHERE p.id = @id AND p.tenant_id = @tenantId AND p.kiosk_device_id = @kioskDeviceId AND {IsClaimable}
                RETURNING {PushColumns}";
            return (await _db.Query<KioskPush>(sql, new { id, tenantId, kioskDeviceId })).FirstOrDefault();
        }

        public async Task<bool> RecordPushSignature(Guid id, Guid tenantId, Guid claimId, Guid signatureId) =>
            await _db.Execute(@"
                UPDATE kiosk_push SET result_signature_id = @signatureId
                 WHERE id = @id AND tenant_id = @tenantId AND status = 'completing' AND claim_id = @claimId",
                new { id, tenantId, claimId, signatureId }) == 1;

        public async Task<bool> RecordPushPhoto(Guid id, Guid tenantId, Guid claimId, Guid photoId) =>
            await _db.Execute(@"
                UPDATE kiosk_push SET result_photo_id = @photoId
                 WHERE id = @id AND tenant_id = @tenantId AND status = 'completing' AND claim_id = @claimId",
                new { id, tenantId, claimId, photoId }) == 1;

        public async Task<bool> FinishPush(Guid id, Guid tenantId, Guid claimId) =>
            await _db.Execute(@"
                UPDATE kiosk_push SET status = 'done', completed_at = now()
                 WHERE id = @id AND tenant_id = @tenantId AND status = 'completing' AND claim_id = @claimId",
                new { id, tenantId, claimId }) == 1;

        public Task ReleasePush(Guid id, Guid tenantId, Guid claimId) => _db.Execute(@"
            UPDATE kiosk_push SET status = 'in_progress'
             WHERE id = @id AND tenant_id = @tenantId AND status = 'completing' AND claim_id = @claimId",
            new { id, tenantId, claimId });

        public async Task<bool> EndPush(Guid id, Guid tenantId, string endReason) =>
            await _db.Execute(@"
                UPDATE kiosk_push SET status = 'expired', end_reason = @endReason
                 WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending', 'in_progress', 'completing')",
                new { id, tenantId, endReason }) == 1;

        public async Task<bool> CancelPush(Guid id, Guid tenantId, Guid? byUserId) =>
            await _db.Execute(@"
                UPDATE kiosk_push p SET status = 'cancelled', cancelled_at = now(), cancelled_by_user_id = @byUserId
                 WHERE p.id = @id AND p.tenant_id = @tenantId AND " + IsClaimable,
                new { id, tenantId, byUserId }) == 1;

        public async Task<bool> DeclinePush(Guid id, Guid tenantId, Guid kioskDeviceId) =>
            await _db.Execute(@"
                UPDATE kiosk_push p SET status = 'cancelled', cancelled_at = now(), end_reason = 'declined'
                 WHERE p.id = @id AND p.tenant_id = @tenantId AND p.kiosk_device_id = @kioskDeviceId AND " + IsClaimable,
                new { id, tenantId, kioskDeviceId }) == 1;

        public Task TouchPoll(Guid kioskDeviceId, Guid tenantId) => _db.Execute(
            "UPDATE kiosk_device SET last_poll_at = now() WHERE id = @kioskDeviceId AND tenant_id = @tenantId",
            new { kioskDeviceId, tenantId });
    }
}
