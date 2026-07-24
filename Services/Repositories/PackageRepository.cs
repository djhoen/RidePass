using Services.Helpers.Interfaces;
using Services.Repositories.Data.PackageData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class PackageRepository : IPackageRepository
    {
        private const string ProductCols = @"
            id, tenant_id AS TenantId, name, slug, summary, description,
            hero_image_url AS HeroImageUrl, landing_published AS LandingPublished,
            includes_day_ticket AS IncludesDayTicket,
            day_ticket_event_type_code AS DayTicketEventTypeCode,
            coaching_minutes AS CoachingMinutes, coaching_label AS CoachingLabel,
            is_active AS IsActive, sort_order AS SortOrder,
            valid_from_date AS ValidFromDate, valid_to_date AS ValidToDate,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public PackageRepository(IDbHelper db) => _db = db;

        // ── Admin ────────────────────────────────────────────────────────────
        public async Task<List<PackageProduct>> ListByTenant(Guid tenantId)
        {
            var rows = await _db.Query<PackageProduct>(
                $"SELECT {ProductCols} FROM package_product WHERE tenant_id = @tenantId ORDER BY sort_order, name",
                new { tenantId });
            return rows.ToList();
        }

        public async Task<PackageProduct?> GetById(Guid id, Guid tenantId)
        {
            var p = (await _db.Query<PackageProduct>(
                $"SELECT {ProductCols} FROM package_product WHERE id = @id AND tenant_id = @tenantId LIMIT 1",
                new { id, tenantId })).FirstOrDefault();
            if (p is null) return null;
            await Hydrate(p);
            return p;
        }

        private async Task Hydrate(PackageProduct p)
        {
            p.Tiers = (await _db.Query<PackageTier>(@"
                SELECT id, package_id AS PackageId, tenant_id AS TenantId, name, price_cents AS PriceCents,
                       day_scope AS DayScope, afternoon_only AS AfternoonOnly, session_count AS SessionCount,
                       sort_order AS SortOrder, is_active AS IsActive
                FROM package_tier WHERE package_id = @id ORDER BY sort_order", new { id = p.Id })).ToList();
            p.Slots = (await _db.Query<PackageSessionSlot>(@"
                SELECT id, package_id AS PackageId, tenant_id AS TenantId, day_scope AS DayScope,
                       start_time AS StartTime, is_afternoon AS IsAfternoon, capacity,
                       instructor_id AS InstructorId, sort_order AS SortOrder, is_active AS IsActive
                FROM package_session_slot WHERE package_id = @id ORDER BY sort_order, start_time", new { id = p.Id })).ToList();
            p.Items = (await _db.Query<PackageItem>(@"
                SELECT pi.id, pi.package_id AS PackageId, pi.tenant_id AS TenantId, pi.item_type AS ItemType,
                       pi.variant_id AS VariantId, pi.quantity, pi.sort_order AS SortOrder,
                       sp.name AS VariantName,
                       NULLIF(TRIM(CONCAT_WS(' / ', v.size, v.color)), '') AS VariantLabel,
                       COALESCE(v.deposit_cents, 0) AS DepositCents
                FROM package_item pi
                JOIN shop_variant v ON v.id = pi.variant_id
                JOIN shop_product sp ON sp.id = v.product_id
                WHERE pi.package_id = @id ORDER BY pi.sort_order", new { id = p.Id })).ToList();

            // Selectable bike sizes = the rentable sibling variants of each bike item's product.
            foreach (var bike in p.Items.Where(i => i.ItemType == "bike"))
                bike.SizeOptions = (await _db.Query<PackageBikeSizeOption>(@"
                    SELECT v.id AS VariantId,
                           COALESCE(NULLIF(TRIM(CONCAT_WS(' / ', v.size, v.color)), ''), 'One size') AS Label,
                           COALESCE(v.deposit_cents, 0) AS DepositCents
                    FROM shop_variant v
                    WHERE v.tenant_id = @tenantId
                      AND v.product_id = (SELECT product_id FROM shop_variant WHERE id = @variantId AND tenant_id = @tenantId)
                      AND v.daily_rate_cents IS NOT NULL
                    ORDER BY v.size", new { tenantId = p.TenantId, variantId = bike.VariantId })).ToList();
        }

        public async Task<Guid> Create(PackageProduct p)
        {
            const string sql = @"
                INSERT INTO package_product
                    (tenant_id, name, slug, summary, description, hero_image_url, landing_published,
                     includes_day_ticket, day_ticket_event_type_code, coaching_minutes, coaching_label,
                     is_active, sort_order, valid_from_date, valid_to_date)
                VALUES (@TenantId, @Name, @Slug, @Summary, @Description, @HeroImageUrl, @LandingPublished,
                        @IncludesDayTicket, @DayTicketEventTypeCode, @CoachingMinutes, @CoachingLabel,
                        @IsActive, @SortOrder, @ValidFromDate, @ValidToDate)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task Update(PackageProduct p)
        {
            const string sql = @"
                UPDATE package_product SET
                    name = @Name, slug = @Slug, summary = @Summary, description = @Description,
                    hero_image_url = @HeroImageUrl, landing_published = @LandingPublished,
                    includes_day_ticket = @IncludesDayTicket, day_ticket_event_type_code = @DayTicketEventTypeCode,
                    coaching_minutes = @CoachingMinutes, coaching_label = @CoachingLabel,
                    is_active = @IsActive, sort_order = @SortOrder,
                    valid_from_date = @ValidFromDate, valid_to_date = @ValidToDate
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public Task Delete(Guid id, Guid tenantId) =>
            _db.Execute("DELETE FROM package_product WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId });

        public async Task ReplaceTiers(Guid packageId, Guid tenantId, IEnumerable<PackageTier> tiers)
        {
            await _db.Execute("DELETE FROM package_tier WHERE package_id = @packageId AND tenant_id = @tenantId",
                new { packageId, tenantId });
            foreach (var t in tiers)
                await _db.Execute(@"
                    INSERT INTO package_tier (package_id, tenant_id, name, price_cents, day_scope,
                        afternoon_only, session_count, sort_order, is_active)
                    VALUES (@packageId, @tenantId, @Name, @PriceCents, @DayScope, @AfternoonOnly,
                        @SessionCount, @SortOrder, @IsActive)",
                    new { packageId, tenantId, t.Name, t.PriceCents, t.DayScope, t.AfternoonOnly, t.SessionCount, t.SortOrder, t.IsActive });
        }

        public async Task ReplaceSlots(Guid packageId, Guid tenantId, IEnumerable<PackageSessionSlot> slots)
        {
            await _db.Execute("DELETE FROM package_session_slot WHERE package_id = @packageId AND tenant_id = @tenantId",
                new { packageId, tenantId });
            foreach (var s in slots)
                await _db.Execute(@"
                    INSERT INTO package_session_slot (package_id, tenant_id, day_scope, start_time,
                        is_afternoon, capacity, instructor_id, sort_order, is_active)
                    VALUES (@packageId, @tenantId, @DayScope, @StartTime, @IsAfternoon, @Capacity,
                        @InstructorId, @SortOrder, @IsActive)",
                    new { packageId, tenantId, s.DayScope, s.StartTime, s.IsAfternoon, s.Capacity, s.InstructorId, s.SortOrder, s.IsActive });
        }

        public async Task ReplaceItems(Guid packageId, Guid tenantId, IEnumerable<PackageItem> items)
        {
            await _db.Execute("DELETE FROM package_item WHERE package_id = @packageId AND tenant_id = @tenantId",
                new { packageId, tenantId });
            foreach (var it in items)
                await _db.Execute(@"
                    INSERT INTO package_item (package_id, tenant_id, item_type, variant_id, quantity, sort_order)
                    VALUES (@packageId, @tenantId, @ItemType, @VariantId, @Quantity, @SortOrder)",
                    new { packageId, tenantId, it.ItemType, it.VariantId, it.Quantity, it.SortOrder });
        }

        // ── Public ───────────────────────────────────────────────────────────
        public async Task<List<PackageProduct>> ListPublic(Guid tenantId)
        {
            var rows = await _db.Query<PackageProduct>(
                $@"SELECT {ProductCols} FROM package_product
                   WHERE tenant_id = @tenantId AND is_active = true
                   ORDER BY sort_order, name", new { tenantId });
            var list = rows.ToList();
            foreach (var p in list) await Hydrate(p);
            return list;
        }

        public async Task<PackageProduct?> GetBySlugOrId(string slugOrId, Guid tenantId)
        {
            PackageProduct? p;
            if (Guid.TryParse(slugOrId, out var id))
                p = (await _db.Query<PackageProduct>(
                    $"SELECT {ProductCols} FROM package_product WHERE id = @id AND tenant_id = @tenantId LIMIT 1",
                    new { id, tenantId })).FirstOrDefault();
            else
                p = (await _db.Query<PackageProduct>(
                    $"SELECT {ProductCols} FROM package_product WHERE lower(slug) = lower(@slug) AND tenant_id = @tenantId LIMIT 1",
                    new { slug = slugOrId, tenantId })).FirstOrDefault();
            if (p is null) return null;
            await Hydrate(p);
            return p;
        }

        // ── Booking ──────────────────────────────────────────────────────────
        public async Task<int> CountSlotBookings(Guid slotId, DateTime rideDate) =>
            (await _db.Query<int>(
                "SELECT COUNT(*) FROM package_purchase WHERE slot_id = @slotId AND ride_date = @rideDate AND status <> 'cancelled'",
                new { slotId, rideDate })).First();

        public async Task<Guid> CreatePurchase(PackagePurchase p)
        {
            const string sql = @"
                INSERT INTO package_purchase
                    (tenant_id, package_id, tier_id, buyer_user_id, buyer_name, buyer_email, ride_date,
                     session_start_at, slot_id, instructor_id, status, subtotal_cents, tax_cents, total_cents,
                     deposit_cents, service_charge_cents)
                VALUES (@TenantId, @PackageId, @TierId, @BuyerUserId, @BuyerName, @BuyerEmail, @RideDate,
                     @SessionStartAt, @SlotId, @InstructorId, @Status, @SubtotalCents, @TaxCents, @TotalCents,
                     @DepositCents, @ServiceChargeCents)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        private const string PurchaseCols = @"
            id, tenant_id AS TenantId, package_id AS PackageId, tier_id AS TierId,
            buyer_user_id AS BuyerUserId, buyer_name AS BuyerName, buyer_email AS BuyerEmail,
            ride_date AS RideDate, session_start_at AS SessionStartAt, slot_id AS SlotId,
            instructor_id AS InstructorId, status, subtotal_cents AS SubtotalCents, tax_cents AS TaxCents,
            total_cents AS TotalCents, deposit_cents AS DepositCents, service_charge_cents AS ServiceChargeCents,
            payment_intent_id AS PaymentIntentId, deposit_intent_id AS DepositIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId, order_number AS OrderNumber,
            receipt_token AS ReceiptToken, event_ticket_purchase_id AS EventTicketPurchaseId,
            shop_rental_id AS ShopRentalId, created_at AS CreatedAt, paid_at AS PaidAt";

        public async Task<PackagePurchase?> GetPurchase(Guid id, Guid tenantId) =>
            (await _db.Query<PackagePurchase>(
                $"SELECT {PurchaseCols} FROM package_purchase WHERE id = @id AND tenant_id = @tenantId LIMIT 1",
                new { id, tenantId })).FirstOrDefault();

        public async Task<PackagePurchase?> GetPurchaseByPaymentIntent(string paymentIntentId) =>
            (await _db.Query<PackagePurchase>(
                $"SELECT {PurchaseCols} FROM package_purchase WHERE payment_intent_id = @paymentIntentId LIMIT 1",
                new { paymentIntentId })).FirstOrDefault();

        public Task SetPurchasePaymentIntent(Guid id, string paymentIntentId, string? depositIntentId, string? connectedAccountId) =>
            _db.Execute(@"UPDATE package_purchase SET payment_intent_id = @paymentIntentId,
                            deposit_intent_id = @depositIntentId, stripe_connected_account_id = @connectedAccountId
                          WHERE id = @id",
                new { id, paymentIntentId, depositIntentId, connectedAccountId });

        public Task SetPurchaseArtifacts(Guid id, Guid? ticketPurchaseId, Guid? shopRentalId) =>
            _db.Execute(@"UPDATE package_purchase SET event_ticket_purchase_id = @ticketPurchaseId,
                            shop_rental_id = @shopRentalId WHERE id = @id",
                new { id, ticketPurchaseId, shopRentalId });

        public async Task<bool> TryMarkPurchasePaid(Guid id, Guid tenantId, int orderNumber)
        {
            var rows = await _db.Execute(@"
                UPDATE package_purchase SET status = 'paid', paid_at = now(), order_number = @orderNumber
                WHERE id = @id AND tenant_id = @tenantId AND status = 'pending'",
                new { id, tenantId, orderNumber });
            return rows > 0;
        }

        public Task MarkPurchaseFailed(Guid id) =>
            _db.Execute("UPDATE package_purchase SET status = 'failed' WHERE id = @id AND status = 'pending'", new { id });
    }
}
