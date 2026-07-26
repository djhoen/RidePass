using Services.Helpers.Interfaces;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventExtraRepository : IEventExtraRepository
    {
        private const string ProductColumns = @"
            id, tenant_id AS TenantId, name, description, image_url AS ImageUrl,
            kind, price_cents AS PriceCents,
            rider_paid_service_charge_bps AS RiderPaidServiceChargeBps,
            requires_waiver AS RequiresWaiver,
            is_active AS IsActive, sort_order AS SortOrder,
            expires_at AS ExpiresAt, inventory AS Inventory,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string PurchaseColumns = @"
            id, tenant_id AS TenantId, event_id AS EventId, product_id AS ProductId,
            purchaser_user_id AS PurchaserUserId,
            purchaser_email AS PurchaserEmail, purchaser_name AS PurchaserName,
            waiver_signature_id AS WaiverSignatureId,
            quantity, unit_price_cents_frozen AS UnitPriceCentsFrozen,
            amount_cents AS AmountCents, service_charge_cents AS ServiceChargeCents,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            redemption_token AS RedemptionToken,
            status,
            redeemed_at_utc AS RedeemedAtUtc, redeemed_by_user_id AS RedeemedByUserId,
            cancelled_reason AS CancelledReason, cancelled_by_user_id AS CancelledByUserId,
            cancelled_at AS CancelledAt, refund_note AS RefundNote,
            payment_method AS PaymentMethod,
            sold_by_user_id AS SoldByUserId,
            variant_id AS VariantId,
            size_at_purchase AS SizeAtPurchase,
            color_at_purchase AS ColorAtPurchase,
            gender_at_purchase AS GenderAtPurchase,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        // Same columns, aliased for the queries that join the catalog product (purchase alias p).
        private const string PrefixedPurchaseColumns = @"
            p.id, p.tenant_id AS TenantId, p.event_id AS EventId, p.product_id AS ProductId,
            p.purchaser_user_id AS PurchaserUserId,
            p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
            p.waiver_signature_id AS WaiverSignatureId,
            p.quantity, p.unit_price_cents_frozen AS UnitPriceCentsFrozen,
            p.amount_cents AS AmountCents, p.service_charge_cents AS ServiceChargeCents,
            p.stripe_payment_intent_id AS StripePaymentIntentId,
            p.stripe_connected_account_id AS StripeConnectedAccountId,
            p.redemption_token AS RedemptionToken,
            p.status,
            p.redeemed_at_utc AS RedeemedAtUtc, p.redeemed_by_user_id AS RedeemedByUserId,
            p.cancelled_reason AS CancelledReason, p.cancelled_by_user_id AS CancelledByUserId,
            p.cancelled_at AS CancelledAt, p.refund_note AS RefundNote,
            p.payment_method AS PaymentMethod,
            p.sold_by_user_id AS SoldByUserId,
            p.variant_id AS VariantId,
            p.size_at_purchase AS SizeAtPurchase,
            p.color_at_purchase AS ColorAtPurchase,
            p.gender_at_purchase AS GenderAtPurchase,
            p.created_at AS CreatedAt, p.updated_at AS UpdatedAt";

        private const string VariantColumns = @"
            id, product_id AS ProductId,
            size, color, gender, sku,
            tier, description,
            price_cents AS PriceCents,
            inventory,
            image_url AS ImageUrl,
            sort_order AS SortOrder,
            is_active AS IsActive,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public EventExtraRepository(IDbHelper db) => _db = db;

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason)
        {
            const string sql = @"
                UPDATE event_extra_purchase
                SET status = 'cancelled',
                    cancelled_reason = @reason,
                    cancelled_at = now(),
                    cancelled_by_user_id = @cancelledByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            await _db.Execute(sql, new { id, tenantId, cancelledByUserId, reason });
        }

        public async Task MarkRefunded(Guid id, string? refundNote)
        {
            const string sql = "UPDATE event_extra_purchase SET status = 'refunded', refund_note = @refundNote WHERE id = @id";
            await _db.Execute(sql, new { id, refundNote });
        }

        // ── Products ─────────────────────────────────────────────────────────
        public async Task<List<EventExtraProduct>> ListProducts(Guid tenantId, bool activeOnly)
        {
            var where = activeOnly ? "AND is_active = true" : "";
            var sql = $@"
                SELECT {ProductColumns} FROM event_extra_product
                WHERE tenant_id = @tenantId {where}
                ORDER BY sort_order, name";
            return (await _db.Query<EventExtraProduct>(sql, new { tenantId })).ToList();
        }

        public async Task<EventExtraProduct?> GetProduct(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {ProductColumns} FROM event_extra_product " +
                      "WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<EventExtraProduct>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateProduct(EventExtraProduct p)
        {
            const string sql = @"
                INSERT INTO event_extra_product
                    (tenant_id, name, description, image_url, kind,
                     price_cents, rider_paid_service_charge_bps,
                     requires_waiver, is_active, sort_order,
                     expires_at, inventory)
                VALUES
                    (@TenantId, @Name, @Description, @ImageUrl, @Kind,
                     @PriceCents, @RiderPaidServiceChargeBps,
                     @RequiresWaiver, @IsActive, @SortOrder,
                     @ExpiresAt, @Inventory)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task UpdateProduct(EventExtraProduct p)
        {
            const string sql = @"
                UPDATE event_extra_product SET
                    name = @Name, description = @Description, image_url = @ImageUrl,
                    kind = @Kind,
                    price_cents = @PriceCents,
                    rider_paid_service_charge_bps = @RiderPaidServiceChargeBps,
                    requires_waiver = @RequiresWaiver,
                    is_active = @IsActive, sort_order = @SortOrder,
                    expires_at = @ExpiresAt, inventory = @Inventory
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task<int> SumSoldProduct(Guid productId)
        {
            const string sql = @"
                SELECT COALESCE(SUM(quantity), 0)::int FROM event_extra_purchase
                WHERE product_id = @productId AND status IN ('paid','redeemed')";
            return await _db.ExecuteScalar(sql, new { productId });
        }

        public async Task DeleteProduct(Guid id, Guid tenantId)
        {
            // ON DELETE RESTRICT on event_extra_purchase.product_id keeps history;
            // controller catches 23503 and converts to a 400.
            const string sql = "DELETE FROM event_extra_product WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            // Single round-trip: zip two parallel arrays via unnest. Tenant predicate
            // guarantees rows in another tenant can't be moved even if their ids leaked.
            const string sql = @"
                UPDATE event_extra_product AS p
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

        // ── Eligibility ──────────────────────────────────────────────────────
        public async Task<List<EventExtraEligibility>> ListEligibilityForEvent(Guid eventId)
        {
            const string sql = @"
                SELECT event_id AS EventId, product_id AS ProductId, inventory
                FROM event_extra_eligibility
                WHERE event_id = @eventId";
            return (await _db.Query<EventExtraEligibility>(sql, new { eventId })).ToList();
        }

        public async Task<Dictionary<Guid, List<EventExtraEligibility>>> ListEligibilityForEvents(IEnumerable<Guid> eventIds)
        {
            var ids = eventIds.ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, List<EventExtraEligibility>>();
            const string sql = @"
                SELECT event_id AS EventId, product_id AS ProductId, inventory
                FROM event_extra_eligibility
                WHERE event_id = ANY(@ids)";
            var rows = await _db.Query<EventExtraEligibility>(sql, new { ids });
            return rows.GroupBy(r => r.EventId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<EventExtraEligibility?> GetEligibility(Guid eventId, Guid productId)
        {
            const string sql = @"
                SELECT event_id AS EventId, product_id AS ProductId, inventory
                FROM event_extra_eligibility
                WHERE event_id = @eventId AND product_id = @productId LIMIT 1";
            return (await _db.Query<EventExtraEligibility>(sql, new { eventId, productId })).FirstOrDefault();
        }

        public async Task ReplaceEligibility(Guid eventId, IEnumerable<EventExtraEligibility> rows)
        {
            await _db.Execute("DELETE FROM event_extra_eligibility WHERE event_id = @eventId",
                new { eventId });
            const string insert = @"
                INSERT INTO event_extra_eligibility (event_id, product_id, inventory)
                VALUES (@EventId, @ProductId, @Inventory)
                ON CONFLICT DO NOTHING";
            foreach (var r in rows)
            {
                if (r.ProductId == Guid.Empty) continue;
                r.EventId = eventId;
                await _db.Execute(insert, r);
            }
        }

        // ── Purchases ────────────────────────────────────────────────────────
        public async Task<(Guid Id, Guid RedemptionToken)> CreatePurchase(EventExtraPurchase p)
        {
            const string sql = @"
                INSERT INTO event_extra_purchase
                    (tenant_id, event_id, product_id, purchaser_user_id,
                     purchaser_email, purchaser_name, waiver_signature_id,
                     quantity, unit_price_cents_frozen, amount_cents, service_charge_cents,
                     status, payment_method,
                     variant_id, size_at_purchase, color_at_purchase, gender_at_purchase,
                     sold_by_user_id,
                     discount_cents, discount_preset_id, discount_label, discount_authorized_by_user_id)
                VALUES
                    (@TenantId, @EventId, @ProductId, @PurchaserUserId,
                     @PurchaserEmail, @PurchaserName, @WaiverSignatureId,
                     @Quantity, @UnitPriceCentsFrozen, @AmountCents, @ServiceChargeCents,
                     @Status, @PaymentMethod,
                     @VariantId, @SizeAtPurchase, @ColorAtPurchase, @GenderAtPurchase,
                     @SoldByUserId,
                     @DiscountCents, @DiscountPresetId, @DiscountLabel, @DiscountAuthorizedByUserId)
                RETURNING id, redemption_token";
            return (await _db.Query<(Guid Id, Guid RedemptionToken)>(sql, p)).First();
        }

        public async Task<EventExtraPurchase?> GetPurchase(Guid id)
        {
            var sql = $"SELECT {PurchaseColumns} FROM event_extra_purchase WHERE id = @id LIMIT 1";
            return (await _db.Query<EventExtraPurchase>(sql, new { id })).FirstOrDefault();
        }

        public async Task<EventExtraPurchase?> GetPurchaseByPaymentIntentId(string paymentIntentId)
        {
            var sql = $@"SELECT {PurchaseColumns} FROM event_extra_purchase
                         WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<EventExtraPurchase>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task<List<EventExtraPurchase>> ListByPaymentIntentId(string paymentIntentId)
        {
            var sql = $@"SELECT {PurchaseColumns} FROM event_extra_purchase
                         WHERE stripe_payment_intent_id = @paymentIntentId
                         ORDER BY created_at";
            return (await _db.Query<EventExtraPurchase>(sql, new { paymentIntentId })).ToList();
        }

        public async Task<EventExtraPurchase?> GetPurchaseByRedemptionToken(Guid token)
        {
            var sql = $"SELECT {PurchaseColumns} FROM event_extra_purchase WHERE redemption_token = @token LIMIT 1";
            return (await _db.Query<EventExtraPurchase>(sql, new { token })).FirstOrDefault();
        }

        public async Task SetPaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE event_extra_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        // Direct charge: snapshot the connected account this extra was charged on (bundled onto a
        // direct event-ticket cart) and flag the row so refunds act on the right account.
        public async Task MarkDirectCharge(Guid id, Guid tenantId, string connectedAccountId)
        {
            const string sql = @"
                UPDATE event_extra_purchase
                SET stripe_connected_account_id = @connectedAccountId,
                    payment_method = 'stripe_direct'
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, connectedAccountId });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE event_extra_purchase SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task<bool> MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc)
        {
            // tenant_id predicate prevents a stray purchaseId from another tenant being
            // flipped to redeemed. The status guard means a cancelled or refunded add-on can never
            // be checked in, which also stops UndoRedeemed resurrecting one to 'paid'.
            const string sql = @"
                UPDATE event_extra_purchase
                SET status = 'redeemed', redeemed_at_utc = @atUtc, redeemed_by_user_id = @redeemedByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            return await _db.Execute(sql, new { id, tenantId, redeemedByUserId, atUtc }) > 0;
        }

        public async Task<bool> UndoRedeemed(Guid id, Guid tenantId)
        {
            // Guarded on 'redeemed': undoing anything else would turn a cancelled or refunded
            // add-on back into a usable one.
            const string sql = @"
                UPDATE event_extra_purchase
                SET status = 'paid', redeemed_at_utc = NULL, redeemed_by_user_id = NULL
                WHERE id = @id AND tenant_id = @tenantId AND status = 'redeemed'";
            return await _db.Execute(sql, new { id, tenantId }) > 0;
        }

        public async Task<List<ExtraCheckInRow>> SearchForCheckIn(
            Guid tenantId, Guid? productId, string? kind, Guid? eventId,
            DateTime? fromUtc, DateTime? toUtc, string? query, bool arrivedOnly, bool notArrivedOnly,
            int limit)
        {
            // Only 'paid' and 'redeemed' rows: a pending checkout is not a sale, and cancelled or
            // refunded rows must never appear on a list whose buttons admit people.
            //
            // COALESCE(e.starts_at, p.created_at) is the sort and window key. An add-on bought at
            // the counter has no event, so it has no event date to file under and its purchase time
            // is the only thing that places it in a window.
            var sql = $@"
                SELECT {PrefixedPurchaseColumns},
                       prod.name AS ProductName,
                       prod.kind AS ProductKind,
                       e.title   AS EventTitle,
                       e.starts_at AS EventStartsAtUtc,
                       NULLIF(TRIM(CONCAT_WS(' ', ru.first_name, ru.last_name)), '') AS RedeemedByName
                FROM event_extra_purchase p
                JOIN event_extra_product prod ON prod.id = p.product_id
                LEFT JOIN event e ON e.id = p.event_id AND e.tenant_id = p.tenant_id
                LEFT JOIN users ru ON ru.id = p.redeemed_by_user_id
                WHERE p.tenant_id = @tenantId
                  AND p.status IN ('paid', 'redeemed')
                  AND (@productId::uuid IS NULL OR p.product_id = @productId)
                  AND (@kind::text IS NULL OR prod.kind = @kind)
                  AND (@eventId::uuid IS NULL OR p.event_id = @eventId)
                  AND (@fromUtc::timestamptz IS NULL OR COALESCE(e.starts_at, p.created_at) >= @fromUtc)
                  AND (@toUtc::timestamptz IS NULL OR COALESCE(e.starts_at, p.created_at) <= @toUtc)
                  AND (@query::text IS NULL
                       OR p.purchaser_name ILIKE '%' || @query || '%'
                       OR p.purchaser_email ILIKE '%' || @query || '%')
                  AND (NOT @arrivedOnly OR p.status = 'redeemed')
                  AND (NOT @notArrivedOnly OR p.status = 'paid')
                ORDER BY COALESCE(e.starts_at, p.created_at), p.purchaser_name
                LIMIT @limit";
            var q = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
            return (await _db.Query<ExtraCheckInRow>(sql, new
            {
                tenantId, productId, kind, eventId, fromUtc, toUtc, query = q,
                arrivedOnly, notArrivedOnly, limit,
            })).ToList();
        }

        public async Task<List<EventExtraPurchase>> ListMine(Guid userId, Guid tenantId)
        {
            var sql = $@"
                SELECT {PurchaseColumns} FROM event_extra_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                ORDER BY created_at DESC";
            return (await _db.Query<EventExtraPurchase>(sql, new { tenantId, userId })).ToList();
        }

        public async Task<List<EventExtraPurchase>> ListForEvent(Guid eventId)
        {
            var sql = $@"
                SELECT {PurchaseColumns} FROM event_extra_purchase
                WHERE event_id = @eventId
                ORDER BY created_at DESC";
            return (await _db.Query<EventExtraPurchase>(sql, new { eventId })).ToList();
        }

        // Gate redemption, event+purchaser scope: a purchaser's add-ons for one event,
        // across orders. Tenant + event scoped; purchaser matched by user id else
        // case-insensitive email. Cancelled rows excluded. Joined to the catalog product so the
        // gate can name each add-on ("Camping, 2 nights") instead of a bare "Add-on".
        public async Task<List<EventExtraPurchaseWithProduct>> ListByEventForPurchaser(
            Guid eventId, Guid tenantId, Guid? purchaserUserId, string? purchaserEmail)
        {
            var sql = $@"
                SELECT {PrefixedPurchaseColumns},
                       prod.name AS ProductName, prod.kind AS ProductKind
                FROM event_extra_purchase p
                JOIN event_extra_product prod ON prod.id = p.product_id AND prod.tenant_id = p.tenant_id
                WHERE p.tenant_id = @tenantId
                  AND p.event_id = @eventId
                  AND p.status <> 'cancelled'
                  AND (
                        (@purchaserUserId IS NOT NULL AND p.purchaser_user_id = @purchaserUserId)
                     OR (@purchaserUserId IS NULL AND lower(p.purchaser_email) = lower(@purchaserEmail))
                      )
                ORDER BY p.created_at DESC";
            return (await _db.Query<EventExtraPurchaseWithProduct>(sql,
                new { eventId, tenantId, purchaserUserId, purchaserEmail })).ToList();
        }

        public async Task<EventExtraPurchaseWithProduct?> GetPurchaseWithProduct(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {PrefixedPurchaseColumns},
                       prod.name AS ProductName, prod.kind AS ProductKind
                FROM event_extra_purchase p
                JOIN event_extra_product prod ON prod.id = p.product_id AND prod.tenant_id = p.tenant_id
                WHERE p.id = @id AND p.tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<EventExtraPurchaseWithProduct>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<int> SumSold(Guid eventId, Guid productId)
        {
            const string sql = @"
                SELECT COALESCE(SUM(quantity), 0)::int FROM event_extra_purchase
                WHERE event_id = @eventId AND product_id = @productId
                  AND status IN ('paid','redeemed')";
            return await _db.ExecuteScalar(sql, new { eventId, productId });
        }

        public async Task<Dictionary<(Guid EventId, Guid ProductId), int>> SumSoldForEvents(IEnumerable<Guid> eventIds)
        {
            var ids = eventIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"
                SELECT event_id AS EventId, product_id AS ProductId, COALESCE(SUM(quantity), 0)::int AS Sold
                FROM event_extra_purchase
                WHERE event_id = ANY(@ids) AND status IN ('paid','redeemed')
                GROUP BY event_id, product_id";
            var rows = await _db.Query<SoldRow>(sql, new { ids });
            return rows.ToDictionary(r => (r.EventId, r.ProductId), r => r.Sold);
        }

        private record SoldRow(Guid EventId, Guid ProductId, int Sold);

        // ── Variants ─────────────────────────────────────────────────────────
        public async Task<List<EventExtraVariant>> ListVariants(Guid productId)
        {
            var sql = $@"
                SELECT {VariantColumns} FROM event_extra_variant
                WHERE product_id = @productId
                ORDER BY sort_order, COALESCE(size,''), COALESCE(color,''), COALESCE(gender,'')";
            return (await _db.Query<EventExtraVariant>(sql, new { productId })).ToList();
        }

        public async Task<Dictionary<Guid, List<EventExtraVariant>>> ListVariantsForProducts(IEnumerable<Guid> productIds)
        {
            var ids = productIds.ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, List<EventExtraVariant>>();
            var sql = $@"
                SELECT {VariantColumns} FROM event_extra_variant
                WHERE product_id = ANY(@ids)
                ORDER BY sort_order, COALESCE(size,''), COALESCE(color,''), COALESCE(gender,'')";
            var rows = await _db.Query<EventExtraVariant>(sql, new { ids });
            return rows.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<EventExtraVariant?> GetVariant(Guid id)
        {
            var sql = $"SELECT {VariantColumns} FROM event_extra_variant WHERE id = @id LIMIT 1";
            return (await _db.Query<EventExtraVariant>(sql, new { id })).FirstOrDefault();
        }

        public async Task<Guid> CreateVariant(EventExtraVariant v)
        {
            const string sql = @"
                INSERT INTO event_extra_variant
                    (product_id, size, color, gender, sku, tier, description,
                     price_cents, inventory, image_url, sort_order, is_active)
                VALUES
                    (@ProductId, @Size, @Color, @Gender, @Sku, @Tier, @Description,
                     @PriceCents, @Inventory, @ImageUrl, @SortOrder, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, v)).First();
        }

        public async Task UpdateVariant(EventExtraVariant v)
        {
            const string sql = @"
                UPDATE event_extra_variant SET
                    size = @Size, color = @Color, gender = @Gender, sku = @Sku,
                    tier = @Tier, description = @Description,
                    price_cents = @PriceCents, inventory = @Inventory,
                    image_url = @ImageUrl, sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id";
            await _db.Execute(sql, v);
        }

        public async Task DeleteVariant(Guid id)
        {
            // ON DELETE RESTRICT on event_extra_purchase.variant_id will block removal once
            // the variant has been sold. Caller catches 23503 and surfaces "set inactive instead".
            const string sql = "DELETE FROM event_extra_variant WHERE id = @id";
            await _db.Execute(sql, new { id });
        }

        public async Task<int> SumSoldVariant(Guid variantId)
        {
            // Tenant-wide — variants don't have per-event inventory.
            const string sql = @"
                SELECT COALESCE(SUM(quantity), 0)::int FROM event_extra_purchase
                WHERE variant_id = @variantId AND status IN ('paid','redeemed')";
            return await _db.ExecuteScalar(sql, new { variantId });
        }

        public async Task<Dictionary<Guid, int>> SumSoldVariants(IEnumerable<Guid> variantIds)
        {
            var ids = variantIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"
                SELECT variant_id AS VariantId, COALESCE(SUM(quantity), 0)::int AS Sold
                FROM event_extra_purchase
                WHERE variant_id = ANY(@ids) AND status IN ('paid','redeemed')
                GROUP BY variant_id";
            var rows = await _db.Query<VariantSoldRow>(sql, new { ids });
            return rows.ToDictionary(r => r.VariantId, r => r.Sold);
        }

        private record VariantSoldRow(Guid VariantId, int Sold);
    }
}
