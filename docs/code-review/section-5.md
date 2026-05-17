# Section 5: Spectator, season-pass, membership, gift-card, rental, and event-extra sale paths

## Scope

Read end-to-end:

- `webapi/Controllers/SpectatorController.cs` — gate-fee spectator buy (extras-flavoured).
- `webapi/Controllers/SeasonPassController.cs` — product CRUD, `Buy`, `Reserve`, `CheckIn`,
  `LookupPassByToken`, `Mine`.
- `webapi/Controllers/MembershipController.cs` — `Buy`, `Status`, `UpdateSettings`, admin list.
- `webapi/Controllers/PurchaseController.cs` `BuyGiftCard` + the gift-card branches of `Buy*`.
- `webapi/Controllers/RentalController.cs` — full file (products + items + `Buy` + counter +
  maintenance).
- `webapi/Controllers/ExtraController.cs` — product / variant CRUD + `Buy` standalone.
- `webapi/Controllers/RedemptionController.cs` — gift-card / extras scan paths.
- `webapi/Controllers/PaymentController.cs` `StripeWebhook` end-to-end (membership / extras /
  rental / gift-card / season-pass branches).
- `Services/Repositories/SeasonPassRepository.cs`, `MembershipRepository.cs`,
  `GiftCardRepository.cs`, `RentalRepository.cs`, `EventExtraRepository.cs`.
- `Services/GiftCards/GiftCardValidator.cs`, `GiftCardDeliveryService.cs`.
- `TaskRunner/Program.cs` (looking for a delivery worker / abandoned-cart janitor).
- `vueapp/src/views/BuyGiftCard.vue`, `BuySpectator.vue`, `Rentals.vue`.

Section 1 (tenancy / auth), Section 2 (payments / webhook / ledger), Section 3 (schema /
migrations), and Section 4 (day-pass + event-ticket) findings are not repeated. Specifically not
repeated: gift-card balance race (Section 2 #2), rental ledger gap (Section 2 #1), rental deposit
refund idempotency (Section 2 #7), gift-card delivery-email duplicate (Section 2 #6), gift-card-paid
refund credit-back (Section 2 #10), webhook dispute coverage gap (Section 2 #12), `UpdateStatus`
unguarded (Section 2 #11), the season-pass free-cart fast path missing a ledger row (Section 2 #16),
the extras source-kind CHECK gap (Section 2 #5), abandoned-pending capacity holds (Section 4 #2),
membership/season-pass/rental/gift-card refund UI absent (Section 2 open question 5). The findings
below land *on top of* those.

## Architecture summary

**Six sale shapes, six webhook branches.** `PaymentController.StripeWebhook` looks up the PI
against seven tables (`pass_purchase`, `event_ticket_purchase`, `season_pass_purchase`, `gift_card`,
`rental_purchase`, `event_waitlist_entry`, `event_extra_purchase`, `membership_purchase`). For each
sale kind that matches, it runs a sale-kind-specific branch and decides whether to early-return.
The branches differ in three orthogonal ways:

| Kind | Ledger on succeeded | Confirmation email | Refund / cancel endpoint | Free-cart fast path |
|---|---|---|---|---|
| pass | `OnPaymentSucceeded` writes one entry per line | `SendPurchaseEmailAsync("pass")` | admin + rider self-cancel | `InsertZeroLedger("pass")` |
| event_ticket | `OnPaymentSucceeded` writes one entry per line | `SendPurchaseEmailAsync("event_ticket")` | admin + rider self-cancel | `InsertZeroLedger("event_ticket")` |
| season_pass | `OnSeasonPassPaid` (one entry) | `SendPurchaseEmailAsync("season_pass")` | **none** | **none** (Section 2 #16) |
| membership | `OnMembershipPaid` (one entry) | **none** | **none** | **none** |
| extras | flips `paid` only; ledger gated on the `source_kind` CHECK | **none** | **none** | flip only, no ledger |
| rental | flips `paid` only; **no ledger ever** (Section 2 #1) | **none** | **none** | flip only, no ledger |
| gift_card | fires `GiftCardDeliveryService.SendDeliveryEmail` for immediate-delivery cards; no ledger of its own (gift-card *purchase* is treated as a deferred-revenue instrument, not as recognised revenue) | recipient delivery email; **no buyer receipt email** | **none** | n/a (gift-card purchases can't be paid by gift card) |

The shape gap above is the dominant theme of this section — the post-payment side-effects for
day-pass / event-ticket are richly handled, and the other five kinds are progressively less complete.
Section 2 already covered the ledger and dispute coverage gaps; this section walks the *confirmation
email + cancel + state-machine + race* gaps that compound on top.

**Reservation system on top of season passes.** Season-pass holders create `season_pass_reservation`
rows when they Reserve an event spot. The reservation table doesn't carry `tenant_id` directly; the
patched `UpdateReservationStatus` joins through `season_pass_purchase` for the tenant filter.
Reservation status flows `reserved → checked_in | cancelled`. There is no rider-facing "cancel my
reservation" endpoint and no path that credits a reservation back to a credits-based pass.

**Rental units are tracked two ways.** `pool` products check `SumOverlappingPoolReserved` against
`inventory_pool` on a date-range overlap. `per_item` products call `PickAvailablePerItemUnits` to
get a list of `rental_item.id`s that have no overlapping booking and no overlapping maintenance,
and persist the assignment in `rental_purchase_item`. Both queries filter to `rp.status IN
('paid','out')` — a *pending* booking holds no capacity. The schema's unique key on
`rental_purchase_item` is `(purchase_id, item_id)`, not `(item_id)` — see Finding 5.1.

**Variant inventory across extras.** Each `event_extra_variant` carries an optional tenant-wide
`inventory` cap. `event_extra_product` also carries a tenant-wide product-level cap. Per-event
inventory lives in `event_extra_eligibility.inventory` for the legacy single-SKU path. All three
caps are read-then-insert with no row lock; pending purchases are excluded from the sold count.

**Membership gate.** `tenant.MembershipRequiredForRiders` gates `BuyPass`, `BuyEventTicket` race-entry
tier, `SeasonPassController.Buy`. `tenant.MembershipRequiredForSpectators` gates `ExtraController.Buy`.
**Neither gate is consulted by `SpectatorController.Buy`** (Section 4 already noted Spec tiers in
`BuyEventTicket` skip the spectator waiver; the membership-gate gap is its sibling).

## Findings

| # | Severity | Title | File / location |
|---|---|---|---|
| 5.1 | **Critical** | Rental per-item double-assignment: `rental_purchase_item` unique key is `(purchase_id, item_id)` rather than `(item_id)`, and `PickAvailablePerItemUnits` excludes `pending` purchases; two concurrent bookings pick + assign the same unit | `RidePass.Migrator/Scripts/Script0048_Rentals.sql:127`, `Services/Repositories/RentalRepository.cs:198-222, 330-340`, `webapi/Controllers/RentalController.cs:316-336, 410-416` |
| 5.2 | **Critical** | Event capacity is not unified across day-pass / event-ticket / season-pass reservations — only `SeasonPassController.Reserve` sums both buckets; `BuyPass` ignores season-pass reservations and `BuyEventTicket` skips the event-capacity check entirely (only tier inventory) | `webapi/Controllers/PurchaseController.cs:265-273, 661-669`, `SeasonPassController.cs:408-418` |
| 5.3 | **Critical** | Variant + per-event extras inventory is read-then-write with `pending` excluded from the sold count; two concurrent buyers picking the last "L Red" both pass and oversell. Same for product-level `inventory` cap | `Services/Repositories/EventExtraRepository.cs:103-109, 273-279, 358-365`, `webapi/Controllers/ExtraController.cs:239-294`, `webapi/Controllers/SpectatorController.cs:90-127` |
| 5.4 | **Critical** | Bundled-membership free-cart fast path leaves the membership in `pending` forever — `BuyPass` / `BuyEventTicket` flip the pass/ticket to paid but never the bundled membership row, so `GetActive` returns null and the rider's membership-gated purchases all reject | `webapi/Controllers/PurchaseController.cs:482-532, 1020-1072` |
| 5.5 | High | `SpectatorController.Buy` never gates on `requires_waiver` extras (pit-vehicle, etc.) — the only waiver consulted is the event's *spectator* waiver. A spectator buying a "pit pass" with `requires_waiver=true` never has a waiver collected, while the same product through `BuyPass`/`BuyEventTicket`/`ExtraController.Buy` does | `webapi/Controllers/SpectatorController.cs:69-77, 90-127` |
| 5.6 | High | `SpectatorController.Buy` skips both membership gates (`MembershipRequiredForRiders` and `MembershipRequiredForSpectators`) and the `requires_emergency_contact` profile check; the FE has no membership-gate UI here either, so a track that requires memberships sells gate fees + camping to anyone | `webapi/Controllers/SpectatorController.cs:56-67` |
| 5.7 | High | Future-scheduled gift cards are never delivered. `TaskRunner/Program.cs` runs only `MonthlyPayoutDrafter`; the gift-card delivery worker that `IGiftCardDeliveryService.SendDeliveryEmail` was designed for doesn't exist. `GiftCardRepository.ListPendingDelivery` is defined but uncalled | `TaskRunner/Program.cs` (whole file), `Services/Repositories/GiftCardRepository.cs:90-103`, `Services/GiftCards/GiftCardDeliveryService.cs:37-66` |
| 5.8 | High | Membership / extras / spectator / rental confirmation emails are never sent on `payment_intent.succeeded`. `OnMembershipPaid` and the extras / rental webhook branches flip status and (sometimes) write the ledger, but never call `SendPurchaseEmailAsync`. The spectator Vue says "We've emailed your QR codes" — that email never goes out | `webapi/Controllers/PaymentController.cs:188-238, 261-287` |
| 5.9 | High | Season-pass `Reserve` is not atomic: capacity check + reservation insert + `DecrementCredits` are three separate statements. Two parallel `Reserve` calls on a credits-based pass with `credits_remaining=1` both create reservations, but only one credit is decremented. Net effect: rider has 2 reservations using 1 credit | `webapi/Controllers/SeasonPassController.cs:408-433`, `Services/Repositories/SeasonPassRepository.cs:236-253` |
| 5.10 | High | No "cancel my reservation" endpoint on season passes, and no credit-restore path. A rider who reserves and changes their mind has no way to free their credit — only staff via `ReportsController.SetCheckIn` can flip status, but that doesn't credit the pass back | `webapi/Controllers/SeasonPassController.cs` (no endpoint), `Services/Repositories/SeasonPassRepository.cs` (no CreditBack method) |
| 5.11 | High | No `cancel` endpoint exists for season passes, memberships, rentals, gift cards, or event extras. The status CHECK constraints allow `cancelled`; the SQL columns (`cancellation_reason`, `cancelled_by_user_id`, `cancelled_at`) exist; only the controllers + repository methods are missing. Combined with Section 2's "no refund UI" for the same five kinds, this leaves admins with no remediation path short of direct SQL when a rider asks for a refund | `webapi/Controllers/SeasonPassController.cs`, `MembershipController.cs`, `RentalController.cs`, `ExtraController.cs`, `PurchaseController.cs:1322-1418` |
| 5.12 | High | `RentalController.Buy` calls `PickAvailablePerItemUnits` *twice* (once for the capacity check at line 330, once for the actual assignment at line 412). Between the two calls another booking can swoop in and claim the units. Even without 5.1's missing unique index, this drops the practical contention window from "any time before paid" to "a few ms" but doesn't close it | `webapi/Controllers/RentalController.cs:316-336, 410-416` |
| 5.13 | High | Rental Buy + free-cart path leaves the rental `paid` immediately on gift-card-fully-covered, but skips both the ledger insert and the confirmation email — same as the Stripe success branch (Section 2 #1) but additionally without the ledger backstop the dashboard reconciliation has any chance of catching | `webapi/Controllers/RentalController.cs:438-453`, `Services/Repositories/RentalRepository.cs:298-302` |
| 5.14 | High | Rental has no cancellation endpoint at all. The `cancelled` status is in the CHECK constraint but no code path writes it. A booking that the rider needs to cancel before pickup can't be undone — admin would have to mark it returned (which fires a Stripe refund of the deposit only, not the rental fee) | `webapi/Controllers/RentalController.cs` (no Cancel endpoint), `RidePass.Migrator/Scripts/Script0048_Rentals.sql:95-96` |
| 5.15 | High | Gift-card BuyGiftCard accepts any non-empty `RecipientEmail` — no email-format validation. A typo on the recipient address gets through, the delivery email bounces, and there's no retry / error surface. (Server returns 200; only the delivery worker would see the bounce, and the worker doesn't exist — see 5.7.) | `webapi/Controllers/PurchaseController.cs:1373-1374`, `webapi/Controllers/API/Data/Purchase/BuyGiftCardRequest.cs` |
| 5.16 | High | Gift-card recipient has no public lookup endpoint. The delivery email contains the code only; there's no "click here to view your balance" page, no public-token-based check. The recipient can't see what's left after a partial redemption without inspecting the gift card in another buy flow's coupon field | (no endpoint), `webapi/Controllers/RedemptionController.cs` (gift cards not handled), `vueapp/src/views/` (no Gift card lookup view) |
| 5.17 | Medium | `SeasonPassController.Buy` does not consult `product.RequiresWaiver` — the pass row's `WaiverSignatureId` is always null even when the product flag is set. The gate-check on `Reserve` doesn't enforce it either. A track that sets `requires_waiver=true` on a season-pass product gets no waiver collected | `webapi/Controllers/SeasonPassController.cs:177-339, 363-434`, `Services/Repositories/Data/PaymentData/SeasonPassProduct.cs` (the `RequiresWaiver` column is read but never gated on) |
| 5.18 | Medium | `RentalController.MarkOut` allows any `paid` rental to be marked out by counter staff at any time — there's no check that today is between `start_date` and `end_date`. A rider who paid in advance for next week's booking can have staff "hand them the bike" right now if the staff clicks the button on the wrong row | `webapi/Controllers/RentalController.cs:587-612` |
| 5.19 | Medium | Inventory caps (`product.Inventory`, `variant.Inventory`, `eligibility.Inventory`) all exclude `pending` sales from the sold count. Combined with no abandoned-cart janitor (Section 4 #2), a buyer hitting "Continue to Payment" then closing the tab DOES eventually free the inventory (it never held in the first place), but two simultaneous buyers can both pass the cap check — the more concerning side of the same coin as Finding 5.3 | `Services/Repositories/EventExtraRepository.cs:103-109, 273-279, 358-365` |
| 5.20 | Medium | Rental return flow allows `paid → returned` directly without ever passing through `out`. A rental that was paid but never checked out (rider no-show) can be "returned" with full deposit refund — possibly correct, but the status machine doesn't reflect "no-show" vs "returned in good condition" | `webapi/Controllers/RentalController.cs:614-660` |
| 5.21 | Medium | Gift-card immediate-delivery scheduling races with redemption: the buyer can apply the freshly-minted gift card to their own next purchase *before* the webhook fires `SendDeliveryEmail` (delivery_status stays `pending` and `ScheduledDeliveryAtUtc` is null, so `GiftCardValidator` doesn't block). The recipient never even gets the email if the buyer drains it first | `Services/GiftCards/GiftCardValidator.cs:36-40`, `webapi/Controllers/PaymentController.cs:163-170` |
| 5.22 | Medium | `MembershipController.UpdateSettings` updates `tenant.MembershipPriceCents` mid-purchase. A rider who initiated `Buy` 30 seconds before sees their `PriceCents` frozen on the purchase row, but a new tenant-admin can lower the price right after — refunds-on-cancel and self-cancel partial refund use `purchase.AmountCents` (snapshotted) and Stripe (which has the original PI amount), so this is more a transparency thing than a money bug, but admins should be told the price change doesn't backfill | `webapi/Controllers/MembershipController.cs:163-179` |
| 5.23 | Medium | `SpectatorController.CheckSignature` is anonymous — anyone with a tenant subdomain can probe whether `email@example.com` has signed a given waiver. The boolean response is low-bit, but it's a user-existence oracle (an email that signed has a `tenant_user` row in some flows). Add rate-limiting and/or scope to a per-event link | `webapi/Controllers/SpectatorController.cs:45-54` |
| 5.24 | Medium | `RentalRepository.UpdateProductSortOrders` doesn't validate that the supplied ids belong to the tenant before unnesting them into the UPDATE. The `WHERE p.tenant_id = @tenantId` clause prevents cross-tenant *writes* but a leaked id from another tenant silently gets a no-op; the API should reject mixed-tenant requests rather than swallowing them | `Services/Repositories/RentalRepository.cs:116-131` (and twins on season-pass, extras, pass-product repositories) |
| 5.25 | Medium | `BuyGiftCard` uniqueness retry uses `GetByCode` which is `WHERE tenant_id = @tenantId` scoped — codes are unique *per tenant* but not globally. Two tenants can have identical `GIFT-AAAA0000` codes. The redemption flow always passes the request tenant in, so this is contained, but a counter-staff who scans a gift-card from a guest at the *wrong* tenant gets a clean "not found" rather than a meaningful "this is a different tenant's gift card" | `webapi/Controllers/PurchaseController.cs:1351-1362`, `Services/Repositories/GiftCardRepository.cs:50-54` |
| 5.26 | Medium | `SeasonPassController.Buy` requires a photo data-url (`IsValidPhotoDataUrl`) and bounds the size to 2 MB. The check accepts JPEG and PNG and rejects below 1 KB, but the photo lives on the purchase row indefinitely as a base64 string — a 1.9 MB base64 photo bloats every read of `season_pass_purchase` (e.g. the rider's `Mine` list query that materializes columns the FE doesn't render). Move to `image_storage` like the rest of the codebase | `webapi/Controllers/SeasonPassController.cs:211-214, 248, 482-489` |
| 5.27 | Medium | Bundled-membership and bundled-extras get the *combined* PI metadata, but the webhook handler's fall-through chain is order-sensitive: extras are handled *before* the season-pass branch but only by `_extras.UpdateStatus("paid")` — no ledger, no email. Same for memberships when bundled with extras/passes. Acceptable today because Section 2 already noted the missing-ledger-for-extras (#5) and the missing-confirmation-email-for-extras (in this section as 5.8), but the fall-through is fragile if anyone reorders the if-blocks | `webapi/Controllers/PaymentController.cs:188-238` |
| 5.28 | Low | `RentalController.Buy`'s "lost the units between check and assignment" branch returns a 400 *after* persisting the `rental_purchase` row, the coupon redemption, and the optional gift-card redemption. The user sees an error and the system carries an orphan `pending` row + a redeemed coupon they didn't get to use | `webapi/Controllers/RentalController.cs:393-416` |
| 5.29 | Low | `SpectatorController.Buy` lets `gateFeeUnits == 0` only when waiver is null (no `RequiresSpectatorWaiver`); a track with no spectator waiver can have a "Spectator" purchase with zero gate fees — which contradicts the `if (gateFeeUnits == 0) return BadRequest` guard that fires *before* the waiver check. Reads correctly but the validation order is brittle | `webapi/Controllers/SpectatorController.cs:131-145` |
| 5.30 | Low | `SeasonPassController.Buy` records `_coupons.RecordRedemption` immediately on insert even when the purchase is `pending`. If the user abandons the cart, the coupon redemption stays — affecting `max_uses_per_user` for future legitimate uses. Same pattern in `RentalController.Buy:395-406` and the pass / ticket sale paths (Section 4) but worth noting it's not unique to passes | `webapi/Controllers/SeasonPassController.cs:254-265`, `RentalController.cs:395-406` |
| 5.31 | Low | `RedemptionController.Order` doesn't include `gift_card` or `season_pass_purchase` lookups. A staff scanning a season-pass QR via the gate worker app sees a single anchor item, not the rider's reservations for today (those live on a separate `SeasonPass/Pass/{token}` endpoint). Two different "scan a QR" UIs results | `webapi/Controllers/RedemptionController.cs:91-242` (no season-pass / gift-card branches), `SeasonPassController.cs:438-480` |
| 5.32 | Low | `ExtraController.Buy`'s waiver-required gate hoists a *single* signature into the cart (`signatureId` is computed once per cart, not per product). If two different `requires_waiver` extras are in the same cart and the rider has signed only one of the two waivers, the cart silently uses the first looked-up signature for both rows. Today there's only one active waiver per tenant so this is dormant, but the design implies multi-waiver support is coming | `webapi/Controllers/ExtraController.cs:418-446, 460-488` |
| 5.33 | Low | `MembershipController.Buy` always returns `RiderServiceChargeCents = 0` even when `ServiceChargeBps > 0` and `MembershipPriceCents > 0`. The comment says memberships are tenant-funded today and "if this needs a rider-paid bps later, mirror the per-product pattern" — but `serviceCharge` is computed (line 109) and stored on the row (line 124), then ignored. Either remove the dead compute or wire the response | `webapi/Controllers/MembershipController.cs:109-124, 155-161` |
| 5.34 | Low | Gift-card buyer's own receipt email never fires. The buyer pays $50 + service charge, the webhook fires `SendDeliveryEmail` to the *recipient*, but the buyer (who's also a tenant user with a profile) gets no purchase confirmation in their inbox — only what Stripe sends if `receipt_email` was set on the PI. The Stripe receipt is minimal and doesn't say "you sent X a gift card" | `webapi/Controllers/PaymentController.cs:163-170` |
| 5.35 | Low | `EventExtraRepository.SumSoldVariant` is tenant-agnostic (no `tenant_id` predicate). Variant ids are globally unique so this is safe in practice, but a copy-pasted query that omits the tenant filter is a Section-1 pattern the CLAUDE.md rule wants flagged | `Services/Repositories/EventExtraRepository.cs:358-378` |

## Critical findings expanded

### 5.1 — Critical — Rental per-item double-assignment

`RentalRepository.AssignItems` writes into `rental_purchase_item` whose unique key is
`(purchase_id, item_id)`:

```sql
CREATE TABLE rental_purchase_item (
    purchase_id  uuid REFERENCES rental_purchase(id) ON DELETE CASCADE,
    item_id      uuid REFERENCES rental_item(id) ON DELETE RESTRICT,
    ...
    UNIQUE (purchase_id, item_id)
);
```

So two different `purchase_id` rows can both claim the same `item_id`. The `ON CONFLICT DO NOTHING`
clause in `AssignItems` doesn't even trip because no conflict happens. Compounding:

- `PickAvailablePerItemUnits` filter is `rp.status IN ('paid','out')` — pending purchases hold no
  capacity. Buyer A picks unit `LBLZ-7`, creates a pending purchase, and assigns it. Buyer B
  immediately runs the same query against the same date range, gets `LBLZ-7` (it's still
  `status='available'` on `rental_item` and the only matching `rental_purchase_item` row is on a
  *pending* purchase that the filter excludes), and assigns it too.
- Both buyers' PIs then both succeed. Both purchases flip to `paid`. Both rows in
  `rental_purchase_item` point at the same unit.

The fix is two coordinated changes:

1. Schema: drop the existing unique key, add `CREATE UNIQUE INDEX uk_rental_purchase_item_per_item
   ON rental_purchase_item (item_id) WHERE ...` — but it can't be partial-by-date-range, so we'd
   need to either (a) move to per-unit *interval* locking (e.g. an `int4range` overlap exclusion
   constraint), or (b) accept a single-unit-cannot-overlap-anywhere unique key and reconcile by
   relying on the assignment being short-lived (assigned at booking, freed on return).
2. Repository: wrap `PickAvailablePerItemUnits` + `AssignItems` in a single transaction with
   `SELECT … FOR UPDATE` on the `rental_item` rows.

Either alone is insufficient; the schema change without serialization still has the read-then-write
race, and the serialization without the schema check has nothing to fail closed against. The
cleaner long-term answer is an exclusion constraint on `rental_purchase_item` with `tstzrange`
covering `start_date` to `end_date`.

### 5.2 — Critical — Event capacity not unified across sale kinds

`event.capacity` is supposed to be the cap across *all* admissions. Today only one of three
purchase paths consults all three buckets:

- `SeasonPassController.Reserve`: sums `_passPurchases.ActiveSpotsReservedForEvent` +
  `_passes.ActiveReservationsForEvents` and rejects when the sum hits capacity. **Correct.**
- `PurchaseController.BuyPass`: reads `_purchases.ActiveSpotsReservedForEvent(eventId.Value)` only.
  **Ignores season-pass reservations entirely.**
- `PurchaseController.BuyEventTicket`: reads `_tiers.SoldCount(tier.Id)` against `tier.Inventory`
  only. **Skips the event-level capacity check completely** — a tier with no inventory cap will
  sell unlimited tickets regardless of `event.capacity`.

So:

1. Track sets `event.capacity = 10`. Ten season-pass holders reserve. `SeasonPassController.Reserve`
   correctly refuses an 11th.
2. A day-pass buyer hits `/Purchase` immediately after. `BuyPass`'s capacity check uses only
   `ActiveSpotsReservedForEvent` which counts day-pass + event-ticket-spectator rows, not
   season-pass reservations. The day-pass buyer succeeds — capacity is now 11/10.
3. Worse: the same track has a "$0 Day Of Race" event ticket tier with no inventory cap.
   `BuyEventTicket` cheerfully sells a 12th, a 13th, an Nth ticket because event capacity is never
   checked at the tier-buy site.

Fix: extract the cap-summing logic into a `IEventCapacityChecker` service that all three call sites
share; the check is "summed reservations across all three tables >= event.capacity". Combined with
abandoned-cart cleanup (Section 4 #2) so `pending` rows don't permanently consume capacity.

### 5.3 — Critical — Variant + extras inventory race

`SumSoldProduct`, `SumSold(eventId, productId)`, and `SumSoldVariant(variantId)` all filter by
`status IN ('paid','redeemed')` — *pending* purchases aren't counted. The four read sites in
`ExtraController.ResolveVariantOrError` and the equivalent path in `SpectatorController.Buy` all
do `remaining = cap - sold; if (item.Quantity > remaining) reject`. Then they `_extras.CreatePurchase`
in a separate statement.

So two buyers picking the last "L Red" t-shirt both pass the check, both create `pending` rows,
both succeed at payment, both flip to `paid`. Inventory ends up at `sold=2, cap=1`. Same for
product-level `tenant.Inventory` and per-event `event_extra_eligibility.inventory`.

The fix mirrors the season-pass credit-decrement pattern (which *is* atomic via guarded UPDATE):
either move the cap enforcement to a single SQL statement that increments a `sold_count` column
on the variant row guarded by `WHERE sold_count + @qty <= inventory`, or wrap the read + create
in a transaction with row-level locking on the variant. Today, the only protection is "few enough
concurrent buyers that the race window doesn't matter" — which is OK for a small track on day-of
but fails at scale and fails for the very-popular limited-edition merch case.

### 5.4 — Critical — Bundled-membership free-cart fast path strands the membership

`PurchaseController.BuyPass` at lines 482-504 creates a `pending` membership row when
`bundleMembership=true`. Then at line 511 the free-cart fast path fires when
`combinedStripeChargeCents == 0` and only flips the pass + extras:

```csharp
if (combinedStripeChargeCents == 0)
{
    await _purchases.UpdateStatus(purchase.Id, "paid");
    await InsertZeroLedger(_tenantContext.TenantId, "pass", purchase.Id);
    foreach (var exId in extraPurchaseIds)
    {
        await _extras.UpdateStatus(exId, "paid");
    }
    // ⚠ bundledMembershipPurchaseId is never flipped to paid here ⚠
    if (request.RewardRedemptionId.HasValue)
        await _rewards.MarkRedemptionUsed(...);
    return new ApiResponses().OkResult(...);
}
```

Same bug at line 1048 in `BuyEventTicket`'s free-cart fast path: the bundled membership is created
at line 1040 but never flipped to paid when `combinedStripeChargeCents == 0`.

Consequence: a rider with a 100% off voucher (or gift card that fully covers a free pass) who opts
into the bundled membership gets the pass for free, but `membership_purchase.status` stays
`pending`. `MembershipRepository.GetActive` filters by `status = 'paid'`, so the membership
"doesn't exist" for any downstream gate check. The user's next purchase (which would now be
membership-gated) rejects with "Participants are required to have an active membership."

Fix: in both free-cart paths, when `bundledMembershipPurchaseId.HasValue`, call
`_memberships.UpdateStatus(bundledMembershipPurchaseId.Value, "paid")` and write a $0 ledger row
mirroring Section 2's `extras` decision.

## High findings worth pulling forward

### 5.5 — High — Spectator extras with `requires_waiver=true` bypass the waiver gate

`SpectatorController.Buy` only consults the *event-level* spectator waiver
(`ev.RequiresSpectatorWaiver`). The line-level `product.RequiresWaiver` flag — which `ExtraController.Buy`
(line 435) and `BuyPass` / `BuyEventTicket` (via `extrasNeedWaiver` in `PurchaseController`) do
gate on — is ignored.

So a track that sets `requires_spectator_waiver=false` on the event but `requires_waiver=true` on
a "Pit Pass" extras product sells the pit pass through the spectator flow with no waiver. The same
product sold through the rider flow (`BuyPass` cart) does collect a waiver. Two flows, two policies.

Fix: in `SpectatorController.Buy`, after gathering the cart lines, check `lines.Any(l =>
l.Product.RequiresWaiver)` and require a signature for those rows. Decision call: spectator
purchases are guest-anonymous so the rider doesn't have a stored signature — the per-spectator
waiver collection in step 3 of `BuySpectator.vue` already covers the *event* waiver; you'd extend
the requirement so any cart-line with `requires_waiver` also tracks the signature on its row.

### 5.7 — High — Future-scheduled gift cards never get delivered

`PaymentController.StripeWebhook` (line 163-170) calls `_giftCardDelivery.SendDeliveryEmail(giftCard)`
inline, but only when `!ScheduledDeliveryAtUtc.HasValue || ScheduledDeliveryAtUtc.Value <= UtcNow`.
For a scheduled-for-tomorrow card, the webhook returns Ok and nothing further happens. The
delivery worker that would scan `gift_card` for due-but-undelivered rows doesn't exist —
`TaskRunner/Program.cs` only runs `MonthlyPayoutDrafter`. The query
`GiftCardRepository.ListPendingDelivery` is wired up and unused.

Rider impact: buyer pays for a Mother's-Day gift card scheduled for Mother's Day, the recipient
never gets it.

Fix: add a `GiftCardDeliveryWorker` to `TaskRunner` that walks `ListPendingDelivery(now, 100)`
each tick and calls `SendDeliveryEmail` on each (the service already short-circuits via
`MarkDelivered` so it's safe under re-delivery once Section 2 #6 lands).

### 5.8 — High — Membership / extras / spectator / rental confirmation emails never fire

The webhook handler's per-kind branches for memberships (line 188-202), extras (line 208-224),
and rentals (line 226-238) flip status and (for membership and pass/ticket) write a ledger row,
but **none** of them call `SendPurchaseEmailAsync` (or its membership / rental / extras equivalent).

UX impact:

- **Spectator**: `BuySpectator.vue` line 220 says "We've emailed your QR codes to {purchaserEmail}".
  No email is sent. The rider drives to the track expecting to show the email on their phone.
- **Membership**: rider pays $X for a yearly membership. Nothing arrives. They don't know if it
  worked except by re-loading `/Membership` and seeing the active card.
- **Rental**: rider books a bike for next weekend. Nothing arrives. The "MyRentals" view shows
  the booking but there's no inbox proof.

Fix: in `OnMembershipPaid`, extras-flip block, and rental-flip block, mirror the
`SendPurchaseEmailAsync` call pattern from `OnPaymentSucceeded`. Add new kind strings to the
`switch (kind)` in `SendPurchaseEmailAsync` for "membership", "rental", "spectator", "event_extra".

### 5.9 — High — Season-pass `Reserve` race on credits

```csharp
// Check capacity
if (ev.Capacity.HasValue) { ... }

// Check existing reservation
var existing = await _passes.GetReservation(...);
if (existing is not null && existing.Status != "cancelled") return alreadyReserved;

// Create reservation
var reservationId = await _passes.CreateReservation(...);
if (product.Kind == "credits") await _passes.DecrementCredits(pass.Id);
```

The credit check at line 403 reads `pass.CreditsRemaining`, but `pass` was loaded at line 370 —
before any of the per-call concurrent traffic. Two parallel `Reserve` calls on a credits=1 pass
for two different events both pass the read-time check (both see 1), both `CreateReservation`,
and only one of the `DecrementCredits` writes succeeds (the other sees credits=0 and the
`WHERE credits_remaining > 0` guard short-circuits).

Net effect: 2 active reservations, 1 credit charged. The pass holder gets a free event.

Fix: combine the credit decrement and the reservation insert in a single transaction with
`SELECT credits_remaining FROM season_pass_purchase WHERE id = @id FOR UPDATE`, decide, write,
commit. Alternative: do the `DecrementCredits` *first* and only `CreateReservation` if the
decrement affected 1 row (i.e. the credit was successfully consumed).

### 5.13 — High — Rental free-cart path skips ledger + email

`RentalController.Buy` at line 441 (free fast-path) does `await _rentals.UpdateStatus(purchase.Id,
"paid")` and returns. No ledger insert (which mirrors the Stripe success branch — see Section 2
#1, which flags both as the same "no rental ledger ever" Critical). No confirmation email.

The rider got a free rental booking (gift card covered everything) and nothing exists in the
ledger to back-derive that revenue. The dashboard's "rental revenue" widget shows $0 even though
the gift card was burned.

Fix: same as the Stripe-success branch. Mirror `InsertZeroLedger` for rentals once the `source_kind
= 'rental'` CHECK gap is addressed (Section 2 #5 — extras + rental both need to be added).

## Coverage notes

What I verified explicitly:

- `SpectatorController.Buy` per-spectator waiver enforcement: minor → fresh signature, adult-self
  with on-file signature → skipped, all others → fresh signature. **Correct.**
- Bundled add-ons riding along on a spectator order (camping, parking, merch) get `WaiverSignatureId
  = null` on their rows since they're not gate-fee units — correct intent.
- `SeasonPassController.UpdateReservationStatus` is consistent across both callers
  (`SeasonPassController.CheckIn` and `ReportsController.SetCheckIn`) — both pass `tenantId` after
  the Section 1 patch.
- `MembershipRepository.GetActive` correctly handles lifetime memberships
  (`valid_to_utc IS NULL`) and orders nulls-first so a lifetime row outranks a yearly row.
- Boundary semantics: `valid_to_utc > @nowUtc` is exclusive. A membership with `valid_to_utc`
  exactly `now` is *not* active. Fine for yearly memberships (no business value to a partial
  second), but worth flagging if a future change introduces hourly/short-term memberships.
- `GiftCardValidator.ResolveAsync` blocks future-scheduled cards from being redeemed before
  delivery, but does *not* block immediate-delivery cards that haven't been emailed yet (Finding
  5.21).
- `RentalController.MarkReturned` correctly handles `damaged` vs `returned` via
  `depositCapturedCents > 0` → damaged state.
- Variant uniqueness across `(size, color, gender)` is enforced by a unique index — the FE upsert
  catches 23505 and surfaces "A variant with the same size / color / gender already exists."
- Drag-drop sort_order: `ReorderProducts` on extras, season-pass, and rental all use the
  unnest-pair pattern; reads of the variants list in `ExtraController.ListVariants` sort by
  `sort_order`. **Buyer sees the right order.**
- `MembershipRequiredForRiders` and `MembershipRequiredForSpectators` gates: enforced in
  `BuyPass`, `BuyEventTicket` (race-entry), `SeasonPassController.Buy`, `ExtraController.Buy`.
  **Not** enforced in `SpectatorController.Buy` (Finding 5.6) or `RentalController.Buy` (also a
  gap, but rentals are a separate audience — flagging the spectator one is more pointed).

What I did not re-verify (deferred to earlier sections):

- The gift-card balance race itself (Section 2 #2).
- Rental deposit refund idempotency / Stripe error swallow (Section 2 #7).
- The webhook handler's pass/ticket-only dispute coverage (Section 2 #12).
- `UpdateStatus` lacking `WHERE status = 'pending'` row count guards (Section 2 #11).
- The waiver-enforcement gap for spectator-pass tier in `BuyEventTicket` (Section 4 #1).
- Abandoned-cart janitor (Section 4 #2) — the lack of one compounds 5.3, 5.12, 5.19, 5.14.
- `v_recent_sales` coverage for rental / membership / season-pass / extras / gift-card rows — not
  in this section's scope, but the CLAUDE.md radar applies if these sale kinds are added without
  updating the view.

## Open questions

1. **Is gift-card *purchase revenue* meant to land on the ledger?** Today it doesn't — the gift
   card itself is treated as a deferred-revenue instrument, recognised only when applied to a real
   purchase. That's a reasonable accounting stance, but Section 2's reconciliation queries don't
   account for it; the dashboard's "gross sales" understates by the value of outstanding gift cards.
2. **Spectator-pass tier in `BuyEventTicket` vs. SpectatorController extras flow** (Section 4 #1).
   Are these meant to be two endpoints for the same audience, or two distinct paths (tier-priced
   spectator-pass vs. always-the-same-product gate-fee)? If one path is meant to be deprecated,
   say so and remove the duplicate waiver-enforcement gap.
3. **Should `RentalController` get a self-cancel endpoint paralleling `MeController.CancelMyPass`?**
   The Vue side has no rental-cancel button anywhere; admins can hand-mark returned (which refunds
   the deposit only). For a track that takes a $400 booking 30 days out and the rider's bike breaks,
   they can't cancel without contacting the track manually.
4. **What's the policy for season-pass reservation cancel + credit restore?** Finding 5.10 notes
   the gap. If the policy is "use it or lose it once reserved," document that in the rider FAQ
   and remove the un-call-able `cancelled` reservation status. If the policy is "rider can cancel
   24h ahead and get the credit back," wire that endpoint.
5. **Why is `RentalController.MarkOut` unconditionally allowed for any `paid` rental regardless
   of date?** (Finding 5.18.) Is the intent that counter staff have full discretion (e.g. rider
   shows up early), or is this an oversight where the date range should clamp the action?
