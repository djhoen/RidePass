using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class SeasonPassRepository : ISeasonPassRepository
    {
        private const string ProductColumns = @"
            id, tenant_id AS TenantId, name, description,
            price_cents AS PriceCents,
            valid_from_date AS ValidFromDate,
            valid_to_date AS ValidToDate,
            kind,
            valid_days_of_week AS ValidDaysOfWeek,
            total_credits AS TotalCredits,
            requires_waiver AS RequiresWaiver,
            rider_paid_service_charge_bps AS RiderPaidServiceChargeBps,
            is_active AS IsActive,
            sort_order AS SortOrder,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string PurchaseColumns = @"
            id, tenant_id AS TenantId, purchaser_user_id AS PurchaserUserId,
            product_id AS ProductId, waiver_signature_id AS WaiverSignatureId,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            amount_cents AS AmountCents, service_charge_cents AS ServiceChargeCents,
            payment_method AS PaymentMethod, status,
            purchaser_email AS PurchaserEmail, purchaser_name AS PurchaserName,
            redemption_token AS RedemptionToken,
            valid_from_date AS ValidFromDate, valid_to_date AS ValidToDate,
            credits_remaining AS CreditsRemaining,
            cancellation_reason AS CancellationReason,
            cancelled_at AS CancelledAt, cancelled_by_user_id AS CancelledByUserId,
            refund_note AS RefundNote,
            photo_data_url AS PhotoDataUrl,
            holder_first_name AS HolderFirstName, holder_last_name AS HolderLastName,
            holder_birthdate AS HolderBirthdate,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ReservationColumns = @"
            id, season_pass_purchase_id AS SeasonPassPurchaseId,
            event_id AS EventId, status,
            reserved_at AS ReservedAt,
            checked_in_at AS CheckedInAt,
            cancelled_at AS CancelledAt";

        private readonly IDbHelper _db;

        public SeasonPassRepository(IDbHelper db) => _db = db;

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason)
        {
            const string sql = @"
                UPDATE season_pass_purchase
                SET status = 'cancelled',
                    cancellation_reason = @reason,
                    cancelled_at = now(),
                    cancelled_by_user_id = @cancelledByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            await _db.Execute(sql, new { id, tenantId, cancelledByUserId, reason });
        }

        public async Task MarkRefunded(Guid id, string? refundNote)
        {
            const string sql = "UPDATE season_pass_purchase SET status = 'refunded', refund_note = @refundNote WHERE id = @id";
            await _db.Execute(sql, new { id, refundNote });
        }

        public async Task<List<SeasonPassProduct>> ListProductsForTenant(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? " AND is_active = true" : "";
            var sql = $@"
                SELECT {ProductColumns}
                FROM season_pass_product
                WHERE tenant_id = @tenantId {filter}
                ORDER BY sort_order, name";
            return (await _db.Query<SeasonPassProduct>(sql, new { tenantId })).ToList();
        }

        public async Task<SeasonPassProduct?> GetProduct(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {ProductColumns} FROM season_pass_product WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<SeasonPassProduct>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateProduct(SeasonPassProduct p)
        {
            const string sql = @"
                INSERT INTO season_pass_product
                    (tenant_id, name, description, price_cents,
                     valid_from_date, valid_to_date, kind, valid_days_of_week, total_credits,
                     requires_waiver, rider_paid_service_charge_bps, is_active, sort_order)
                VALUES
                    (@TenantId, @Name, @Description, @PriceCents,
                     @ValidFromDate, @ValidToDate, @Kind, @ValidDaysOfWeek, @TotalCredits,
                     @RequiresWaiver, @RiderPaidServiceChargeBps, @IsActive, @SortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task UpdateProduct(SeasonPassProduct p)
        {
            const string sql = @"
                UPDATE season_pass_product
                SET name = @Name, description = @Description, price_cents = @PriceCents,
                    valid_from_date = @ValidFromDate, valid_to_date = @ValidToDate,
                    kind = @Kind, valid_days_of_week = @ValidDaysOfWeek, total_credits = @TotalCredits,
                    requires_waiver = @RequiresWaiver,
                    rider_paid_service_charge_bps = @RiderPaidServiceChargeBps,
                    is_active = @IsActive, sort_order = @SortOrder,
                    updated_at = now()
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task DeleteProduct(Guid id, Guid tenantId)
        {
            // ON DELETE RESTRICT from season_pass_purchase will block delete if purchases exist.
            const string sql = "DELETE FROM season_pass_product WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE season_pass_product AS p
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

        public async Task<List<SeasonPassEventTypePerk>> ListPerks(Guid passProductId)
        {
            const string sql = @"
                SELECT id, pass_product_id AS PassProductId, event_type_id AS EventTypeId,
                       discount_percent AS DiscountPercent
                FROM season_pass_event_type_perk
                WHERE pass_product_id = @passProductId";
            return (await _db.Query<SeasonPassEventTypePerk>(sql, new { passProductId })).ToList();
        }

        public async Task ReplacePerks(Guid passProductId, IEnumerable<SeasonPassEventTypePerk> perks)
        {
            await _db.Execute("DELETE FROM season_pass_event_type_perk WHERE pass_product_id = @passProductId",
                new { passProductId });
            foreach (var perk in perks)
            {
                const string sql = @"
                    INSERT INTO season_pass_event_type_perk (pass_product_id, event_type_id, discount_percent)
                    VALUES (@PassProductId, @EventTypeId, @DiscountPercent)
                    ON CONFLICT (pass_product_id, event_type_id) DO UPDATE
                    SET discount_percent = EXCLUDED.discount_percent";
                await _db.Execute(sql, new { PassProductId = passProductId, perk.EventTypeId, perk.DiscountPercent });
            }
        }

        private const string BenefitColumns = @"
            id, tenant_id AS TenantId, pass_product_id AS PassProductId,
            benefit_type AS BenefitType, scope_id AS ScopeId,
            discount_kind AS DiscountKind, discount_value AS DiscountValue,
            quantity";

        public async Task<List<SeasonPassBenefit>> ListBenefits(Guid passProductId, Guid tenantId)
        {
            var sql = $@"
                SELECT {BenefitColumns}
                FROM season_pass_benefit
                WHERE pass_product_id = @passProductId AND tenant_id = @tenantId
                ORDER BY benefit_type, discount_value DESC";
            return (await _db.Query<SeasonPassBenefit>(sql, new { passProductId, tenantId })).ToList();
        }

        public async Task<Dictionary<Guid, List<SeasonPassBenefit>>> ListBenefitsForProducts(
            IEnumerable<Guid> passProductIds, Guid tenantId)
        {
            var ids = passProductIds.Distinct().ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, List<SeasonPassBenefit>>();
            var sql = $@"
                SELECT {BenefitColumns}
                FROM season_pass_benefit
                WHERE pass_product_id = ANY(@ids) AND tenant_id = @tenantId
                ORDER BY benefit_type, discount_value DESC";
            var rows = await _db.Query<SeasonPassBenefit>(sql, new { ids, tenantId });
            return rows.GroupBy(b => b.PassProductId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task ReplaceBenefits(Guid passProductId, Guid tenantId, IEnumerable<SeasonPassBenefit> benefits)
        {
            // Tenant-scoped delete: without it a spoofed product id from another tenant would wipe
            // that tenant's benefits before the insert failed.
            await _db.Execute(
                "DELETE FROM season_pass_benefit WHERE pass_product_id = @passProductId AND tenant_id = @tenantId",
                new { passProductId, tenantId });
            foreach (var b in benefits)
            {
                const string sql = @"
                    INSERT INTO season_pass_benefit
                        (tenant_id, pass_product_id, benefit_type, scope_id, discount_kind, discount_value, quantity)
                    VALUES (@tenantId, @passProductId, @BenefitType, @ScopeId, @DiscountKind, @DiscountValue, @Quantity)
                    ON CONFLICT DO NOTHING";
                await _db.Execute(sql, new
                {
                    tenantId,
                    passProductId,
                    b.BenefitType,
                    b.ScopeId,
                    b.DiscountKind,
                    b.DiscountValue,
                    b.Quantity,
                });
            }
        }

        public async Task<List<SeasonPassBenefitGrant>> ListActiveBenefitGrantsForUser(
            Guid userId, Guid tenantId, string benefitType, Guid? scopeId, DateTime onDateUtc)
        {
            // One row per (pass, matching benefit). The scope filter accepts a benefit scoped to
            // this exact scope OR one with no scope (the whole surface).
            //
            // "Active" is deliberately strict, mirroring what the gate would accept on the day:
            //   * status = 'paid'                — a pending/refunded pass grants nothing
            //   * photo present                  — unregistered passes aren't usable (Script0176)
            //   * waiver signed when required    — same rule as check-in
            //   * the event date inside the pass's validity window
            //   * the day-of-week rule for days_of_week products
            // Without these a rider could buy an unregistered pass and immediately claim free entry
            // at a race their pass isn't valid for.
            //
            // Credits products grant NOTHING here, deliberately. A credit pass is "N rides", so a
            // benefit it grants has to burn a credit — and burning one safely means recording which
            // pass paid for which ticket, so a failed or refunded charge hands the credit back.
            // That accounting doesn't exist yet, and without it a 10-credit pass would hand out
            // UNLIMITED free entry (the count never drops). Excluding them is the safe half: a
            // credits pass simply offers no event benefit until the burn is wired.
            // Columns are spelled out rather than reusing BenefitColumns: the joins make the
            // shared names (id, tenant_id, quantity) ambiguous, and Dapper's splitOn needs the
            // benefit's own id to start the second object.
            const string sql = @"
                SELECT sp.id                AS PassPurchaseId,
                       sp.product_id        AS PassProductId,
                       p.name               AS ProductName,
                       p.kind               AS ProductKind,
                       sp.credits_remaining AS CreditsRemaining,
                       b.id,
                       b.tenant_id      AS TenantId,
                       b.pass_product_id AS PassProductId,
                       b.benefit_type   AS BenefitType,
                       b.scope_id       AS ScopeId,
                       b.discount_kind  AS DiscountKind,
                       b.discount_value AS DiscountValue,
                       b.quantity
                FROM season_pass_purchase sp
                JOIN season_pass_product p ON p.id = sp.product_id
                JOIN season_pass_benefit b ON b.pass_product_id = sp.product_id
                WHERE sp.purchaser_user_id = @userId
                  AND sp.tenant_id = @tenantId
                  AND sp.status = 'paid'
                  AND sp.photo_data_url IS NOT NULL
                  AND (p.requires_waiver = false OR sp.waiver_signature_id IS NOT NULL)
                  AND @onDate::date BETWEEN sp.valid_from_date AND sp.valid_to_date
                  AND (p.kind <> 'days_of_week'
                       OR p.valid_days_of_week IS NULL
                       OR EXTRACT(DOW FROM @onDate::date)::int = ANY(p.valid_days_of_week))
                  AND p.kind <> 'credits'
                  AND b.benefit_type = @benefitType
                  AND (b.scope_id = @scopeId OR b.scope_id IS NULL)
                ORDER BY sp.created_at";
            var rows = await _db.Query<SeasonPassBenefitGrant, SeasonPassBenefit, SeasonPassBenefitGrant>(
                sql, (grant, benefit) => { grant.Benefit = benefit; return grant; },
                new { userId, tenantId, benefitType, scopeId, onDate = onDateUtc.Date },
                splitOn: "id");

            // A pass whose product carries BOTH a type-scoped benefit and a whole-surface one
            // matches twice. The scoped row wins: "10% off Race" alongside "50% off all events"
            // reads as a deliberate override for races, not an accident, so specificity beats
            // size (a tie on specificity falls back to the bigger discount). One grant per pass
            // means the caller can treat grant count as "how many tickets can be discounted".
            return rows
                .GroupBy(g => g.PassPurchaseId)
                .Select(g => g.OrderByDescending(x => x.Benefit.ScopeId.HasValue ? 1 : 0)
                              .ThenByDescending(x => x.Benefit.DiscountValue)
                              .First())
                .ToList();
        }

        public async Task<(Guid Id, Guid RedemptionToken)> CreatePurchase(SeasonPassPurchase p)
        {
            const string sql = @"
                INSERT INTO season_pass_purchase
                    (tenant_id, purchaser_user_id, product_id, waiver_signature_id,
                     amount_cents, service_charge_cents, payment_method, status,
                     purchaser_email, purchaser_name,
                     valid_from_date, valid_to_date, credits_remaining,
                     photo_data_url)
                VALUES
                    (@TenantId, @PurchaserUserId, @ProductId, @WaiverSignatureId,
                     @AmountCents, @ServiceChargeCents, @PaymentMethod, @Status,
                     @PurchaserEmail, @PurchaserName,
                     @ValidFromDate, @ValidToDate, @CreditsRemaining,
                     @PhotoDataUrl)
                RETURNING id, redemption_token AS RedemptionToken";
            var row = (await _db.Query<SeasonPassPurchase>(sql, p)).First();
            return (row.Id, row.RedemptionToken);
        }

        public async Task<SeasonPassPurchase?> GetPurchase(Guid id)
        {
            var sql = $"SELECT {PurchaseColumns} FROM season_pass_purchase WHERE id = @id LIMIT 1";
            return (await _db.Query<SeasonPassPurchase>(sql, new { id })).FirstOrDefault();
        }

        public async Task<SeasonPassPurchase?> GetPurchaseByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {PurchaseColumns} FROM season_pass_purchase WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<SeasonPassPurchase>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task<List<SeasonPassPurchase>> ListPurchasesByStripePaymentIntentId(string paymentIntentId)
        {
            // One checkout can put several passes on a single PaymentIntent (a parent buying for
            // three kids), so finalization and refunds must see every row, not just the first.
            // Ordered by created_at so the caller's per-pass output is stable across calls.
            var sql = $@"
                SELECT {PurchaseColumns}
                FROM season_pass_purchase
                WHERE stripe_payment_intent_id = @paymentIntentId
                ORDER BY created_at";
            return (await _db.Query<SeasonPassPurchase>(sql, new { paymentIntentId })).ToList();
        }

        public async Task<SeasonPassPurchase?> GetPurchaseByRedemptionToken(Guid token)
        {
            var sql = $"SELECT {PurchaseColumns} FROM season_pass_purchase WHERE redemption_token = @token LIMIT 1";
            return (await _db.Query<SeasonPassPurchase>(sql, new { token })).FirstOrDefault();
        }

        public async Task<List<SeasonPassPurchaseWithContext>> ListMine(Guid userId, Guid tenantId)
        {
            // Columns are spelled out rather than reusing PurchaseColumns because the product join
            // makes the shared names (id, status, name) ambiguous — they need the sp. qualifier.
            var sql = $@"
                SELECT
                    sp.id, sp.tenant_id AS TenantId, sp.purchaser_user_id AS PurchaserUserId,
                    sp.product_id AS ProductId, sp.waiver_signature_id AS WaiverSignatureId,
                    sp.stripe_payment_intent_id AS StripePaymentIntentId,
                    sp.stripe_connected_account_id AS StripeConnectedAccountId,
                    sp.amount_cents AS AmountCents, sp.service_charge_cents AS ServiceChargeCents,
                    sp.payment_method AS PaymentMethod, sp.status,
                    sp.purchaser_email AS PurchaserEmail, sp.purchaser_name AS PurchaserName,
                    sp.redemption_token AS RedemptionToken,
                    sp.valid_from_date AS ValidFromDate, sp.valid_to_date AS ValidToDate,
                    sp.credits_remaining AS CreditsRemaining,
                    sp.cancellation_reason AS CancellationReason,
                    sp.cancelled_at AS CancelledAt, sp.cancelled_by_user_id AS CancelledByUserId,
                    sp.refund_note AS RefundNote,
                    sp.photo_data_url AS PhotoDataUrl,
                    sp.holder_first_name AS HolderFirstName, sp.holder_last_name AS HolderLastName,
                    sp.holder_birthdate AS HolderBirthdate,
                    sp.created_at AS CreatedAt, sp.updated_at AS UpdatedAt,
                    p.name AS ProductName,
                    p.kind AS ProductKind,
                    p.total_credits AS ProductTotalCredits,
                    p.valid_days_of_week AS ProductValidDaysOfWeek,
                    p.requires_waiver AS ProductRequiresWaiver
                FROM season_pass_purchase sp
                JOIN season_pass_product p ON p.id = sp.product_id
                WHERE sp.purchaser_user_id = @userId AND sp.tenant_id = @tenantId
                ORDER BY sp.created_at DESC";
            return (await _db.Query<SeasonPassPurchaseWithContext>(sql, new { userId, tenantId })).ToList();
        }

        public async Task SetPurchaseStripePaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE season_pass_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        // Direct charge: snapshot the connected account this pass was charged on and flag the row.
        public async Task MarkPurchaseDirectCharge(Guid id, Guid tenantId, string connectedAccountId)
        {
            const string sql = @"
                UPDATE season_pass_purchase
                SET stripe_connected_account_id = @connectedAccountId,
                    payment_method = 'stripe_direct'
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, connectedAccountId });
        }

        public async Task UpdatePurchaseStatus(Guid id, string status)
        {
            const string sql = "UPDATE season_pass_purchase SET status = @status, updated_at = now() WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task<int> CompleteRegistration(Guid id, Guid tenantId, Guid purchaserUserId,
            string holderFirstName, string holderLastName, DateTime? holderBirthdate,
            string photoDataUrl, Guid? waiverSignatureId)
        {
            // Scoped by tenant AND purchaser: registration is driven by a client-supplied pass id,
            // so without the purchaser predicate any signed-in rider could write their own holder
            // name, photo, and waiver onto someone else's pass. Returns rows affected so the caller
            // can tell "not yours / wrong tenant" from a real update.
            //
            // status = 'paid' because an unpaid pass has nothing to register against — a pending row
            // may still fail its charge, and registering it would leave a holder-complete pass the
            // gate would happily admit. Re-running on an already-registered pass overwrites it,
            // which is what a buyer correcting a typo needs.
            const string sql = @"
                UPDATE season_pass_purchase
                SET holder_first_name   = @holderFirstName,
                    holder_last_name    = @holderLastName,
                    holder_birthdate    = @holderBirthdate,
                    photo_data_url      = @photoDataUrl,
                    waiver_signature_id = COALESCE(@waiverSignatureId, waiver_signature_id),
                    updated_at          = now()
                WHERE id = @id
                  AND tenant_id = @tenantId
                  AND purchaser_user_id = @purchaserUserId
                  AND status = 'paid'";
            return await _db.Execute(sql, new
            {
                id, tenantId, purchaserUserId, holderFirstName, holderLastName,
                holderBirthdate, photoDataUrl, waiverSignatureId,
            });
        }

        public async Task DecrementCredits(Guid purchaseId)
        {
            // Guarded so we never go below zero from a race.
            const string sql = @"
                UPDATE season_pass_purchase
                SET credits_remaining = credits_remaining - 1, updated_at = now()
                WHERE id = @purchaseId AND credits_remaining IS NOT NULL AND credits_remaining > 0";
            await _db.Execute(sql, new { purchaseId });
        }

        public async Task<Guid> CreateReservation(SeasonPassReservation r)
        {
            const string sql = @"
                INSERT INTO season_pass_reservation (season_pass_purchase_id, event_id, status)
                VALUES (@SeasonPassPurchaseId, @EventId, @Status)
                RETURNING id";
            return (await _db.Query<Guid>(sql, r)).First();
        }

        public async Task<SeasonPassReservation?> GetReservation(Guid purchaseId, Guid eventId)
        {
            var sql = $@"
                SELECT {ReservationColumns}
                FROM season_pass_reservation
                WHERE season_pass_purchase_id = @purchaseId AND event_id = @eventId
                LIMIT 1";
            return (await _db.Query<SeasonPassReservation>(sql, new { purchaseId, eventId })).FirstOrDefault();
        }

        public async Task<SeasonPassCheckInContext?> GetReservationForCheckIn(Guid reservationId, Guid tenantId)
        {
            const string sql = @"
                SELECT r.id AS ReservationId, r.event_id AS EventId,
                       p.purchaser_user_id AS HolderUserId,
                       p.purchaser_email AS HolderEmail,
                       p.purchaser_name AS HolderName,
                       p.holder_first_name AS HolderFirstName,
                       (p.photo_data_url IS NOT NULL) AS HasPhoto,
                       p.waiver_signature_id AS WaiverSignatureId,
                       pr.requires_waiver AS ProductRequiresWaiver
                FROM season_pass_reservation r
                JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
                JOIN season_pass_product pr ON pr.id = p.product_id
                WHERE r.id = @reservationId AND p.tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<SeasonPassCheckInContext>(sql, new { reservationId, tenantId })).FirstOrDefault();
        }

        public async Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchase(Guid purchaseId)
        {
            const string sql = @"
                SELECT r.id, r.season_pass_purchase_id AS SeasonPassPurchaseId,
                       r.event_id AS EventId, r.status,
                       r.reserved_at AS ReservedAt, r.checked_in_at AS CheckedInAt,
                       r.cancelled_at AS CancelledAt,
                       e.title AS EventTitle, e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt
                FROM season_pass_reservation r
                JOIN event e ON e.id = r.event_id
                WHERE r.season_pass_purchase_id = @purchaseId
                ORDER BY e.starts_at DESC";
            return (await _db.Query<SeasonPassReservationWithContext>(sql, new { purchaseId })).ToList();
        }

        public async Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchaseOnDate(Guid purchaseId, DateTime atUtc, DateTime untilUtc)
        {
            const string sql = @"
                SELECT r.id, r.season_pass_purchase_id AS SeasonPassPurchaseId,
                       r.event_id AS EventId, r.status,
                       r.reserved_at AS ReservedAt, r.checked_in_at AS CheckedInAt,
                       r.cancelled_at AS CancelledAt,
                       e.title AS EventTitle, e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt
                FROM season_pass_reservation r
                JOIN event e ON e.id = r.event_id
                WHERE r.season_pass_purchase_id = @purchaseId
                  AND e.starts_at < @untilUtc AND e.ends_at >= @atUtc
                ORDER BY e.starts_at";
            return (await _db.Query<SeasonPassReservationWithContext>(sql, new { purchaseId, atUtc, untilUtc })).ToList();
        }

        // Returns the number of rows affected so callers can detect a no-op transition (e.g. an
        // attempt to check in a reservation whose parent pass was refunded/cancelled).
        public async Task<int> UpdateReservationStatus(Guid id, Guid tenantId, string status, Guid? checkedInByUserId = null)
        {
            // Tenant scope is enforced by joining to season_pass_purchase. season_pass_reservation
            // doesn't carry tenant_id directly, so we filter via its parent purchase to refuse
            // updates against reservations belonging to another tenant — closes the cross-tenant
            // write previously possible by passing any reservation GUID.
            string sql;
            if (status == "checked_in")
            {
                // Only a live 'reserved' row on a still-paid pass may be checked in. This blocks
                // checking in a cancelled reservation or one whose pass was refunded/cancelled (which
                // a later un-check would resurrect to 'reserved', re-granting event access).
                sql = @"UPDATE season_pass_reservation r
                        SET status = @status, checked_in_at = now(),
                            checked_in_by_user_id = @checkedInByUserId
                        FROM season_pass_purchase p
                        WHERE r.id = @id
                          AND r.season_pass_purchase_id = p.id
                          AND p.tenant_id = @tenantId
                          AND r.status = 'reserved'
                          AND p.status NOT IN ('refunded', 'cancelled')";
            }
            else if (status == "cancelled")
            {
                sql = @"UPDATE season_pass_reservation r
                        SET status = @status, cancelled_at = now()
                        FROM season_pass_purchase p
                        WHERE r.id = @id
                          AND r.season_pass_purchase_id = p.id
                          AND p.tenant_id = @tenantId";
            }
            else
            {
                sql = @"UPDATE season_pass_reservation r
                        SET status = @status
                        FROM season_pass_purchase p
                        WHERE r.id = @id
                          AND r.season_pass_purchase_id = p.id
                          AND p.tenant_id = @tenantId";
            }
            return await _db.Execute(sql, new { id, tenantId, status, checkedInByUserId });
        }

        public async Task<Dictionary<Guid, int>> ActiveReservationsForEvents(IEnumerable<Guid> eventIds)
        {
            var ids = eventIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"
                SELECT event_id AS EventId, COUNT(*)::int AS Count
                FROM season_pass_reservation
                WHERE event_id = ANY(@ids) AND status <> 'cancelled'
                GROUP BY event_id";
            var rows = await _db.Query<(Guid EventId, int Count)>(sql, new { ids });
            return rows.ToDictionary(r => r.EventId, r => r.Count);
        }
    }
}
