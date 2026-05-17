using Services.Helpers.Interfaces;
using Services.Repositories.Data.RentalData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RentalRepository : IRentalRepository
    {
        private const string ProductColumns = @"
            id, tenant_id AS TenantId, name, description,
            image_url AS ImageUrl,
            daily_rate_cents AS DailyRateCents,
            deposit_cents AS DepositCents,
            tracking_kind AS TrackingKind,
            inventory_pool AS InventoryPool,
            requires_waiver AS RequiresWaiver,
            rider_paid_service_charge_bps AS RiderPaidServiceChargeBps,
            is_active AS IsActive,
            sort_order AS SortOrder,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ItemColumns = @"
            id, tenant_id AS TenantId, product_id AS ProductId,
            label, serial, notes, status,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string PurchaseColumns = @"
            id, tenant_id AS TenantId, product_id AS ProductId,
            purchaser_user_id AS PurchaserUserId,
            purchaser_email AS PurchaserEmail,
            purchaser_name AS PurchaserName,
            waiver_signature_id AS WaiverSignatureId,
            start_date AS StartDate, end_date AS EndDate,
            quantity,
            daily_rate_cents_frozen AS DailyRateCentsFrozen,
            days_count AS DaysCount,
            amount_cents AS AmountCents,
            service_charge_cents AS ServiceChargeCents,
            deposit_cents AS DepositCents,
            rental_pi_id AS RentalPiId,
            deposit_pi_id AS DepositPiId,
            deposit_captured_cents AS DepositCapturedCents,
            redemption_token AS RedemptionToken,
            status,
            checked_out_at AS CheckedOutAt,
            returned_at AS ReturnedAt,
            condition_notes AS ConditionNotes,
            payment_method AS PaymentMethod,
            cancelled_reason AS CancelledReason,
            cancelled_by_user_id AS CancelledByUserId,
            applied_reward_redemption_id AS AppliedRewardRedemptionId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public RentalRepository(IDbHelper db) => _db = db;

        // ── Products ─────────────────────────────────────────────────────────
        public async Task<List<RentalProduct>> ListProducts(Guid tenantId, bool activeOnly)
        {
            var where = activeOnly ? "AND is_active = true" : "";
            var sql = $"SELECT {ProductColumns} FROM rental_product " +
                      $"WHERE tenant_id = @tenantId {where} ORDER BY sort_order, name";
            return (await _db.Query<RentalProduct>(sql, new { tenantId })).ToList();
        }

        public async Task<RentalProduct?> GetProduct(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {ProductColumns} FROM rental_product " +
                      "WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<RentalProduct>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateProduct(RentalProduct p)
        {
            const string sql = @"
                INSERT INTO rental_product
                    (tenant_id, name, description, image_url,
                     daily_rate_cents, deposit_cents, tracking_kind, inventory_pool,
                     requires_waiver, rider_paid_service_charge_bps, is_active, sort_order)
                VALUES
                    (@TenantId, @Name, @Description, @ImageUrl,
                     @DailyRateCents, @DepositCents, @TrackingKind, @InventoryPool,
                     @RequiresWaiver, @RiderPaidServiceChargeBps, @IsActive, @SortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task UpdateProduct(RentalProduct p)
        {
            const string sql = @"
                UPDATE rental_product SET
                    name = @Name,
                    description = @Description,
                    image_url = @ImageUrl,
                    daily_rate_cents = @DailyRateCents,
                    deposit_cents = @DepositCents,
                    tracking_kind = @TrackingKind,
                    inventory_pool = @InventoryPool,
                    requires_waiver = @RequiresWaiver,
                    rider_paid_service_charge_bps = @RiderPaidServiceChargeBps,
                    is_active = @IsActive,
                    sort_order = @SortOrder
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task DeleteProduct(Guid id, Guid tenantId)
        {
            // Hard delete only succeeds when there are no rental_purchase rows on it
            // (FK is ON DELETE RESTRICT). Caller catches 23503 and turns it into a
            // "set inactive instead" 400.
            const string sql = "DELETE FROM rental_product WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE rental_product AS p
                SET sort_order = data.sort_order, updated_at = now()
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE p.id = data.id AND p.tenant_id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId,
                ids = ids.ToArray(),
                orders = sortOrders.ToArray(),
            });
        }

        // ── Per-item units ───────────────────────────────────────────────────
        public async Task<List<RentalItem>> ListItems(Guid productId)
        {
            var sql = $"SELECT {ItemColumns} FROM rental_item " +
                      "WHERE product_id = @productId ORDER BY status, label";
            return (await _db.Query<RentalItem>(sql, new { productId })).ToList();
        }

        public async Task<RentalItem?> GetItem(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {ItemColumns} FROM rental_item " +
                      "WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<RentalItem>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateItem(RentalItem i)
        {
            const string sql = @"
                INSERT INTO rental_item (tenant_id, product_id, label, serial, notes, status)
                VALUES (@TenantId, @ProductId, @Label, @Serial, @Notes, @Status)
                RETURNING id";
            return (await _db.Query<Guid>(sql, i)).First();
        }

        public async Task UpdateItem(RentalItem i)
        {
            const string sql = @"
                UPDATE rental_item SET
                    label = @Label, serial = @Serial, notes = @Notes, status = @Status
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, i);
        }

        public async Task DeleteItem(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM rental_item WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task<int> CountAvailablePerItemUnits(Guid productId, DateTime fromDate, DateTime toDate)
        {
            // Count items that are 'available' AND have no overlapping booking AND
            // no overlapping scheduled maintenance window.
            const string sql = @"
                SELECT COUNT(*) FROM rental_item ri
                WHERE ri.product_id = @productId
                  AND ri.status = 'available'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM rental_purchase_item rpi
                    JOIN rental_purchase rp ON rp.id = rpi.purchase_id
                    WHERE rpi.item_id = ri.id
                      AND rp.status IN ('paid','out')
                      AND rp.start_date <= @toDate
                      AND rp.end_date   >= @fromDate
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM rental_item_maintenance m
                    WHERE m.item_id = ri.id
                      AND m.starts_at_date <= @toDate
                      AND m.ends_at_date   >= @fromDate
                  )";
            return await _db.ExecuteScalar(sql, new { productId, fromDate, toDate });
        }

        public async Task<List<Guid>> PickAvailablePerItemUnits(Guid productId, DateTime fromDate, DateTime toDate, int quantity)
        {
            const string sql = @"
                SELECT ri.id FROM rental_item ri
                WHERE ri.product_id = @productId
                  AND ri.status = 'available'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM rental_purchase_item rpi
                    JOIN rental_purchase rp ON rp.id = rpi.purchase_id
                    WHERE rpi.item_id = ri.id
                      AND rp.status IN ('paid','out')
                      AND rp.start_date <= @toDate
                      AND rp.end_date   >= @fromDate
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM rental_item_maintenance m
                    WHERE m.item_id = ri.id
                      AND m.starts_at_date <= @toDate
                      AND m.ends_at_date   >= @fromDate
                  )
                ORDER BY ri.label
                LIMIT @quantity";
            return (await _db.Query<Guid>(sql, new { productId, fromDate, toDate, quantity })).ToList();
        }

        public async Task<int> SumOverlappingPoolReserved(Guid productId, DateTime fromDate, DateTime toDate)
        {
            const string sql = @"
                SELECT COALESCE(SUM(quantity), 0)::int FROM rental_purchase
                WHERE product_id = @productId
                  AND status IN ('paid','out')
                  AND start_date <= @toDate
                  AND end_date   >= @fromDate";
            return await _db.ExecuteScalar(sql, new { productId, fromDate, toDate });
        }

        // ── Purchases ────────────────────────────────────────────────────────
        public async Task<(Guid Id, Guid RedemptionToken)> CreatePurchase(RentalPurchase p)
        {
            const string sql = @"
                INSERT INTO rental_purchase
                    (tenant_id, product_id, purchaser_user_id, purchaser_email, purchaser_name,
                     waiver_signature_id,
                     start_date, end_date, quantity,
                     daily_rate_cents_frozen, days_count,
                     amount_cents, service_charge_cents, deposit_cents,
                     status, payment_method,
                     applied_reward_redemption_id)
                VALUES
                    (@TenantId, @ProductId, @PurchaserUserId, @PurchaserEmail, @PurchaserName,
                     @WaiverSignatureId,
                     @StartDate, @EndDate, @Quantity,
                     @DailyRateCentsFrozen, @DaysCount,
                     @AmountCents, @ServiceChargeCents, @DepositCents,
                     @Status, @PaymentMethod,
                     @AppliedRewardRedemptionId)
                RETURNING id, redemption_token";
            var row = (await _db.Query<(Guid Id, Guid RedemptionToken)>(sql, p)).First();
            return row;
        }

        public async Task<RentalPurchase?> GetPurchase(Guid id)
        {
            var sql = $"SELECT {PurchaseColumns} FROM rental_purchase WHERE id = @id LIMIT 1";
            return (await _db.Query<RentalPurchase>(sql, new { id })).FirstOrDefault();
        }

        public async Task<RentalPurchase?> GetPurchaseByRedemptionToken(Guid token)
        {
            var sql = $"SELECT {PurchaseColumns} FROM rental_purchase WHERE redemption_token = @token LIMIT 1";
            return (await _db.Query<RentalPurchase>(sql, new { token })).FirstOrDefault();
        }

        public async Task<RentalPurchase?> GetPurchaseByRentalPaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {PurchaseColumns} FROM rental_purchase " +
                      "WHERE rental_pi_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<RentalPurchase>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task<RentalPurchase?> GetPurchaseByDepositPaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {PurchaseColumns} FROM rental_purchase " +
                      "WHERE deposit_pi_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<RentalPurchase>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task SetRentalPaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE rental_purchase SET rental_pi_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        public async Task SetDepositPaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE rental_purchase SET deposit_pi_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE rental_purchase SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task MarkOut(Guid id, DateTime atUtc)
        {
            const string sql = @"
                UPDATE rental_purchase
                SET status = 'out', checked_out_at = @atUtc
                WHERE id = @id";
            await _db.Execute(sql, new { id, atUtc });
        }

        public async Task MarkReturned(Guid id, DateTime atUtc, string? conditionNotes,
            int depositCapturedCents, bool damaged)
        {
            const string sql = @"
                UPDATE rental_purchase
                SET status = @status,
                    returned_at = @atUtc,
                    condition_notes = @conditionNotes,
                    deposit_captured_cents = @depositCapturedCents
                WHERE id = @id";
            await _db.Execute(sql, new
            {
                id, atUtc, conditionNotes, depositCapturedCents,
                status = damaged ? "damaged" : "returned",
            });
        }

        public async Task AssignItems(Guid purchaseId, IEnumerable<Guid> itemIds)
        {
            const string sql = @"
                INSERT INTO rental_purchase_item (purchase_id, item_id)
                VALUES (@purchaseId, @itemId)
                ON CONFLICT DO NOTHING";
            foreach (var itemId in itemIds)
            {
                await _db.Execute(sql, new { purchaseId, itemId });
            }
        }

        public async Task<List<RentalPurchaseItem>> ListAssignedItems(Guid purchaseId)
        {
            const string sql = @"
                SELECT id, purchase_id AS PurchaseId, item_id AS ItemId,
                       checkout_photo_data_url AS CheckoutPhotoDataUrl,
                       checkout_notes AS CheckoutNotes,
                       return_photo_data_url AS ReturnPhotoDataUrl,
                       return_notes AS ReturnNotes
                FROM rental_purchase_item
                WHERE purchase_id = @purchaseId";
            return (await _db.Query<RentalPurchaseItem>(sql, new { purchaseId })).ToList();
        }

        public async Task SetCheckoutCondition(Guid purchaseItemId, string? photoDataUrl, string? notes)
        {
            const string sql = @"
                UPDATE rental_purchase_item
                SET checkout_photo_data_url = @photoDataUrl,
                    checkout_notes = @notes
                WHERE id = @purchaseItemId";
            await _db.Execute(sql, new { purchaseItemId, photoDataUrl, notes });
        }

        public async Task SetReturnCondition(Guid purchaseItemId, string? photoDataUrl, string? notes)
        {
            const string sql = @"
                UPDATE rental_purchase_item
                SET return_photo_data_url = @photoDataUrl,
                    return_notes = @notes
                WHERE id = @purchaseItemId";
            await _db.Execute(sql, new { purchaseItemId, photoDataUrl, notes });
        }

        public async Task<List<RentalPurchase>> ListMine(Guid userId, Guid tenantId)
        {
            var sql = $@"
                SELECT {PurchaseColumns}
                FROM rental_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                ORDER BY start_date DESC";
            return (await _db.Query<RentalPurchase>(sql, new { tenantId, userId })).ToList();
        }

        public async Task<List<RentalPurchase>> ListForCounter(Guid tenantId, DateTime fromUtc, DateTime toUtc, string? status)
        {
            var statusFilter = string.IsNullOrEmpty(status) ? "" : "AND status = @status";
            var sql = $@"
                SELECT {PurchaseColumns}
                FROM rental_purchase
                WHERE tenant_id = @tenantId
                  AND start_date <= @toUtc
                  AND end_date   >= @fromUtc
                  {statusFilter}
                ORDER BY start_date";
            return (await _db.Query<RentalPurchase>(sql, new { tenantId, fromUtc, toUtc, status })).ToList();
        }

        // ── Maintenance ──────────────────────────────────────────────────────
        private const string MaintenanceColumns = @"
            id, tenant_id AS TenantId, item_id AS ItemId,
            starts_at_date AS StartsAtDate, ends_at_date AS EndsAtDate,
            reason, created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<List<RentalItemMaintenance>> ListMaintenanceForItem(Guid itemId)
        {
            var sql = $@"
                SELECT {MaintenanceColumns} FROM rental_item_maintenance
                WHERE item_id = @itemId ORDER BY starts_at_date";
            return (await _db.Query<RentalItemMaintenance>(sql, new { itemId })).ToList();
        }

        public async Task<List<RentalItemMaintenance>> ListUpcomingMaintenanceForProduct(Guid productId)
        {
            var sql = $@"
                SELECT {MaintenanceColumns} FROM rental_item_maintenance m
                WHERE m.ends_at_date >= CURRENT_DATE
                  AND m.item_id IN (SELECT id FROM rental_item WHERE product_id = @productId)
                ORDER BY m.starts_at_date";
            return (await _db.Query<RentalItemMaintenance>(sql, new { productId })).ToList();
        }

        public async Task<RentalItemMaintenance?> GetMaintenance(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {MaintenanceColumns} FROM rental_item_maintenance " +
                      "WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<RentalItemMaintenance>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> AddMaintenance(RentalItemMaintenance m)
        {
            const string sql = @"
                INSERT INTO rental_item_maintenance
                    (tenant_id, item_id, starts_at_date, ends_at_date, reason)
                VALUES
                    (@TenantId, @ItemId, @StartsAtDate, @EndsAtDate, @Reason)
                RETURNING id";
            return (await _db.Query<Guid>(sql, m)).First();
        }

        public async Task UpdateMaintenance(RentalItemMaintenance m)
        {
            const string sql = @"
                UPDATE rental_item_maintenance
                SET starts_at_date = @StartsAtDate,
                    ends_at_date   = @EndsAtDate,
                    reason         = @Reason
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, m);
        }

        public async Task DeleteMaintenance(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM rental_item_maintenance WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }
    }
}
