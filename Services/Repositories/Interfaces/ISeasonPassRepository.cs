using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface ISeasonPassRepository
    {
        // Products
        /// <summary>Admin Employee Passes roster: every staff account on the tenant with the
        /// employee pass they hold, if any. Staff with no pass are included.</summary>
        Task<List<EmployeePassRosterRow>> ListEmployeePassRoster(Guid tenantId);

        // ── Upgrades (Script0253) ────────────────────────────────────────────
        Task<List<SeasonPassUpgradePath>> ListUpgradePaths(Guid tenantId);

        /// <summary>Create or update one offer. Silently writes nothing when either product is
        /// not this tenant's, or is an employee product.</summary>
        Task UpsertUpgradePath(Guid tenantId, Guid fromProductId, Guid toProductId, int priceCents, bool isActive);

        Task DeleteUpgradePath(Guid id, Guid tenantId);

        /// <summary>Eligible holders per path today, for the admin matrix.</summary>
        Task<Dictionary<Guid, int>> CountEligibleHolders(Guid tenantId, DateTime today);

        /// <summary>Upgrades this rider can take right now, one row per (pass, path).</summary>
        Task<List<SeasonPassUpgradeOffer>> ListUpgradeOffersForUser(Guid userId, Guid tenantId, DateTime today);

        /// <summary>One offer, re-resolved server-side at checkout. Null when unavailable, which
        /// is what stops a stale or spoofed price being charged.</summary>
        Task<SeasonPassUpgradeOffer?> GetUpgradeOffer(Guid passPurchaseId, Guid pathId, Guid userId, Guid tenantId, DateTime today);

        /// <summary>Retire the replaced pass. False when it was not 'paid' (already handled).</summary>
        Task<bool> MarkUpgraded(Guid passPurchaseId, Guid tenantId);

        /// <summary>Carry photo, waiver, holder details, and ID verification onto the replacement.
        /// Load-bearing: without it the new pass will not scan.</summary>
        Task CarryRegistrationForward(Guid fromPurchaseId, Guid toPurchaseId, Guid tenantId);

        // ── Buddy passes (Script0247) ────────────────────────────────────────
        /// <summary>Replace what a buddy-pass entitlement is good for. No-op for a benefit that
        /// isn't this tenant's buddy_pass row.</summary>
        Task ReplaceBuddyScopes(Guid benefitId, Guid tenantId, IEnumerable<Guid> eventTypeIds, bool includeWalkUp);

        Task<List<SeasonPassBuddyScope>> ListBuddyScopes(Guid benefitId, Guid tenantId);

        /// <summary>The buddy entitlement behind one paid pass, with remaining count and scopes.
        /// Null when the product grants none.</summary>
        Task<SeasonPassBuddyEntitlement?> GetBuddyEntitlement(Guid passPurchaseId, Guid tenantId);

        /// <summary>Spend one buddy admission. CALLER MUST hold the per-pass advisory lock and
        /// re-check Remaining inside it.</summary>
        Task<Guid> RedeemBuddyPass(SeasonPassBuddyRedemption r);

        /// <summary>Return a spent credit (entitlement only, no money, admission untouched).
        /// False when already returned or not this tenant's.</summary>
        Task<bool> ReturnBuddyCredit(Guid redemptionId, Guid tenantId, Guid returnedByUserId, string reason);

        /// <summary>Buddy usage for the admin report. Returned credits are included and flagged.</summary>
        Task<List<SeasonPassBuddyRedemption>> ListBuddyRedemptions(Guid tenantId, int take = 200);

        /// <summary>Revoke an issued employee pass. Only an employee product's purchase can be
        /// revoked here. Returns false when nothing matched (already revoked, or not this tenant's).</summary>
        Task<bool> RevokeEmployeePass(Guid purchaseId, Guid tenantId, Guid revokedByUserId, string reason);

        /// <summary>True when this employee already holds a live employee pass.</summary>
        Task<bool> HasLiveEmployeePass(Guid userId, Guid tenantId);

        /// <summary>Tenant's pass products. includeEmployee defaults to false: employee products
        /// are staff-only grants and must never reach a public read path. Only the admin product
        /// list opts in.</summary>
        Task<List<SeasonPassProduct>> ListProductsForTenant(Guid tenantId, bool activeOnly, bool includeEmployee = false);
        Task<SeasonPassProduct?> GetProduct(Guid id, Guid tenantId);
        Task<Guid> CreateProduct(SeasonPassProduct p);
        Task UpdateProduct(SeasonPassProduct p);
        Task DeleteProduct(Guid id, Guid tenantId);

        /// <summary>Landing-page lookup: case-insensitive slug, tenant-scoped.</summary>
        Task<SeasonPassProduct?> GetProductBySlug(string slug, Guid tenantId);
        Task<bool> ProductSlugExists(string slug, Guid tenantId, Guid? excludeId);

        /// <summary>Atomic bulk update of sort_order for many season pass products at once.</summary>
        Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        // Perks (legacy — superseded by Benefits; kept until season_pass_event_type_perk is dropped)
        Task<List<SeasonPassEventTypePerk>> ListPerks(Guid passProductId);
        Task ReplacePerks(Guid passProductId, IEnumerable<SeasonPassEventTypePerk> perks);

        // Benefits
        Task<List<SeasonPassBenefit>> ListBenefits(Guid passProductId, Guid tenantId);

        /// <summary>Benefits for many products at once, keyed by pass_product_id. Keeps the public
        /// product list and checkout off an N+1 when several products are in play.</summary>
        Task<Dictionary<Guid, List<SeasonPassBenefit>>> ListBenefitsForProducts(
            IEnumerable<Guid> passProductIds, Guid tenantId);
        Task ReplaceBenefits(Guid passProductId, Guid tenantId, IEnumerable<SeasonPassBenefit> benefits);

        /// <summary>
        /// The benefits a user's ACTIVE passes grant on one surface, newest pass first. One entry
        /// per pass (not per product), because a buyer holding three passes gets three grants.
        /// Only passes that are paid, registered, and valid on <paramref name="onDateUtc"/>
        /// (including the product's day-of-week rule and any remaining credits) are returned.
        /// </summary>
        Task<List<SeasonPassBenefitGrant>> ListActiveBenefitGrantsForUser(
            Guid userId, Guid tenantId, string benefitType, Guid? scopeId, DateTime onDateUtc);

        /// <summary>
        /// Does this user hold a pass that is paid and valid on the given date? Backs the
        /// TENANT-WIDE holder discount, which is not tied to any one product and so has no benefit
        /// row to look up.
        ///
        /// Deliberately looser than <see cref="ListActiveBenefitGrantsForUser"/>: it does not
        /// require a photo or a signed waiver. Those gates exist because a per-pass benefit can be
        /// admission-grade (free entry), where an unverifiable pass is a real fraud risk. Refusing
        /// a pass holder 10% off a t-shirt because they never uploaded a photo is a support ticket,
        /// not a control, and the cashier looking them up has no photo to check anyway.
        /// </summary>
        Task<bool> HasPassValidOn(Guid userId, Guid tenantId, DateTime onDateUtc);

        // Purchases
        Task<(Guid Id, Guid RedemptionToken)> CreatePurchase(SeasonPassPurchase p);
        Task<SeasonPassPurchase?> GetPurchase(Guid id);
        Task<SeasonPassPurchase?> GetPurchaseByStripePaymentIntentId(string paymentIntentId);

        /// <summary>Every pass on one PaymentIntent. A single checkout can buy several passes
        /// (one buyer, several holders), so finalization and refunds must see them all rather
        /// than stopping at the first match.</summary>
        Task<List<SeasonPassPurchase>> ListPurchasesByStripePaymentIntentId(string paymentIntentId);
        Task<SeasonPassPurchase?> GetPurchaseByRedemptionToken(Guid token);
        Task<List<SeasonPassPurchaseWithContext>> ListMine(Guid userId, Guid tenantId);
        Task SetPurchaseStripePaymentIntentId(Guid id, string paymentIntentId);
        Task MarkPurchaseDirectCharge(Guid id, Guid tenantId, string connectedAccountId);
        Task UpdatePurchaseStatus(Guid id, string status);

        /// <summary>
        /// Writes the post-payment registration onto one paid pass: who it admits, their photo,
        /// and their waiver signature. Scoped by tenant AND purchaser so a rider can only register
        /// passes from their own order. Returns rows affected — 0 means the pass isn't theirs,
        /// isn't in this tenant, or isn't paid.
        /// </summary>
        Task<int> CompleteRegistration(Guid id, Guid tenantId, Guid purchaserUserId,
            string holderFirstName, string holderLastName, DateTime? holderBirthdate,
            string photoDataUrl, Guid? waiverSignatureId);
        /// <summary>Records a staff ID/age check against the pass holder. Tenant-scoped, paid
        /// passes only; returns rows affected.</summary>
        Task<int> SetIdVerified(Guid id, Guid tenantId, Guid? verifiedByUserId, DateTime? verifiedDob);
        /// <summary>Undoes a verification recorded in error. Tenant-scoped.</summary>
        Task<int> ClearIdVerified(Guid id, Guid tenantId);
        /// <summary>Burn one ride credit. Guarded (&gt; 0, paid, tenant-scoped); returns rows
        /// affected — 0 means no credit was available and the caller must abort the thing the
        /// credit was about to fund.</summary>
        Task<int> TryDecrementCredits(Guid purchaseId, Guid tenantId);

        /// <summary>Automatic credit hand-back (ticket refund / failed payment), capped at the
        /// product's total_credits and a no-op on non-paid or non-credit passes.</summary>
        Task IncrementCredits(Guid purchaseId, Guid tenantId, int by = 1);

        /// <summary>
        /// Admin support override: set a credits pass's remaining ride count outright.
        /// Guarded to credits passes (credits_remaining IS NOT NULL) and tenant-scoped.
        /// Returns rows affected — 0 means not found in tenant or not a credits pass.
        /// </summary>
        Task<int> SetCredits(Guid purchaseId, Guid tenantId, int credits);

        // Reservations
        Task<Guid> CreateReservation(SeasonPassReservation r);
        Task<SeasonPassReservation?> GetReservation(Guid purchaseId, Guid eventId);

        /// <summary>Resolve a reservation id to its event + the season-pass holder, for check-in
        /// waiver gating. Tenant-scoped through the purchase join. Null if not found in this tenant.</summary>
        Task<SeasonPassCheckInContext?> GetReservationForCheckIn(Guid reservationId, Guid tenantId);

        /// <summary>Same holder/registration context, but resolved from the pass itself (no
        /// reservation yet) — for walk-up gate redemption. ReservationId/EventId are unset.</summary>
        Task<SeasonPassCheckInContext?> GetPassForGateCheckIn(Guid passPurchaseId, Guid tenantId);

        /// <summary>
        /// Walk-up gate admission in ONE atomic statement: optionally burns a credit
        /// (guarded &gt; 0, paid, tenant-scoped) and upserts the (pass, event) reservation
        /// straight to checked_in (reviving a cancelled row if present). Returns null when
        /// the credit guard fails — the pass has no rides left; otherwise the reservation id
        /// and the post-burn credits (null for non-credit passes). Caller must hold the
        /// per-pass advisory lock and have pre-checked for an existing live reservation.
        /// </summary>
        Task<(Guid ReservationId, int? CreditsRemaining)?> CreateGateCheckIn(
            Guid passPurchaseId, Guid tenantId, Guid eventId, Guid? staffUserId, bool burnCredit);
        /// <summary>
        /// The no-event twin of CreateGateCheckIn, for a walk-up track open on a day with nothing
        /// on the calendar: optionally burns a credit (guarded &gt; 0, paid, tenant-scoped) and
        /// upserts the (pass, check-in date) reservation straight to checked_in, reviving a
        /// cancelled row if present. Returns null when the credit guard fails; otherwise the
        /// reservation id and post-burn credits. Caller MUST hold the per-pass advisory lock and
        /// have pre-checked GetWalkUpCheckIn for an existing checked_in row: the burn commits even
        /// when ON CONFLICT filters the insert out, so calling this on an already-admitted day
        /// burns a credit and returns null anyway. The index stops the duplicate row, not the
        /// duplicate burn.
        /// </summary>
        Task<(Guid ReservationId, int? CreditsRemaining)?> CreateWalkUpGateCheckIn(
            Guid passPurchaseId, Guid tenantId, DateTime checkInDate, Guid? staffUserId, bool burnCredit);

        /// <summary>Resolve a no-event walk-up admission for one pass on one tenant-local calendar
        /// day. Tenant-scoped through the join to season_pass_purchase, which is where the scope
        /// lives, since season_pass_reservation has no tenant_id. Null when no such row exists yet.</summary>
        Task<SeasonPassReservation?> GetWalkUpCheckIn(Guid passPurchaseId, Guid tenantId, DateTime checkInDate);

        /// <summary>Tenant-scoped reservation read for wristband linking: is this admission
        /// checked in, and what event/date scope should a band linked to it inherit?</summary>
        Task<SeasonPassReservationLinkContext?> GetReservationForBandLink(Guid reservationId, Guid tenantId);

        Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchase(Guid purchaseId);
        Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchaseOnDate(
            Guid purchaseId, Guid tenantId, DateTime atUtc, DateTime untilUtc, DateTime localDate);
        /// <summary>
        /// Updates a reservation's status. <paramref name="tenantId"/> is required and the
        /// SQL filter joins through season_pass_purchase to refuse updates against
        /// reservations that belong to a different tenant — so a staff JWT scoped to
        /// tenant A can't flip reservation status on tenant B's records.
        /// </summary>
        Task<int> UpdateReservationStatus(Guid id, Guid tenantId, string status, Guid? checkedInByUserId = null);

        /// <summary>Tenant-scoped cancel of a paid season-pass purchase (mirrors pass/ticket).</summary>
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason);
        Task MarkRefunded(Guid id, string? refundNote);

        // Capacity helper — number of active (reserved + checked_in) season pass spots per event.
        Task<Dictionary<Guid, int>> ActiveReservationsForEvents(IEnumerable<Guid> eventIds);
    }
}
