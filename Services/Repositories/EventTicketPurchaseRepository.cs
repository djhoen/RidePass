using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventTicketPurchaseRepository : IEventTicketPurchaseRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, tier_id AS TierId, purchaser_user_id AS PurchaserUserId,
            stripe_payment_intent_id AS StripePaymentIntentId, amount_cents AS AmountCents,
            service_charge_cents AS ServiceChargeCents,
            applied_reward_redemption_id AS AppliedRewardRedemptionId,
            payment_method AS PaymentMethod,
            status, purchaser_email AS PurchaserEmail, purchaser_name AS PurchaserName,
            redemption_token AS RedemptionToken,
            redeemed_at_utc AS RedeemedAtUtc, redeemed_by_user_id AS RedeemedByUserId,
            sold_by_user_id AS SoldByUserId,
            race_number AS RaceNumber,
            rider_first_name AS RiderFirstName, rider_last_name AS RiderLastName,
            rider_birthdate AS RiderBirthdate, bike AS Bike,
            waiver_id AS WaiverId, waiver_signed_at AS WaiverSignedAt,
            waiver_signature_data_url AS WaiverSignatureDataUrl,
            parent_guardian_name AS ParentGuardianName,
            registration_complete AS RegistrationComplete,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string WithContextColumns = Columns + @",
            tier_name AS TierName, event_id AS EventId,
            event_title AS EventTitle, event_starts_at AS EventStartsAt";

        private readonly IDbHelper _db;

        public EventTicketPurchaseRepository(IDbHelper db) => _db = db;

        public async Task<(Guid Id, Guid RedemptionToken)> Create(EventTicketPurchase p)
        {
            const string sql = @"
                INSERT INTO event_ticket_purchase
                    (tenant_id, tier_id, purchaser_user_id, amount_cents, service_charge_cents, applied_reward_redemption_id, payment_method,
                     status, purchaser_email, purchaser_name, sold_by_user_id, registration_complete)
                VALUES
                    (@TenantId, @TierId, @PurchaserUserId, @AmountCents, @ServiceChargeCents, @AppliedRewardRedemptionId, @PaymentMethod,
                     @Status, @PurchaserEmail, @PurchaserName, @SoldByUserId, @RegistrationComplete)
                RETURNING id, redemption_token AS RedemptionToken";
            var row = (await _db.Query<EventTicketPurchase>(sql, p)).First();
            return (row.Id, row.RedemptionToken);
        }

        public async Task<EventTicketPurchase?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM event_ticket_purchase WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<EventTicketPurchase>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<EventTicketPurchase?> GetByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {Columns} FROM event_ticket_purchase WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1";
            var result = await _db.Query<EventTicketPurchase>(sql, new { paymentIntentId });
            return result.FirstOrDefault();
        }

        public async Task<List<EventTicketPurchase>> ListByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {Columns} FROM event_ticket_purchase WHERE stripe_payment_intent_id = @paymentIntentId";
            var result = await _db.Query<EventTicketPurchase>(sql, new { paymentIntentId });
            return result.ToList();
        }

        public async Task<EventTicketPurchaseWithContext?> GetByRedemptionToken(Guid token, Guid tenantId)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.race_number AS RaceNumber, p.registration_complete AS RegistrationComplete,
                       p.redeemed_at_utc AS RedeemedAtUtc, p.redeemed_by_user_id AS RedeemedByUserId,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       t.name AS TierName,
                       e.id AS EventId, e.title AS EventTitle, e.description AS EventDescription,
                       e.location_label AS EventLocationLabel,
                       e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt, e.all_day AS EventAllDay
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.redemption_token = @token AND p.tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<EventTicketPurchaseWithContext>(sql, new { token, tenantId });
            return result.FirstOrDefault();
        }

        // Gate redemption, event+purchaser scope: all of a purchaser's tickets for one
        // event regardless of how many orders they span, so a single QR scan surfaces the
        // whole rider's set. Tenant-scoped; purchaser matched by user id (logged-in buy)
        // else case-insensitive email (guest buy). Cancelled rows are excluded.
        public async Task<List<EventTicketPurchaseWithContext>> ListByEventForPurchaser(
            Guid eventId, Guid tenantId, Guid? purchaserUserId, string? purchaserEmail)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.race_number AS RaceNumber, p.registration_complete AS RegistrationComplete,
                       p.redeemed_at_utc AS RedeemedAtUtc, p.redeemed_by_user_id AS RedeemedByUserId,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       t.name AS TierName,
                       e.id AS EventId, e.title AS EventTitle, e.description AS EventDescription,
                       e.location_label AS EventLocationLabel,
                       e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt, e.all_day AS EventAllDay
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = @eventId
                  AND p.status <> 'cancelled'
                  AND (
                        (@purchaserUserId IS NOT NULL AND p.purchaser_user_id = @purchaserUserId)
                     OR (@purchaserUserId IS NULL AND lower(p.purchaser_email) = lower(@purchaserEmail))
                      )
                ORDER BY t.kind, t.name";
            var result = await _db.Query<EventTicketPurchaseWithContext>(sql,
                new { eventId, tenantId, purchaserUserId, purchaserEmail });
            return result.ToList();
        }

        public async Task SetStripePaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE event_ticket_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE event_ticket_purchase SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc)
        {
            // tenant_id predicate prevents a stray purchaseId from another tenant being
            // flipped to redeemed. UndoRedeemed already had this; MarkRedeemed didn't.
            const string sql = @"
                UPDATE event_ticket_purchase
                SET status = 'redeemed', redeemed_at_utc = @atUtc, redeemed_by_user_id = @redeemedByUserId
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, redeemedByUserId, atUtc });
        }

        // Reverse a check-in (status: redeemed → paid) so staff can correct
        // an accidental scan. Audit fields are cleared along with status.
        public async Task UndoRedeemed(Guid id, Guid tenantId)
        {
            const string sql = @"
                UPDATE event_ticket_purchase
                SET status = 'paid', redeemed_at_utc = NULL, redeemed_by_user_id = NULL
                WHERE id = @id AND tenant_id = @tenantId AND status = 'redeemed'";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task SetRaceNumber(Guid id, Guid tenantId, string? raceNumber)
        {
            const string sql = @"
                UPDATE event_ticket_purchase
                SET race_number = @raceNumber
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, raceNumber });
        }

        // Post-payment registration for the unified checkout: attach this ticket's rider
        // identity + signed waiver and flip registration_complete. Tenant-scoped. The
        // caller is responsible for enforcing that a required waiver signature is present
        // before marking complete.
        public async Task CompleteRegistration(Guid id, Guid tenantId,
            string? riderFirstName, string? riderLastName, DateTime? riderBirthdate, string? bike,
            string? raceNumber, Guid? waiverId, string? waiverSignatureDataUrl, string? parentGuardianName,
            Guid? registrantId)
        {
            const string sql = @"
                UPDATE event_ticket_purchase
                SET rider_first_name           = @riderFirstName,
                    rider_last_name            = @riderLastName,
                    rider_birthdate            = @riderBirthdate,
                    bike                       = @bike,
                    race_number                = COALESCE(@raceNumber, race_number),
                    waiver_id                  = @waiverId,
                    waiver_signature_data_url  = @waiverSignatureDataUrl,
                    waiver_signed_at           = CASE WHEN @waiverSignatureDataUrl IS NOT NULL THEN now() ELSE waiver_signed_at END,
                    parent_guardian_name       = @parentGuardianName,
                    registrant_id              = @registrantId,
                    registration_complete      = true,
                    updated_at                 = now()
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new
            {
                id, tenantId, riderFirstName, riderLastName, riderBirthdate, bike,
                raceNumber, waiverId, waiverSignatureDataUrl, parentGuardianName, registrantId
            });
        }

        // Rider-facing order detail: every (non-cancelled) ticket this rider holds for an
        // event, across any order. Scoped by purchaser_user_id (the authenticated rider),
        // so it only ever returns the caller's own rows.
        public async Task<List<UserEventOrderItem>> ListForUserEvent(Guid userId, Guid eventId)
        {
            const string sql = @"
                SELECT p.id AS Id, t.name AS TierName, t.kind AS Kind, t.audience AS Audience,
                       p.status AS Status, p.amount_cents AS AmountCents, t.price_cents AS BasePriceCents,
                       p.race_number AS RaceNumber,
                       NULLIF(TRIM(COALESCE(p.rider_first_name, '') || ' ' || COALESCE(p.rider_last_name, '')), '') AS RiderName,
                       p.registration_complete AS RegistrationComplete,
                       (p.waiver_signed_at IS NOT NULL) AS WaiverSigned,
                       p.redemption_token AS RedemptionToken,
                       e.title AS EventTitle
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.purchaser_user_id = @userId AND t.event_id = @eventId
                  AND p.status <> 'cancelled'
                ORDER BY t.kind, t.name";
            var rows = await _db.Query<UserEventOrderItem>(sql, new { userId, eventId });
            return rows.ToList();
        }

        // Rider-scoped (Me feed, cross-tenant by purchaser_user_id): the rider's paid/redeemed
        // rows for one event, denormalized with tenant + purchaser + event/tier names so the
        // resend endpoint can rebuild the confirmation email without extra round-trips.
        public async Task<List<OrderConfirmationRow>> ListForOrderConfirmation(Guid userId, Guid eventId)
        {
            const string sql = @"
                SELECT p.tenant_id AS TenantId, te.subdomain AS TenantSubdomain, te.display_name AS TenantDisplayName,
                       p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       e.title AS EventTitle, t.name AS TierName,
                       p.amount_cents AS AmountCents, p.status AS Status,
                       p.redemption_token AS RedemptionToken
                FROM event_ticket_purchase p
                JOIN tenant te ON te.id = p.tenant_id
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.purchaser_user_id = @userId AND t.event_id = @eventId
                  AND p.status IN ('paid', 'redeemed')
                ORDER BY t.kind, t.name";
            var rows = await _db.Query<OrderConfirmationRow>(sql, new { userId, eventId });
            return rows.ToList();
        }

        // Sweep (cross-tenant) for the "finish your registration" reminder: paid tickets
        // still missing registration, older than the cutoff, not yet reminded. Carries the
        // tenant subdomain + purchaser + event title so the worker can build the email.
        public async Task<List<RegistrationReminderRow>> ListIncompleteForReminder(DateTime cutoffUtc, int take)
        {
            const string sql = @"
                SELECT p.id AS TicketId, p.redemption_token AS RedemptionToken,
                       p.stripe_payment_intent_id AS PaymentIntentId,
                       p.tenant_id AS TenantId, te.subdomain AS TenantSubdomain,
                       p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       e.title AS EventTitle
                FROM event_ticket_purchase p
                JOIN tenant te ON te.id = p.tenant_id
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.status = 'paid'
                  AND p.registration_complete = false
                  AND p.registration_reminder_sent_at IS NULL
                  AND p.created_at <= @cutoffUtc
                ORDER BY p.created_at
                LIMIT @take";
            var rows = await _db.Query<RegistrationReminderRow>(sql, new { cutoffUtc, take });
            return rows.ToList();
        }

        public async Task MarkRegistrationReminderSent(IEnumerable<Guid> ticketIds)
        {
            const string sql = "UPDATE event_ticket_purchase SET registration_reminder_sent_at = now() WHERE id = ANY(@ids)";
            await _db.Execute(sql, new { ids = ticketIds.ToArray() });
        }

        // Resume page: all still-incomplete tickets in the same order as the token's ticket
        // (same PaymentIntent; for free orders with no PI, just the token's ticket). Includes
        // the event's waiver flags so the form knows what to require per audience.
        public async Task<List<IncompleteRegistrationTicket>> ListIncompleteForRegistrationByToken(Guid token, Guid tenantId)
        {
            // Scope by EVENT + same purchaser (not by PaymentIntent): a rider can place more
            // than one order for the same event, and the apex "Finish registration" link
            // carries a single token, so a per-PI scope would surface only one order's rows
            // (and the spectator gate, often bought in a separate add-on order, would never
            // appear). Matching the anchor's purchaser (user id, else email) keeps it to the
            // caller's own tickets. Accept 'pending' too: registration is non-financial and
            // the paid-flipping webhook lands seconds after the client-side confirmation, so
            // requiring 'paid' would show an empty form during that window (CompleteRegistration
            // is relaxed the same way). Check-in still honors paid/redeemed only.
            const string sql = @"
                WITH anchor AS (
                    SELECT p.id, p.purchaser_user_id, p.purchaser_email, t.event_id
                    FROM event_ticket_purchase p
                    JOIN event_ticket_tier t ON t.id = p.tier_id
                    WHERE p.redemption_token = @token AND p.tenant_id = @tenantId
                    LIMIT 1
                )
                SELECT p.id AS TicketId, t.name AS TierName, t.kind AS Kind,
                       t.audience AS Audience, t.required AS Required, e.title AS EventTitle,
                       e.requires_rider_waiver AS RequiresRiderWaiver,
                       e.requires_spectator_waiver AS RequiresSpectatorWaiver
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                CROSS JOIN anchor a
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = a.event_id
                  AND p.registration_complete = false
                  AND p.status NOT IN ('cancelled', 'refunded', 'failed')
                  AND (
                        (a.purchaser_user_id IS NOT NULL AND p.purchaser_user_id = a.purchaser_user_id)
                     OR (a.purchaser_user_id IS NULL AND lower(p.purchaser_email) = lower(a.purchaser_email))
                      )
                ORDER BY t.kind, t.name";
            var rows = await _db.Query<IncompleteRegistrationTicket>(sql, new { token, tenantId });
            return rows.ToList();
        }

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason)
        {
            const string sql = @"
                UPDATE event_ticket_purchase
                SET status = 'cancelled',
                    cancellation_reason = @reason,
                    cancelled_at = now(),
                    cancelled_by_user_id = @cancelledByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            await _db.Execute(sql, new { id, tenantId, cancelledByUserId, reason });
        }

        public async Task MarkRefunded(Guid id, string? refundNote)
        {
            const string sql = "UPDATE event_ticket_purchase SET status = 'refunded', refund_note = @refundNote WHERE id = @id";
            await _db.Execute(sql, new { id, refundNote });
        }

        // Race-class one-per-rider enforcement. Matches on user_id when signed in,
        // falls back to email (case-insensitive) for guest checkouts. Cancelled and
        // refunded rows free the slot; pending/paid/redeemed all count as active.
        public async Task<bool> HasActiveRaceEntry(Guid tenantId, Guid tierId, Guid? purchaserUserId, string? purchaserEmail)
        {
            var normalisedEmail = string.IsNullOrWhiteSpace(purchaserEmail) ? null : purchaserEmail.Trim();
            if (!purchaserUserId.HasValue && string.IsNullOrEmpty(normalisedEmail))
            {
                return false;
            }
            const string sql = @"
                SELECT EXISTS(
                    SELECT 1 FROM event_ticket_purchase
                    WHERE tenant_id = @tenantId
                      AND tier_id = @tierId
                      AND status IN ('pending', 'paid', 'redeemed')
                      AND (
                           (@purchaserUserId IS NOT NULL AND purchaser_user_id = @purchaserUserId)
                        OR (@purchaserEmail  IS NOT NULL AND LOWER(purchaser_email) = LOWER(@purchaserEmail))
                      )
                )";
            var rows = await _db.Query<bool>(sql, new
            {
                tenantId,
                tierId,
                purchaserUserId,
                purchaserEmail = normalisedEmail,
            });
            return rows.FirstOrDefault();
        }

        public async Task<List<EventTicketPurchaseWithContext>> ListByStatusAcrossTenants(string status)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.cancellation_reason AS CancellationReason, p.cancelled_at AS CancelledAt,
                       p.cancelled_by_user_id AS CancelledByUserId, p.refund_note AS RefundNote,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       t.name AS TierName, e.id AS EventId, e.title AS EventTitle,
                       e.description AS EventDescription, e.location_label AS EventLocationLabel,
                       e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt, e.all_day AS EventAllDay
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.status = @status
                ORDER BY p.cancelled_at DESC NULLS LAST, p.created_at DESC";
            var rows = await _db.Query<EventTicketPurchaseWithContext>(sql, new { status });
            return rows.ToList();
        }

        public async Task<List<EventTicketPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       t.name AS TierName, t.kind AS TierKind,
                       e.id AS EventId, e.title AS EventTitle, e.starts_at AS EventStartsAt
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.purchaser_user_id = @userId AND p.tenant_id = @tenantId
                ORDER BY p.created_at DESC";
            var rows = await _db.Query<EventTicketPurchaseWithContext>(sql, new { userId, tenantId });
            return rows.ToList();
        }
    }
}
