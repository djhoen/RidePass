using Dapper;
using Services.BikeShop;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    /// <summary>
    /// Used &amp; ex-rental listings (Script0302). Every join re-asserts tenant_id rather than
    /// trusting the FK chain (one predicate per table is the rule in this codebase); shop_sale_line
    /// carries no tenant_id of its own, so sale lines are scoped through their sale. The two writes
    /// that move a unit on or off the floor run in one transaction with the unit row locked, so two
    /// cashiers cannot list the same bike twice or unlist it under a live checkout.
    /// </summary>
    public class ShopListingRepository : IShopListingRepository
    {
        private readonly IDbHelper _db;
        public ShopListingRepository(IDbHelper db) => _db = db;

        private const string PhotoCols = @"
            id, tenant_id AS TenantId, listing_id AS ListingId, image_url AS ImageUrl,
            caption, sort_order AS SortOrder, created_at AS CreatedAt";

        // The listing with everything a screen needs. The sold sale is only looked up once the unit
        // reads 'sold' (ON i.status = 'sold'), and uk_shop_sale_line_item guarantees at most one
        // line ever names a unit, so the LATERAL is a point lookup. sold_at prefers the stock
        // movement the sale-complete path wrote (that is the moment it was sold), falling back to
        // the sale's last update when depletion was skipped.
        private const string RowSelect = @"
            SELECT l.id, l.tenant_id AS TenantId, l.item_id AS ItemId, l.title,
                   l.asking_price_cents AS AskingPriceCents, l.condition_grade AS ConditionGrade,
                   l.condition_notes AS ConditionNotes, l.published, l.listed_at AS ListedAt,
                   l.listed_by_user_id AS ListedByUserId, l.reserved_sale_id AS ReservedSaleId,
                   l.reserved_at AS ReservedAt, l.unlisted_at AS UnlistedAt, l.prior_status AS PriorStatus,
                   i.label AS UnitLabel, i.serial, i.status AS ItemStatus,
                   i.acquired_cost_cents AS AcquiredCostCents,
                   v.id AS VariantId, v.size, v.color, v.gender, v.is_active AS VariantIsActive,
                   p.id AS ProductId, p.name AS ProductName, p.image_url AS ProductImageUrl,
                   p.condition AS ProductCondition, p.is_active AS ProductIsActive,
                   c.name AS CategoryName,
                   NULLIF(btrim(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), '') AS ListedByName,
                   rs.status AS ReservedSaleStatus, rs.buyer_user_id AS ReservedBuyerUserId,
                   ss.id AS SoldSaleId, ss.order_number AS SoldOrderNumber, ss.sold_at AS SoldAt
            FROM shop_item_listing l
            JOIN shop_item i ON i.id = l.item_id AND i.tenant_id = l.tenant_id
            JOIN shop_variant v ON v.id = i.variant_id AND v.tenant_id = l.tenant_id
            JOIN shop_product p ON p.id = v.product_id AND p.tenant_id = l.tenant_id
            LEFT JOIN shop_category c ON c.id = p.category_id AND c.tenant_id = l.tenant_id
            LEFT JOIN users u ON u.id = l.listed_by_user_id
            LEFT JOIN shop_sale rs ON rs.id = l.reserved_sale_id AND rs.tenant_id = l.tenant_id
            LEFT JOIN LATERAL (
                SELECT s.id, s.order_number,
                       COALESCE((SELECT min(m.created_at) FROM shop_stock_movement m
                                 WHERE m.tenant_id = l.tenant_id AND m.item_id = l.item_id
                                   AND m.reason = 'sale' AND m.reference_id = s.id), s.updated_at) AS sold_at
                FROM shop_sale_line sl
                JOIN shop_sale s ON s.id = sl.sale_id AND s.tenant_id = l.tenant_id
                WHERE sl.item_id = l.item_id AND s.status IN ('paid', 'refunded')
                ORDER BY s.created_at DESC
                LIMIT 1
            ) ss ON i.status = 'sold'";

        // ── Reads ─────────────────────────────────────────────────────────────────
        public async Task<List<ShopListingRow>> List(Guid tenantId, string status)
        {
            // 'unlisted' also catches a stranded open row whose unit left the floor some other way
            // (an admin edit moved it, a refund returned it to 'available'): it is off the floor,
            // whatever unlisted_at says, and ListingRules.State reads it the same way.
            var where = status switch
            {
                "active" => " AND l.unlisted_at IS NULL AND i.status = 'for_sale'",
                "sold" => " AND l.unlisted_at IS NULL AND i.status = 'sold'",
                "unlisted" => " AND (l.unlisted_at IS NOT NULL OR i.status NOT IN ('for_sale', 'sold'))",
                _ => "",
            };
            var rows = (await _db.Query<ShopListingRow>(
                RowSelect + " WHERE l.tenant_id = @tenantId" + where + " ORDER BY l.listed_at DESC, l.id",
                new { tenantId })).ToList();
            await AttachPhotos(rows, tenantId);
            return rows;
        }

        public async Task<ShopListingRow?> Get(Guid id, Guid tenantId)
        {
            var row = (await _db.Query<ShopListingRow>(
                RowSelect + " WHERE l.id = @id AND l.tenant_id = @tenantId", new { id, tenantId })).FirstOrDefault();
            if (row is null) return null;
            await AttachPhotos(new List<ShopListingRow> { row }, tenantId);
            return row;
        }

        public async Task<ShopListingRow?> GetOpenForItem(Guid itemId, Guid tenantId) =>
            (await _db.Query<ShopListingRow>(
                RowSelect + " WHERE l.tenant_id = @tenantId AND l.item_id = @itemId AND l.unlisted_at IS NULL",
                new { itemId, tenantId })).FirstOrDefault();

        public async Task<List<ShopListingRow>> ListPublic(Guid tenantId)
        {
            var rows = (await _db.Query<ShopListingRow>(RowSelect + @"
                WHERE l.tenant_id = @tenantId AND l.unlisted_at IS NULL AND l.published = true
                  AND i.status = 'for_sale' AND p.is_active = true AND v.is_active = true
                ORDER BY l.listed_at DESC, l.id", new { tenantId })).ToList();
            await AttachPhotos(rows, tenantId);
            return rows;
        }

        public async Task<List<ShopListingRow>> ListActiveForItems(IEnumerable<Guid> itemIds, Guid tenantId)
        {
            var ids = itemIds.Distinct().ToArray();
            if (ids.Length == 0) return new List<ShopListingRow>();
            return (await _db.Query<ShopListingRow>(RowSelect + @"
                WHERE l.tenant_id = @tenantId AND l.item_id = ANY(@ids)
                  AND l.unlisted_at IS NULL AND i.status = 'for_sale'", new { tenantId, ids })).ToList();
        }

        public async Task<ShopListingItemContext?> GetItemContext(Guid itemId, Guid tenantId) =>
            (await _db.Query<ShopListingItemContext>(@"
                SELECT i.id AS ItemId, i.status AS ItemStatus, i.label AS UnitLabel,
                       i.acquired_cost_cents AS AcquiredCostCents,
                       v.id AS VariantId, v.size, v.color, v.gender,
                       p.id AS ProductId, p.name AS ProductName
                FROM shop_item i
                JOIN shop_variant v ON v.id = i.variant_id AND v.tenant_id = i.tenant_id
                JOIN shop_product p ON p.id = v.product_id AND p.tenant_id = i.tenant_id
                WHERE i.id = @itemId AND i.tenant_id = @tenantId", new { itemId, tenantId })).FirstOrDefault();

        public async Task<Dictionary<Guid, string>> ListProductConditions(Guid tenantId) =>
            (await _db.Query<Guid>(@"
                SELECT id FROM shop_product WHERE tenant_id = @tenantId AND condition = 'used'", new { tenantId }))
            .ToDictionary(id => id, _ => "used");

        public async Task<List<Guid>> ListUsedProductIds(Guid tenantId) =>
            (await _db.Query<Guid>(@"
                SELECT id FROM shop_product
                WHERE tenant_id = @tenantId AND condition = 'used'
                  AND is_active = true AND is_sellable = true AND is_published = true
                ORDER BY sort_order, name", new { tenantId })).ToList();

        private async Task AttachPhotos(List<ShopListingRow> rows, Guid tenantId)
        {
            if (rows.Count == 0) return;
            var ids = rows.Select(r => r.Id).ToArray();
            var photos = await _db.Query<ShopListingPhoto>($@"
                SELECT {PhotoCols} FROM shop_listing_photo
                WHERE tenant_id = @tenantId AND listing_id = ANY(@ids)
                ORDER BY sort_order, created_at", new { tenantId, ids });
            var byListing = photos.GroupBy(p => p.ListingId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var r in rows) r.Photos = byListing.GetValueOrDefault(r.Id) ?? new List<ShopListingPhoto>();
        }

        // ── Listing writes ────────────────────────────────────────────────────────
        private sealed class ItemLock
        {
            public Guid Id { get; set; }
            public string Status { get; set; } = null!;
        }

        private sealed class OpenLock
        {
            public Guid Id { get; set; }
            public Guid? ReservedSaleId { get; set; }
        }

        // Script0303's trigger refuses any move of a 'for_sale' unit off the floor unless this
        // transaction-local setting is on; only the listing's own transactions turn it on.
        private const string AllowListingChange = "SELECT set_config('ridepass.listing_change', 'on', true)";

        // Lock order, both writes: the unit (shop_item) FIRST, then its listing row(s), so Create and
        // Unlist on the same unit queue behind each other instead of deadlocking.
        public Task<ListingWriteResult> Create(ShopItemListing listing) => _db.WithTransaction(async (conn, tx) =>
        {
            await conn.ExecuteAsync(AllowListingChange, transaction: tx);
            var p = new { itemId = listing.ItemId, tenantId = listing.TenantId };
            // Row lock on the unit: a second List click, or a rental going out at the same moment,
            // waits here and then sees the new status.
            var item = await conn.QueryFirstOrDefaultAsync<ItemLock>(
                "SELECT id, status FROM shop_item WHERE id = @itemId AND tenant_id = @tenantId FOR UPDATE", p, tx);
            if (item is null) return ListingWriteResult.NotFound("Unit not found.");

            var open = await conn.QueryFirstOrDefaultAsync<OpenLock>(@"
                SELECT id, reserved_sale_id AS ReservedSaleId FROM shop_item_listing
                WHERE tenant_id = @tenantId AND item_id = @itemId AND unlisted_at IS NULL
                FOR UPDATE", p, tx);

            if (item.Status == ListingRules.ForSale && open is not null)
                return ListingWriteResult.Conflict(ListingRules.CannotListReason(item.Status));
            if (item.Status != ListingRules.ForSale && !ListingRules.CanList(item.Status))
                return ListingWriteResult.Conflict(ListingRules.CannotListReason(item.Status));

            // A booking that has not ended still owns the bike for its window, even while it sits
            // 'available' on the rack today: selling it would strand a paid (or paying) renter.
            // shop_rental_line has no tenant_id of its own; it is scoped through its rental.
            var bookedFrom = await conn.QueryFirstOrDefaultAsync<DateTime?>(@"
                SELECT r.starts_at
                FROM shop_rental_line rl
                JOIN shop_rental r ON r.id = rl.rental_id AND r.tenant_id = @tenantId
                WHERE rl.item_id = @itemId
                  AND r.status IN ('pending', 'paid', 'out')
                  AND r.ends_at > now()
                ORDER BY r.starts_at
                LIMIT 1", p, tx);
            if (bookedFrom is not null) return ListingWriteResult.RentalBooked(bookedFrom.Value);

            // A stranded open row (the unit left the floor some other way) is closed, not reused:
            // the new listing is a new stay on the floor with its own price and photos. A hold on it
            // must have been RELEASED first (IListingReservationService); this never clears one.
            if (open is not null)
            {
                if (open.ReservedSaleId is not null)
                    return ListingWriteResult.Conflict("An online checkout still holds this bike. Try again in a few minutes.");
                await conn.ExecuteAsync(@"
                    UPDATE shop_item_listing SET unlisted_at = now()
                    WHERE id = @id AND tenant_id = @tenantId", new { id = open.Id, tenantId = listing.TenantId }, tx);
            }

            // Where the unit goes back to if this listing comes down. A stranded 'for_sale' unit
            // with no open listing inherits whatever its last listing came from.
            listing.PriorStatus = item.Status != ListingRules.ForSale
                ? item.Status
                : ListingRules.RestoreStatusOnUnlist(await conn.QueryFirstOrDefaultAsync<string?>(@"
                    SELECT prior_status FROM shop_item_listing
                    WHERE tenant_id = @tenantId AND item_id = @itemId
                    ORDER BY listed_at DESC LIMIT 1", p, tx));

            var id = await conn.QuerySingleAsync<Guid>(@"
                INSERT INTO shop_item_listing
                    (tenant_id, item_id, title, asking_price_cents, condition_grade, condition_notes,
                     published, listed_by_user_id, prior_status)
                VALUES (@TenantId, @ItemId, @Title, @AskingPriceCents, @ConditionGrade, @ConditionNotes,
                        @Published, @ListedByUserId, @PriorStatus)
                RETURNING id", listing, tx);

            await conn.ExecuteAsync(@"
                UPDATE shop_item SET status = 'for_sale', updated_at = now()
                WHERE id = @itemId AND tenant_id = @tenantId",
                new { itemId = listing.ItemId, tenantId = listing.TenantId }, tx);
            return ListingWriteResult.Ok(id);
        });

        // Only an open listing whose unit is still on the floor takes edits: a sold listing is the
        // record of that sale, and an unlisted (or stranded) one is history. 0 rows = not editable or
        // not found; the caller tells them apart.
        public Task<int> Update(ShopItemListing listing) => _db.Execute(@"
            UPDATE shop_item_listing l
            SET title = @Title, asking_price_cents = @AskingPriceCents, condition_grade = @ConditionGrade,
                condition_notes = @ConditionNotes, published = @Published
            WHERE l.id = @Id AND l.tenant_id = @TenantId AND l.unlisted_at IS NULL
              AND EXISTS (SELECT 1 FROM shop_item i
                          WHERE i.id = l.item_id AND i.tenant_id = @TenantId AND i.status = 'for_sale')", listing);

        private sealed class UnlistLock
        {
            public Guid Id { get; set; }
            public Guid ItemId { get; set; }
            public DateTime? UnlistedAt { get; set; }
            public Guid? ReservedSaleId { get; set; }
            public string? PriorStatus { get; set; }
            public string ItemStatus { get; set; } = null!;
        }

        public Task<ListingWriteResult> Unlist(Guid id, Guid tenantId) => _db.WithTransaction(async (conn, tx) =>
        {
            await conn.ExecuteAsync(AllowListingChange, transaction: tx);
            // Same lock order as Create: the unit first (found through the listing, unlocked), then
            // the listing row.
            var itemLocked = await conn.QueryFirstOrDefaultAsync<Guid?>(@"
                SELECT i.id FROM shop_item i
                WHERE i.tenant_id = @tenantId
                  AND i.id = (SELECT l.item_id FROM shop_item_listing l WHERE l.id = @id AND l.tenant_id = @tenantId)
                FOR UPDATE", new { id, tenantId }, tx);
            if (itemLocked is null) return ListingWriteResult.NotFound("Listing not found.");
            var row = await conn.QueryFirstOrDefaultAsync<UnlistLock>(@"
                SELECT l.id, l.item_id AS ItemId, l.unlisted_at AS UnlistedAt,
                       l.reserved_sale_id AS ReservedSaleId, l.prior_status AS PriorStatus,
                       i.status AS ItemStatus
                FROM shop_item_listing l
                JOIN shop_item i ON i.id = l.item_id AND i.tenant_id = l.tenant_id
                WHERE l.id = @id AND l.tenant_id = @tenantId
                FOR UPDATE OF l", new { id, tenantId }, tx);
            if (row is null) return ListingWriteResult.NotFound("Listing not found.");
            if (row.UnlistedAt is not null) return ListingWriteResult.Ok(row.Id);
            if (row.ItemStatus == "sold")
                return ListingWriteResult.Conflict("This unit has been sold; the listing stays as the record of that sale.");
            // A hold (live or stale) is released by IListingReservationService before this runs;
            // one still here means a checkout arrived in between, and it wins.
            if (row.ReservedSaleId is not null)
                return ListingWriteResult.Conflict("Someone is checking out this bike online right now. Try again in a few minutes.");

            await conn.ExecuteAsync(@"
                UPDATE shop_item_listing SET unlisted_at = now()
                WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId }, tx);
            // Back where it came from: the fleet, the bench or retirement. Only a unit still on the
            // floor moves; a unit an admin already moved elsewhere stays where they put it.
            await conn.ExecuteAsync(@"
                UPDATE shop_item SET status = @restore, updated_at = now()
                WHERE id = @itemId AND tenant_id = @tenantId AND status = 'for_sale'",
                new { itemId = row.ItemId, tenantId, restore = ListingRules.RestoreStatusOnUnlist(row.PriorStatus) }, tx);
            return ListingWriteResult.Ok(row.Id);
        });

        // ── Online reservation ────────────────────────────────────────────────────
        public async Task<bool> Reserve(Guid listingId, Guid tenantId, Guid saleId) =>
            await _db.Execute(@"
                UPDATE shop_item_listing SET reserved_sale_id = @saleId, reserved_at = now()
                WHERE id = @listingId AND tenant_id = @tenantId AND unlisted_at IS NULL",
                new { listingId, tenantId, saleId }) > 0;

        public Task<int> ClearReservation(Guid listingId, Guid tenantId, Guid saleId) => _db.Execute(@"
            UPDATE shop_item_listing SET reserved_sale_id = NULL, reserved_at = NULL
            WHERE id = @listingId AND tenant_id = @tenantId AND reserved_sale_id = @saleId",
            new { listingId, tenantId, saleId });

        // Only item_id is nulled; the line, its price and the sale's history stay exactly as they were.
        public Task<int> DetachDeadSaleLine(Guid saleId, Guid itemId, Guid tenantId) => _db.Execute(@"
            UPDATE shop_sale_line sl SET item_id = NULL
            FROM shop_sale s
            WHERE s.id = sl.sale_id AND s.tenant_id = @tenantId AND s.id = @saleId
              AND sl.item_id = @itemId AND s.status IN ('failed', 'abandoned', 'cancelled', 'refunded')",
            new { saleId, itemId, tenantId });

        public async Task<List<ShopDeadSaleLine>> ListDeadSaleLinesForItem(Guid itemId, Guid tenantId) =>
            (await _db.Query<ShopDeadSaleLine>(@"
                SELECT s.id AS SaleId, sl.id AS LineId, s.status AS SaleStatus,
                       s.stripe_payment_intent_id AS StripePaymentIntentId,
                       s.stripe_connected_account_id AS StripeConnectedAccountId
                FROM shop_sale_line sl
                JOIN shop_sale s ON s.id = sl.sale_id AND s.tenant_id = @tenantId
                WHERE sl.item_id = @itemId
                  AND s.status IN ('failed', 'abandoned', 'cancelled', 'refunded')
                ORDER BY s.created_at", new { itemId, tenantId })).ToList();

        // ── Sale status, tenant-scoped ────────────────────────────────────────────
        public Task<int> MarkSaleDead(Guid saleId, Guid tenantId, string status) => _db.Execute(@"
            UPDATE shop_sale SET status = @status, updated_at = now()
            WHERE id = @saleId AND tenant_id = @tenantId AND status = 'pending'",
            new { saleId, tenantId, status = status == "failed" ? "failed" : "abandoned" });

        public Task<int> SetSalePaymentIntent(Guid saleId, Guid tenantId, string paymentIntentId) => _db.Execute(@"
            UPDATE shop_sale SET stripe_payment_intent_id = @paymentIntentId, updated_at = now()
            WHERE id = @saleId AND tenant_id = @tenantId",
            new { saleId, tenantId, paymentIntentId });

        // ── Photos ────────────────────────────────────────────────────────────────
        public async Task<List<ShopListingPhoto>> ListPhotos(Guid listingId, Guid tenantId) =>
            (await _db.Query<ShopListingPhoto>($@"
                SELECT {PhotoCols} FROM shop_listing_photo
                WHERE listing_id = @listingId AND tenant_id = @tenantId
                ORDER BY sort_order, created_at", new { listingId, tenantId })).ToList();

        public Task<int> CountPhotos(Guid listingId, Guid tenantId) => _db.ExecuteScalar(
            "SELECT count(*) FROM shop_listing_photo WHERE listing_id = @listingId AND tenant_id = @tenantId",
            new { listingId, tenantId });

        public Task<(ShopListingPhoto? Photo, bool LimitReached)> AddPhoto(ShopListingPhoto photo, int maxPhotos) =>
            _db.WithTransaction<(ShopListingPhoto?, bool)>(async (conn, tx) =>
            {
                // The listing row lock serializes uploads to one listing, so the count and the insert
                // below are one decision: two uploads at once cannot both take the twelfth slot.
                var listing = await conn.QueryFirstOrDefaultAsync<Guid?>(
                    "SELECT id FROM shop_item_listing WHERE id = @ListingId AND tenant_id = @TenantId FOR UPDATE", photo, tx);
                if (listing is null) return (null, false);
                var count = await conn.ExecuteScalarAsync<int>(
                    "SELECT count(*) FROM shop_listing_photo WHERE listing_id = @ListingId AND tenant_id = @TenantId", photo, tx);
                if (count >= maxPhotos) return (null, true);
                // Position is computed server-side (max + 10), under the same lock.
                var saved = await conn.QuerySingleAsync<ShopListingPhoto>($@"
                    INSERT INTO shop_listing_photo (tenant_id, listing_id, image_url, caption, sort_order)
                    SELECT @TenantId, @ListingId, @ImageUrl, @Caption,
                           COALESCE((SELECT max(sort_order) FROM shop_listing_photo
                                     WHERE tenant_id = @TenantId AND listing_id = @ListingId), 0) + 10
                    RETURNING {PhotoCols}", photo, tx);
                return (saved, false);
            });

        public async Task<string?> DeletePhoto(Guid photoId, Guid listingId, Guid tenantId) =>
            (await _db.Query<string>(@"
                DELETE FROM shop_listing_photo
                WHERE id = @photoId AND listing_id = @listingId AND tenant_id = @tenantId
                RETURNING image_url", new { photoId, listingId, tenantId })).FirstOrDefault();

        public async Task<int> ReorderPhotos(Guid listingId, Guid tenantId, IReadOnlyList<Guid> photoIds)
        {
            if (photoIds.Count == 0) return 0;
            var stmts = new List<(string Sql, object? Param)>();
            for (var i = 0; i < photoIds.Count; i++)
            {
                stmts.Add((@"
                    UPDATE shop_listing_photo SET sort_order = @sortOrder
                    WHERE id = @id AND listing_id = @listingId AND tenant_id = @tenantId",
                    new { id = photoIds[i], listingId, tenantId, sortOrder = (i + 1) * 10 }));
            }
            await _db.ExecuteBatch(stmts);
            return photoIds.Count;
        }

        // ── Product condition ─────────────────────────────────────────────────────
        public Task<int> SetProductCondition(Guid productId, Guid tenantId, string condition) => _db.Execute(@"
            UPDATE shop_product SET condition = @condition, updated_at = now()
            WHERE id = @productId AND tenant_id = @tenantId", new { productId, tenantId, condition });
    }
}
