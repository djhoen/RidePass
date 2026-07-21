using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventTicketPurchaseRepository : IEventTicketPurchaseRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, tier_id AS TierId, purchaser_user_id AS PurchaserUserId,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            amount_cents AS AmountCents,
            service_charge_cents AS ServiceChargeCents,
            tax_cents AS TaxCents, tax_rate_bps AS TaxRateBps, tax_inclusive AS TaxInclusive,
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
            waiver_signature_id AS WaiverSignatureId,
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
                    (tenant_id, tier_id, purchaser_user_id, amount_cents, service_charge_cents,
                     tax_cents, tax_rate_bps, tax_inclusive, applied_reward_redemption_id, payment_method,
                     status, purchaser_email, purchaser_name, sold_by_user_id, registration_complete,
                     waiver_signature_id)
                VALUES
                    (@TenantId, @TierId, @PurchaserUserId, @AmountCents, @ServiceChargeCents,
                     @TaxCents, @TaxRateBps, @TaxInclusive, @AppliedRewardRedemptionId, @PaymentMethod,
                     @Status, @PurchaserEmail, @PurchaserName, @SoldByUserId, @RegistrationComplete,
                     @WaiverSignatureId)
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
                       p.rider_first_name AS RiderFirstName, p.rider_last_name AS RiderLastName,
                       COALESCE(sig.signed_by_parent, false) AS SignedByParent,
                       COALESCE(sig.parent_name, p.parent_guardian_name) AS GuardianName,
                       t.name AS TierName,
                       e.id AS EventId, e.title AS EventTitle, e.description AS EventDescription,
                       e.location_label AS EventLocationLabel,
                       e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt, e.all_day AS EventAllDay
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                LEFT JOIN rider_waiver_signature sig
                       ON sig.id = p.waiver_signature_id AND sig.tenant_id = p.tenant_id
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
                       p.rider_first_name AS RiderFirstName, p.rider_last_name AS RiderLastName,
                       COALESCE(sig.signed_by_parent, false) AS SignedByParent,
                       COALESCE(sig.parent_name, p.parent_guardian_name) AS GuardianName,
                       t.name AS TierName, t.kind AS TierKind, t.audience AS TierAudience,
                       e.id AS EventId, e.title AS EventTitle, e.description AS EventDescription,
                       e.location_label AS EventLocationLabel,
                       e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt, e.all_day AS EventAllDay
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                LEFT JOIN rider_waiver_signature sig
                       ON sig.id = p.waiver_signature_id AND sig.tenant_id = p.tenant_id
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = @eventId
                  AND p.status <> 'cancelled'
                  AND (
                        (@purchaserUserId IS NOT NULL AND p.purchaser_user_id = @purchaserUserId)
                     OR (@purchaserUserId IS NULL AND lower(trim(p.purchaser_email)) = lower(trim(@purchaserEmail)))
                      )
                ORDER BY t.kind, t.name";
            var result = await _db.Query<EventTicketPurchaseWithContext>(sql,
                new { eventId, tenantId, purchaserUserId, purchaserEmail });
            return result.ToList();
        }

        // Gate lookup by name or email, for the rider who turns up with a dead phone and no QR.
        //
        // Deliberately narrow: only events whose check-in window overlaps today (the caller passes the
        // tenant-local day as a UTC interval) and only paid/redeemed rows. That keeps the query fast and,
        // more importantly, means a gate-staff login can't be used to page through the tenant's whole
        // customer list — it can only see who is actually coming through the gate today.
        //
        // Matching is case-insensitive (Postgres = and LIKE are not) across the buyer's name, the buyer's
        // email, and the RIDER's name: a parent often buys under their own name for a kid with a different
        // surname, and staff at the gate know the rider.
        //
        // One row per (event, purchaser); purchaser identity is the user id when the buyer had an account,
        // else their lowercased email, which mirrors how the redemption scope resolves a scanned token.
        public async Task<List<GateSearchRow>> SearchForGate(
            Guid tenantId, string query, DateTime todayStartUtc, DateTime todayEndUtc, int limit)
        {
            const string sql = @"
                SELECT e.id AS EventId,
                       MIN(e.title) AS EventTitle,
                       MIN(e.starts_at) AS EventStartsAt,
                       MIN(p.purchaser_name) AS PurchaserName,
                       MIN(p.purchaser_email) AS PurchaserEmail,
                       (MIN(p.redemption_token::text))::uuid AS AnchorToken,
                       COUNT(*)::int AS ItemCount,
                       COUNT(*) FILTER (WHERE p.status = 'redeemed')::int AS RedeemedCount,
                       string_agg(DISTINCT NULLIF(trim(COALESCE(p.rider_first_name, '') || ' ' ||
                                                        COALESCE(p.rider_last_name, '')), ''), ', ') AS RiderNames
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.tenant_id = @tenantId
                  AND p.status IN ('paid', 'redeemed')
                  AND e.starts_at <= @todayEndUtc
                  AND e.ends_at   >= @todayStartUtc
                  -- EVERY word the operator typed has to appear somewhere in this row's searchable
                  -- text (buyer name + buyer email + rider name), in any order. So 'reed jake',
                  -- 'jake reed', and plain 'jake' all find Jake Reed, and 'sarah reed' finds the
                  -- order Sarah bought for rider Reed. A single substring match can't do that, and
                  -- at a gate people type names in whatever order they hear them.
                  AND lower(
                        COALESCE(p.purchaser_name, '') || ' ' ||
                        COALESCE(p.purchaser_email, '') || ' ' ||
                        COALESCE(p.rider_first_name, '') || ' ' ||
                        COALESCE(p.rider_last_name, '')
                      ) LIKE ALL (@likes)
                GROUP BY e.id, COALESCE(p.purchaser_user_id::text, lower(trim(p.purchaser_email)))
                ORDER BY MIN(e.starts_at), MIN(p.purchaser_name)
                LIMIT @limit";
            var rows = await _db.Query<GateSearchRow>(sql, new
            {
                tenantId,
                likes = BuildLikeTerms(query),
                todayStartUtc,
                todayEndUtc,
                limit,
            });
            return rows.ToList();
        }

        // One '%term%' pattern per word typed, lowercased to match the lowered haystack (Postgres
        // LIKE is case-sensitive). Capped at 5 words so a pasted paragraph can't build a huge
        // conjunction. Falls back to the whole trimmed string if it somehow splits to nothing.
        private static string[] BuildLikeTerms(string query)
        {
            var words = query
                .Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(5)
                .Select(w => $"%{EscapeLike(w.ToLowerInvariant())}%")
                .ToArray();
            return words.Length > 0
                ? words
                : new[] { $"%{EscapeLike(query.Trim().ToLowerInvariant())}%" };
        }

        // A rider named "100%" or an email with an underscore must not turn into a wildcard.
        private static string EscapeLike(string value) =>
            value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        // Gate check-in waiver panel: the purchaser's ticket set for one event, denormalized with
        // the tier's audience (which decides whether the rider or the spectator waiver applies) and
        // the linked signature row (who signed, when, on whose behalf, which document). Same tenant +
        // event + purchaser scope as ListByEventForPurchaser, but only paid/redeemed rows: a failed or
        // pending payment is not a person walking through the gate, and listing one would raise a
        // false "somebody hasn't signed" alarm for a purchase that never completed.
        public async Task<List<OrderAttendeeWaiverRow>> ListWaiverStatusForPurchaser(
            Guid eventId, Guid tenantId, Guid? purchaserUserId, string? purchaserEmail)
        {
            const string sql = @"
                SELECT p.id AS PurchaseId,
                       p.registrant_id AS RegistrantId,
                       t.name AS TierName, t.kind AS TierKind, t.audience AS TierAudience,
                       p.status,
                       p.registration_complete AS RegistrationComplete,
                       p.purchaser_name AS PurchaserName,
                       p.rider_first_name AS RiderFirstName,
                       p.rider_last_name AS RiderLastName,
                       p.rider_birthdate AS RiderBirthdate,
                       p.waiver_signature_id AS WaiverSignatureId,
                       p.waiver_signed_at AS WaiverSignedAt,
                       (p.waiver_signature_data_url IS NOT NULL) AS HasInlineSignatureImage,
                       sig.signed_at AS SignatureSignedAt,
                       (sig.signature_data_url IS NOT NULL) AS SignatureHasImage,
                       COALESCE(sig.signed_by_parent, false) AS SignedByParent,
                       COALESCE(sig.parent_name, p.parent_guardian_name) AS ParentName,
                       sig.signer_name AS SignerName,
                       sig.signer_email AS SignerEmail,
                       sig.spectator_birthdate AS SignatureBirthdate,
                       COALESCE(sig.waiver_id, p.waiver_id) AS SignedWaiverId,
                       w.name AS SignedWaiverName,
                       w.title AS SignedWaiverTitle
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                LEFT JOIN rider_waiver_signature sig
                       ON sig.id = p.waiver_signature_id AND sig.tenant_id = p.tenant_id
                LEFT JOIN tenant_waiver w
                       ON w.id = COALESCE(sig.waiver_id, p.waiver_id) AND w.tenant_id = p.tenant_id
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = @eventId
                  AND p.status IN ('paid', 'redeemed')
                  AND (
                        (@purchaserUserId IS NOT NULL AND p.purchaser_user_id = @purchaserUserId)
                     OR (@purchaserUserId IS NULL AND lower(trim(p.purchaser_email)) = lower(trim(@purchaserEmail)))
                      )
                ORDER BY t.kind, t.name";
            var rows = await _db.Query<OrderAttendeeWaiverRow>(sql,
                new { eventId, tenantId, purchaserUserId, purchaserEmail });
            return rows.ToList();
        }

        // Event-wide check-in roster. Only paid/redeemed rows are real attendees (pending /
        // failed / cancelled never enter the gate). Tenant-scoped on the purchase; the event
        // filter rides the tier join. Sorted spectators-after-riders, then by class then name.
        // onDate is supplied for a MULTI-DAY event (a camp), where "checked in" is a per-day
        // question the one-shot redeemed flag can't answer. Null keeps the single-day meaning.
        public async Task<List<EventRosterRow>> ListEventRoster(Guid eventId, Guid tenantId, DateOnly? onDate = null)
        {
            const string sql = @"
                SELECT p.id AS PurchaseId,
                       p.redemption_token AS RedemptionToken,
                       p.purchaser_user_id AS PurchaserUserId,
                       p.purchaser_name AS PurchaserName,
                       p.purchaser_email AS PurchaserEmail,
                       p.race_number AS RaceNumber,
                       p.status,
                       p.registration_complete AS RegistrationComplete,
                       (p.waiver_signature_id IS NOT NULL
                        OR p.waiver_signed_at IS NOT NULL
                        OR p.waiver_signature_data_url IS NOT NULL) AS WaiverSigned,
                       p.redeemed_at_utc AS RedeemedAtUtc,
                       p.redeemed_by_user_id AS RedeemedByUserId,
                       t.name AS TierName, t.kind AS TierKind, t.audience AS TierAudience,
                       CASE WHEN @onDate::date IS NULL THEN (p.status = 'redeemed')
                            ELSE (a.id IS NOT NULL) END AS CheckedInOnDate
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                LEFT JOIN event_ticket_attendance a
                       ON a.ticket_id = p.id AND a.on_date = @onDate::date
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = @eventId
                  AND p.status IN ('paid', 'redeemed')
                ORDER BY t.audience, t.kind, t.name, lower(coalesce(p.purchaser_name, ''))";
            var result = await _db.Query<EventRosterRow>(sql, new { eventId, tenantId, onDate });
            return result.ToList();
        }

        public async Task SetStripePaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE event_ticket_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        // Direct charge: snapshot the connected account the row was charged on and flag the row's
        // payment method so downstream (finalizer / refund / reconciler / reporting) treats it as direct.
        public async Task MarkDirectCharge(Guid id, Guid tenantId, string connectedAccountId)
        {
            const string sql = @"
                UPDATE event_ticket_purchase
                SET stripe_connected_account_id = @connectedAccountId,
                    payment_method = 'stripe_direct'
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, connectedAccountId });
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

        // Guarded redeem for offline batch sync: only a 'paid' row transitions, so a row
        // already redeemed (by any device) is untouched and this returns false. That makes
        // the first sync to land win and later duplicates detectable rather than overwritten.
        public async Task<bool> TryMarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc)
        {
            const string sql = @"
                UPDATE event_ticket_purchase
                SET status = 'redeemed', redeemed_at_utc = @atUtc, redeemed_by_user_id = @redeemedByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            var affected = await _db.Execute(sql, new { id, tenantId, redeemedByUserId, atUtc });
            return affected > 0;
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

        // ── Multi-day attendance ──────────────────────────────────────────────────────
        // A camp spans several days on one ticket, so "did this rider check in" is a per-day
        // question the one-shot redeemed flag can't answer. Single-day events never touch this.

        /// <summary>Records this ticket's check-in for one local date. Returns false when the
        /// ticket was already checked in that day (unique index), which the gate reports rather
        /// than double-counting. The insert is tenant-guarded through the ticket row.</summary>
        public async Task<bool> TryRecordAttendance(Guid ticketId, Guid tenantId, DateOnly onDate, Guid? byUserId)
        {
            const string sql = @"
                INSERT INTO event_ticket_attendance (tenant_id, ticket_id, on_date, by_user_id)
                SELECT p.tenant_id, p.id, @onDate, @byUserId
                FROM event_ticket_purchase p
                WHERE p.id = @ticketId AND p.tenant_id = @tenantId
                ON CONFLICT (ticket_id, on_date) DO NOTHING";
            var affected = await _db.Execute(sql, new { ticketId, tenantId, onDate, byUserId });
            return affected > 0;
        }

        /// <summary>Local dates this ticket has been checked in on, oldest first.</summary>
        public async Task<List<DateOnly>> ListAttendanceDates(Guid ticketId, Guid tenantId)
        {
            const string sql = @"
                SELECT a.on_date
                FROM event_ticket_attendance a
                WHERE a.ticket_id = @ticketId AND a.tenant_id = @tenantId
                ORDER BY a.on_date";
            return (await _db.Query<DateOnly>(sql, new { ticketId, tenantId })).ToList();
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
        // Returns false (no row written) when the ticket isn't in a registerable state. Only a
        // 'pending' or 'paid' ticket may be (re)registered; once it's 'redeemed' (checked in) its
        // rider identity, birthdate, and signed waiver are a locked legal record and must not be
        // overwritten by anyone holding the ticket GUID.
        public async Task<bool> CompleteRegistration(Guid id, Guid tenantId,
            string? riderFirstName, string? riderLastName, DateTime? riderBirthdate, string? bike,
            string? raceNumber, Guid? waiverId, string? waiverSignatureDataUrl, Guid? waiverSignatureId,
            string? parentGuardianName,
            string? emergencyContactName, string? emergencyContactPhone, Guid? registrantId)
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
                    waiver_signature_id        = @waiverSignatureId,
                    waiver_signed_at           = CASE WHEN @waiverSignatureDataUrl IS NOT NULL THEN now() ELSE waiver_signed_at END,
                    parent_guardian_name       = @parentGuardianName,
                    emergency_contact_name     = @emergencyContactName,
                    emergency_contact_phone    = @emergencyContactPhone,
                    registrant_id              = @registrantId,
                    registration_complete      = true,
                    updated_at                 = now()
                WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending', 'paid')";
            var affected = await _db.Execute(sql, new
            {
                id, tenantId, riderFirstName, riderLastName, riderBirthdate, bike,
                raceNumber, waiverId, waiverSignatureDataUrl, waiverSignatureId, parentGuardianName,
                emergencyContactName, emergencyContactPhone, registrantId
            });
            return affected > 0;
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

        // Per-RIDER uniqueness within a race class, enforced at registration. The class is
        // the set of tiers in classTierIds (all price-ladder steps of one class, or a single
        // standalone tier). Returns "person" if the same rider (name + birthdate) is already
        // entered, "number" if the race number is taken, else null. excludeTicketIds are the
        // rows being registered in this same request (so a rider doesn't conflict with self).
        // Name/number comparisons are case-insensitive; pending/paid/redeemed count as active.
        public async Task<string?> FindRaceClassConflict(Guid tenantId, IReadOnlyList<Guid> classTierIds,
            string firstName, string lastName, DateTime? birthdate, string? raceNumber,
            IReadOnlyList<Guid> excludeTicketIds)
        {
            if (classTierIds.Count == 0) return null;
            var tierIds = classTierIds.ToArray();
            var excludeIds = excludeTicketIds.ToArray();

            const string personSql = @"
                SELECT EXISTS(
                    SELECT 1 FROM event_ticket_purchase
                    WHERE tenant_id = @tenantId
                      AND tier_id = ANY(@tierIds)
                      AND status IN ('pending', 'paid', 'redeemed')
                      AND NOT (id = ANY(@excludeIds))
                      AND rider_first_name IS NOT NULL
                      AND lower(rider_first_name) = lower(@firstName)
                      AND lower(rider_last_name) = lower(@lastName)
                      AND rider_birthdate IS NOT DISTINCT FROM @birthdate)";
            var person = await _db.Query<bool>(personSql, new { tenantId, tierIds, excludeIds, firstName, lastName, birthdate });
            if (person.FirstOrDefault()) return "person";

            if (!string.IsNullOrWhiteSpace(raceNumber))
            {
                const string numberSql = @"
                    SELECT EXISTS(
                        SELECT 1 FROM event_ticket_purchase
                        WHERE tenant_id = @tenantId
                          AND tier_id = ANY(@tierIds)
                          AND status IN ('pending', 'paid', 'redeemed')
                          AND NOT (id = ANY(@excludeIds))
                          AND race_number IS NOT NULL
                          AND lower(race_number) = lower(@raceNumber))";
                var num = await _db.Query<bool>(numberSql, new { tenantId, tierIds, excludeIds, raceNumber });
                if (num.FirstOrDefault()) return "number";
            }
            return null;
        }

        public async Task<int> CountByStatusForTenant(Guid tenantId, string status)
        {
            const string sql = "SELECT COUNT(*) FROM event_ticket_purchase WHERE tenant_id = @tenantId AND status = @status";
            return await _db.ExecuteScalar(sql, new { tenantId, status });
        }

        public async Task<List<EventTicketPurchaseWithContext>> ListByStatusAcrossTenants(string status)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId,
                       p.stripe_connected_account_id AS StripeConnectedAccountId, p.amount_cents AS AmountCents,
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
