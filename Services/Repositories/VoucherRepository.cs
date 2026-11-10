using Dapper;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.VoucherData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    /// <summary>
    /// Voucher batches and their codes (Script0315). A code is a coupon row (100% off one rider
    /// admission, one use) carrying voucher_batch_id; redemption goes through the coupon paths.
    /// Every statement is tenant-scoped.
    /// </summary>
    public class VoucherRepository : IVoucherRepository
    {
        // The staff roles that may hold an allowance: everyone at the venue except riders.
        private static readonly string[] StaffRoles =
        {
            "tenant_admin", "tenant_manager", "tenant_accountant", "tenant_cashier",
            "tenant_fnb_cashier", "tenant_shop_cashier", "tenant_scanner", "tenant_staff",
        };

        private const string BatchColumns = @"
            b.id, b.tenant_id AS TenantId, b.kind, b.issued_by_user_id AS IssuedByUserId, b.label,
            b.recipient_email AS RecipientEmail, b.note, b.code_count AS CodeCount, b.event_id AS EventId,
            b.expires_at_utc AS ExpiresAtUtc, b.created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public VoucherRepository(IDbHelper db) => _db = db;

        public async Task<VoucherBatch> CreateBatch(VoucherBatch batch, IReadOnlyList<string> codes, string description)
        {
            if (codes.Count != batch.CodeCount) throw new ArgumentException("One code per voucher in the batch.");
            return await _db.WithTransaction(async (conn, tx) =>
            {
                var created = await conn.QuerySingleAsync<VoucherBatch>(@"
                    INSERT INTO voucher_batch AS b
                        (tenant_id, kind, issued_by_user_id, label, recipient_email, note, code_count, event_id, expires_at_utc)
                    VALUES (@TenantId, @Kind, @IssuedByUserId, @Label, @RecipientEmail, @Note, @CodeCount, @EventId, @ExpiresAtUtc)
                    RETURNING " + BatchColumns, batch, tx);

                // 100% off one rider admission, used once. ApplicableScope event_ticket keeps it
                // off season passes and memberships; the one-admission rule is applied by the
                // checkout and Gate Sale paths, which read voucher_batch_id.
                await conn.ExecuteAsync(@"
                    INSERT INTO coupon
                        (tenant_id, code, description, discount_kind, discount_value, applicable_scope,
                         applicable_event_id, valid_to_utc, max_total_uses, max_uses_per_user, is_active,
                         created_by_user_id, voucher_batch_id)
                    SELECT @TenantId, c, @description, 'percent', 10000, 'event_ticket',
                           @EventId, @ExpiresAtUtc, 1, NULL, true, @IssuedByUserId, @BatchId
                    FROM unnest(@codes) AS c",
                    new
                    {
                        created.TenantId, created.EventId, created.ExpiresAtUtc, created.IssuedByUserId,
                        BatchId = created.Id, description, codes = codes.ToArray(),
                    }, tx);
                return created;
            });
        }

        public async Task<bool> AnyCodeExists(Guid tenantId, IReadOnlyList<string> codes)
        {
            const string sql = @"
                SELECT EXISTS (SELECT 1 FROM coupon
                               WHERE tenant_id = @tenantId AND lower(code) = ANY(@codes))";
            return (await _db.Query<bool>(sql, new { tenantId, codes = codes.Select(c => c.ToLowerInvariant()).ToArray() }))
                .FirstOrDefault();
        }

        public async Task<int> CountIssuedSince(Guid tenantId, Guid userId, DateTime sinceUtc)
        {
            // Codes an employee has handed out this year from their own allowance. A voided code
            // that was never used goes back on the allowance; a used one always counts.
            const string sql = @"
                SELECT COUNT(*)::int
                FROM coupon c
                JOIN voucher_batch b ON b.id = c.voucher_batch_id AND b.tenant_id = c.tenant_id
                WHERE c.tenant_id = @tenantId AND b.tenant_id = @tenantId
                  AND b.kind = 'employee' AND b.issued_by_user_id = @userId
                  AND b.created_at >= @sinceUtc
                  AND (c.voucher_voided_at IS NULL
                       OR EXISTS (SELECT 1 FROM coupon_redemption r
                                  WHERE r.coupon_id = c.id AND r.tenant_id = @tenantId))";
            return (await _db.Query<int>(sql, new { tenantId, userId, sinceUtc })).FirstOrDefault();
        }

        public async Task<StaffVoucherAllowance?> GetAllowance(Guid tenantId, Guid userId)
        {
            const string sql = @"
                SELECT tenant_id AS TenantId, user_id AS UserId, yearly_allowance AS YearlyAllowance
                FROM staff_voucher_allowance
                WHERE tenant_id = @tenantId AND user_id = @userId";
            return (await _db.Query<StaffVoucherAllowance>(sql, new { tenantId, userId })).FirstOrDefault();
        }

        public async Task SetAllowance(Guid tenantId, Guid userId, int? yearlyAllowance, Guid updatedByUserId)
        {
            // The target must be this venue's staff member; the WHERE makes a foreign user a no-op.
            const string sql = @"
                INSERT INTO staff_voucher_allowance (tenant_id, user_id, yearly_allowance, updated_by_user_id, updated_at)
                SELECT @tenantId, u.id, @yearlyAllowance, @updatedByUserId, now()
                FROM users u
                WHERE u.id = @userId AND u.tenant_id = @tenantId
                ON CONFLICT (tenant_id, user_id) DO UPDATE
                    SET yearly_allowance = EXCLUDED.yearly_allowance,
                        updated_by_user_id = EXCLUDED.updated_by_user_id,
                        updated_at = now()";
            await _db.Execute(sql, new { tenantId, userId, yearlyAllowance, updatedByUserId });
        }

        public async Task<List<StaffVoucherSummaryRow>> ListStaffSummary(Guid tenantId, DateTime sinceUtc)
        {
            const string sql = @"
                SELECT u.id AS UserId, u.first_name AS FirstName, u.last_name AS LastName, u.email AS Email,
                       u.role AS Role, u.roles AS Roles,
                       (a.user_id IS NOT NULL) AS HasAllowanceRow, a.yearly_allowance AS YearlyAllowance,
                       COALESCE(s.issued, 0) AS IssuedThisYear, COALESCE(s.redeemed, 0) AS RedeemedThisYear
                FROM users u
                LEFT JOIN staff_voucher_allowance a ON a.tenant_id = @tenantId AND a.user_id = u.id
                LEFT JOIN LATERAL (
                    SELECT COUNT(*) FILTER (WHERE c.voucher_voided_at IS NULL OR r.id IS NOT NULL)::int AS issued,
                           COUNT(r.id)::int AS redeemed
                    FROM voucher_batch b
                    JOIN coupon c ON c.voucher_batch_id = b.id AND c.tenant_id = @tenantId
                    LEFT JOIN coupon_redemption r ON r.coupon_id = c.id AND r.tenant_id = @tenantId
                    WHERE b.tenant_id = @tenantId AND b.kind = 'employee'
                      AND b.issued_by_user_id = u.id AND b.created_at >= @sinceUtc
                ) s ON true
                WHERE u.tenant_id = @tenantId AND u.status = 'active'
                  AND (u.role = ANY(@staffRoles) OR u.roles && @staffRoles)
                ORDER BY lower(u.first_name), lower(u.last_name)";
            return (await _db.Query<StaffVoucherSummaryRow>(sql, new { tenantId, sinceUtc, staffRoles = StaffRoles })).ToList();
        }

        public async Task<List<VoucherBatchRow>> ListBatches(Guid tenantId, Guid? issuedByUserId, string? kind, int take)
        {
            var sql = $@"
                SELECT {BatchColumns},
                       trim(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')) AS IssuedByName,
                       e.title AS EventTitle,
                       COALESCE(s.redeemed, 0) AS RedeemedCount, COALESCE(s.voided, 0) AS VoidedCount
                FROM voucher_batch b
                LEFT JOIN users u ON u.id = b.issued_by_user_id
                LEFT JOIN event e ON e.id = b.event_id AND e.tenant_id = @tenantId
                LEFT JOIN LATERAL (
                    SELECT COUNT(r.id)::int AS redeemed,
                           COUNT(*) FILTER (WHERE c.voucher_voided_at IS NOT NULL AND r.id IS NULL)::int AS voided
                    FROM coupon c
                    LEFT JOIN coupon_redemption r ON r.coupon_id = c.id AND r.tenant_id = @tenantId
                    WHERE c.voucher_batch_id = b.id AND c.tenant_id = @tenantId
                ) s ON true
                WHERE b.tenant_id = @tenantId
                  {(issuedByUserId is null ? "" : "AND b.issued_by_user_id = @issuedByUserId")}
                  {(kind is null ? "" : "AND b.kind = @kind")}
                ORDER BY b.created_at DESC
                LIMIT @take";
            return (await _db.Query<VoucherBatchRow>(sql, new { tenantId, issuedByUserId, kind, take })).ToList();
        }

        public async Task<VoucherBatch?> GetBatch(Guid tenantId, Guid batchId)
        {
            var sql = $"SELECT {BatchColumns} FROM voucher_batch b WHERE b.id = @batchId AND b.tenant_id = @tenantId";
            return (await _db.Query<VoucherBatch>(sql, new { tenantId, batchId })).FirstOrDefault();
        }

        public async Task<List<VoucherCodeRow>> ListCodes(Guid tenantId, Guid batchId)
        {
            const string sql = @"
                SELECT c.id AS Id, c.voucher_batch_id AS BatchId, c.code AS Code, c.valid_to_utc AS ValidToUtc,
                       c.voucher_voided_at AS VoidedAt,
                       r.redeemed_at AS RedeemedAt, r.user_id AS RedeemedByUserId,
                       NULLIF(trim(COALESCE(ru.first_name, '') || ' ' || COALESCE(ru.last_name, '')), '') AS RedeemedByName,
                       t.id AS TicketPurchaseId,
                       COALESCE(NULLIF(trim(COALESCE(t.rider_first_name, '') || ' ' || COALESCE(t.rider_last_name, '')), ''),
                                t.purchaser_name) AS RiderName,
                       e.title AS EventTitle
                FROM coupon c
                LEFT JOIN coupon_redemption r ON r.coupon_id = c.id AND r.tenant_id = @tenantId
                LEFT JOIN users ru ON ru.id = r.user_id
                LEFT JOIN event_ticket_purchase t
                       ON r.source_kind = 'event_ticket' AND t.id = r.source_id AND t.tenant_id = @tenantId
                LEFT JOIN event_ticket_tier tt ON tt.id = t.tier_id
                LEFT JOIN event e ON e.id = tt.event_id AND e.tenant_id = @tenantId
                WHERE c.tenant_id = @tenantId AND c.voucher_batch_id = @batchId
                ORDER BY c.code";
            return (await _db.Query<VoucherCodeRow>(sql, new { tenantId, batchId })).ToList();
        }

        public async Task<int> VoidUnusedCodes(Guid tenantId, Guid batchId, Guid? couponId)
        {
            // Only codes nobody has used: a redeemed code is the record of an admission.
            var sql = $@"
                UPDATE coupon c
                SET is_active = false, voucher_voided_at = now()
                WHERE c.tenant_id = @tenantId AND c.voucher_batch_id = @batchId
                  {(couponId is null ? "" : "AND c.id = @couponId")}
                  AND c.voucher_voided_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM coupon_redemption r
                                  WHERE r.coupon_id = c.id AND r.tenant_id = @tenantId)";
            return await _db.Execute(sql, new { tenantId, batchId, couponId });
        }
    }
}
