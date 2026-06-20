# QA Test Plan: Passes & Season Passes

> Scope: season pass products (admin CRUD, pricing, validity window, kinds, per-event-type perks), rider purchase (coupon/gift card/photo), reservations against event capacity, validity/expiry, waiver requirement, QR redemption / gate check-in, plus the surviving membership "pass" and the now-removed day pass. Money correctness and multi-tenant isolation. Last updated: 2026-06-20.

## Surface map

- **Season pass products (admin, `CatalogManage`):** `SeasonPassController` `GET Products/Admin`, `POST Products`, `PUT Products/{id}`, `POST Products/Reorder`, `DELETE Products/{id}`. UI: `vueapp/src/views/Admin/SeasonPasses.vue` (drag-reorder list + create/edit dialog). Repo: `Services/Repositories/SeasonPassRepository.cs` / `ISeasonPassRepository`. Tables: `RidePass.Migrator/Scripts/Script0035_SeasonPasses.sql`.
- **Season pass public list:** `GET SeasonPass/Products` (anonymous, active-only). UI: `vueapp/src/views/BuySeasonPass.vue`, plus `Home.vue` "Season Passes" hero CTA + "From $X" card.
- **Season pass purchase (rider):** `POST SeasonPass/Buy` (`[Authorize]`, authed only). Coupon scope `"season_pass"`, gift card as a payment instrument, required selfie `photoDataUrl`, rider-paid service-charge portion, free fast-path when a gift card covers the full amount. UI: `BuySeasonPass.vue`.
- **My passes:** `GET SeasonPass/Mine`. UI: `vueapp/src/views/User/SeasonPasses.vue` (renders the QR from `redemption_token`).
- **Reservation (rider):** `POST SeasonPass/Reserve` (`[Authorize]`). Validates ownership, tenant, paid status, event date inside the validity window, day-of-week, credits, and event capacity. **Not wired into any Vue view** (see risks).
- **Gate scan / check-in (staff, `SalesRedeem`):** `GET SeasonPass/Pass/{token}` (lookup by QR, returns holder photo + today's reservations), `POST SeasonPass/Reservations/{id}/CheckIn`. **Not wired into any Vue view** (the rider-facing `Redeem.vue` scanner uses the event-ticket redeem service, not these endpoints).
- **Payment confirmation:** `webapi/Payments/StripePurchaseFinalizer.cs` `OnSeasonPassPaid` flips `pending` to `paid` on `payment_intent.succeeded`, writes the ledger sale row, and sends the purchase email.
- **Refund / cancel:** `PurchaseController` `Refund` with `kind = "season_pass"` (cancels the purchase, releases its non-cancelled reservations, marks refunded; default withholds the service charge). Surfaced in Admin -> Purchases (`PassService.ts` `refund`).
- **Feature toggle:** `tenant.season_passes_enabled` (`Script0064_SeasonPassesEnabled.sql`, default true). Admin/Settings/Features.vue; `branding.seasonPassesEnabled` hides nav + CTAs and `Buy`/`Reserve` reject when off.
- **Membership ("pass"-style program):** `MembershipController` `GET Status`, `POST Buy` (`[Authorize]`), `PUT Settings` (`SettingsManage`), `GET Admin` (`SalesView`). UI: `Admin/Settings/Membership.vue`, `User/Membership.vue`.
- **Day pass (single-day `pass_product`): REMOVED.** `Script0118_RemoveDayPass.sql` hard-drops `pass_product`, `pass_purchase`, and `event_pass_eligibility`. `Script0098_PassProductUniqueName.sql` and `Script0053_EventDayPassEligibility.sql` target tables that no longer exist (historical migrations).

## Concepts under test

- A **season pass product** (`season_pass_product`) has a `price_cents > 0`, a `[valid_from_date, valid_to_date]` window, and a `kind`:
  - `unlimited` - any number of rides in the window.
  - `days_of_week` - only valid when the event's start day is in `valid_days_of_week` (int[], 0=Sun..6=Sat).
  - `credits` - `total_credits` rides; each reservation burns one (`credits_remaining` decremented, floored at 0).
- The window is **frozen onto the purchase** at buy time (`valid_from_date`/`valid_to_date` copied to `season_pass_purchase`); later product edits do not retro-change sold passes.
- `rider_paid_service_charge_bps` (0..10000) is the share of the tenant service charge passed to the rider; 10000 = rider pays the full charge, 0 = tenant absorbs it.
- **Perks** (`season_pass_event_type_perk`, 0..100%, 100 = included) are **configuration only**: the admin UI states discount-at-checkout ships later, and `Reserve` does **not** read perks at all, so they currently neither discount nor gate anything.
- `requires_waiver` exists on the product but **is not enforced** in `Buy` (the purchase's `waiver_signature_id` is never set).
- Each purchase gets a `redemption_token` (QR) and a required `photo_data_url` selfie (JPEG/PNG data URL, length 1KB..2MB) for gate ID.
- **Reservation capacity:** `ActiveReservationsForEvents` counts non-cancelled season-pass reservations for an event and compares to `event.capacity`; this is counted **separately** from event-ticket sales. `UNIQUE(season_pass_purchase_id, event_id)` prevents a second reservation by the same pass for the same event.
- A **membership** is a tenant-funded `none`/`yearly` access program with its own enabled flag, price, and required-for-rider/spectator gates (distinct from season passes).

## Preconditions / test data

- A tenant (Track A) with `season_passes_enabled = true`, a Stripe test key, a configured service-charge bps, and at least one scheduled future event with `capacity` set.
- A second tenant (Track B) for isolation checks, with its own season pass + a paid purchase + a reservation.
- Three season pass products on Track A: an **Unlimited** ($200), a **Days-of-week** (Sat/Sun only), and a **Credits** (e.g. 5 credits), each with a validity window that includes the test event date.
- One product with `requires_waiver = true` and one event-type perk at 100%.
- Two rider accounts on Track A (one with an emergency contact set if `RequireEmergencyContact` is on, one without) plus a valid coupon scoped `season_pass` and a gift card with a known balance.
- A staff account with `SalesRedeem` for gate-scan cases.

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SPA1 [NN] | Create each kind | Create Unlimited, Days-of-week (pick Sat/Sun), and Credits (set total credits) products | All save; list shows correct kind label, price, and validity range. Reopen confirms persistence. `valid_days_of_week` saved only for days_of_week; `total_credits` only for credits. |
| SPA2 [NN] | Kind-specific validation | Create a Credits pass with credits = 0/blank; create a Days-of-week pass with no days selected | Rejected: "Credit-based passes need total_credits > 0" and "Day-of-week passes need at least one valid day" (server-side; `CreateProduct`). |
| SPA3 [NN] | Validity window guard | Set Valid-to earlier than Valid-from | Rejected: "Valid-to date must be on or after valid-from date." Also enforced by DB CHECK `chk_season_pass_dates`. |
| SPA4 [NN] | Price floor | Set price to $0 | Rejected (DTO `Range(1, 10_000_000)` and table `CHECK price_cents > 0`). |
| SPA5 [NN] | Rider-paid service charge | Set "Rider pays % of service charge" to 50 | Saves as `rider_paid_service_charge_bps = 5000`; a later purchase charges rider half the service charge (verify in SPU3). |
| SPA6 [NN] | Perks config | Add an event-type perk at 100% and one at 50%, save, reopen | Perks persist (`ReplacePerks`). Note: perks are informational only today (no checkout discount, no reservation gate). |
| SPA7 [NN] | Edit does not retro-change sold passes | Sell a pass (SPU1), then edit the product's window/price | Existing purchase keeps its frozen `valid_from/to` and amount; only new purchases use the edited values. |
| SPA8 [NN] | Reorder | Drag-reorder the product list | `POST Products/Reorder` persists `sort_order`; public list (`Products`) and admin list reflect new order (ordered by sort_order, then name). |
| SPA9 [R] | Delete blocked when sold | Delete a product that has purchases | Rejected: "This pass has purchases on file and can't be deleted. Set inactive instead." (FK `ON DELETE RESTRICT`, PG 23503 mapped). |
| SPA10 [R] | Deactivate vs delete | Set a product inactive | Disappears from public `Products` (active-only) but stays in `Products/Admin`; existing passes unaffected. |
| SPA11 [R] | Feature toggle off | Disable season passes in Settings -> Features | Admin pages stay reachable; public list/CTA hide; `Buy`/`Reserve` reject (SPU6). |
| SPA12 [R] | Membership config | Enable membership, set name/price/duration (yearly) and required-for-riders | `PUT Membership/Settings` saves; `GET Membership/Status` reflects it; rider `Buy` available. |
| SPA13 | Admin sales visibility | After a paid season pass, open Admin -> Purchases / dashboard | Row appears as kind `season_pass` via `v_recent_sales` with product name, purchaser, amount, status. |

---

## User (buy)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SPU1 [NN] | Authed purchase happy path | As a rider, open Buy page, pick a pass, take the photo, pay with a test card | `Buy` creates a `pending` purchase with a `redemption_token`; webhook (`OnSeasonPassPaid`) flips to `paid`; pass appears in `My Season Passes` with a QR. Email sent. |
| SPU2 [NN] | Photo required | Submit without a selfie (or with an oversized/non-image data URL) | Continue button disabled client-side; server rejects with "A photo of the pass holder is required..." (`IsValidPhotoDataUrl`, 1KB..2MB JPEG/PNG). |
| SPU3 [NN] | Rider service-charge math | Buy a pass whose product has rider-paid bps = 5000, tenant service-charge bps set | Amount = base + (serviceCharge * 5000/10000). Pay screen shows Subtotal / Service charge / Total consistently; `amountCents` matches the Stripe charge. |
| SPU4 [NN] | Coupon (season_pass scope) | Apply a valid `season_pass` coupon | Discount applied pre-service-charge; redemption recorded (`CouponRedemption` source `season_pass`); total drops. Invalid/wrong-scope coupon shows the coupon error inline. |
| SPU5 [NN] | Gift card partial + full | (a) Apply a gift card covering part of the price; (b) one covering the whole price | (a) Stripe charged remainder; gift-card redemption + balance applied. (b) Free fast-path: status set `paid` immediately, no `clientSecret`, redirect to My Season Passes with "Gift card covered the pass". |
| SPU6 [NN] | Feature off rejects buy | With `season_passes_enabled = false`, POST `SeasonPass/Buy` | Rejected: "Season passes aren't sold at this track." Buy route also bounces to home client-side. |
| SPU7 [NN] | Emergency contact gate | If tenant `RequireEmergencyContact` is on, buy as a rider without one | Rejected: "Please add an emergency contact on your profile before purchasing." |
| SPU8 [R] | Guest purchase not supported | Attempt to buy a season pass while signed out | Blocked: endpoint is `[Authorize]`. Document that guest season-pass purchase does not exist (the old guest day-pass flow was removed). |
| SPU9 [R] | Inactive / cross-tenant product | Buy with a productId that is inactive, or one belonging to Track B | Rejected: "Pass is not available." (`GetProduct` is tenant-scoped; `IsActive` checked.) |
| SPU10 [R] | Membership purchase | As a rider, buy a membership (yearly) | `Membership/Buy` creates pending, webhook marks paid; `Status` shows active with `validTo = now + 365d`. Tenant-funded (no rider service-charge line). |

---

## Redemption & reservations

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SPR1 [NN] | Reserve within window | Call `SeasonPass/Reserve` (API/Postman) for a paid Unlimited pass against a scheduled event inside the validity window | Reservation created (`reserved`); returns `reservationId`, `alreadyReserved = false`. |
| SPR2 [NN] | Reserve outside window | Reserve against an event whose date is before `valid_from` or after `valid_to` | Rejected: "This pass isn't valid on the event's date." |
| SPR3 [NN] | Days-of-week gate | Reserve a Sat/Sun-only pass against a Wednesday event | Rejected: "This pass isn't valid on that day of the week." (Uses event `StartsAt.DayOfWeek` in UTC, see risks.) |
| SPR4 [NN] | Credits burn + exhaustion | Reserve a 1-credit pass for an event; reserve again for a second event | First succeeds and `credits_remaining` drops to 0; second rejected: "This pass has no credits remaining." |
| SPR5 [NN] | Capacity sell-out | Fill an event to `capacity` with season-pass reservations, then reserve once more | Rejected: "This event is sold out." (Counts non-cancelled season-pass reservations only.) |
| SPR6 [NN] | Idempotent re-reserve | Reserve the same pass for the same event twice (active reservation exists) | Second returns `alreadyReserved = true` with the existing reservationId; no duplicate, no extra credit burn. |
| SPR7 [NN] | Not-yet-paid / not-owned | Reserve with a `pending` pass; reserve with another rider's pass | Rejected: "This pass isn't active yet." / "That pass isn't yours." |
| SPR8 [NN] | Event not reservable | Reserve against a cancelled/past event | Rejected: "That event isn't available." (Requires `scheduled` and `ends_at >= now`.) |
| SPR9 [NN] | Gate lookup by QR | As `SalesRedeem` staff, `GET SeasonPass/Pass/{token}` for a paid pass | Returns holder name/email, status, validity, credits, selfie photo, product kind, and today's reservations (tenant-tz day window). |
| SPR10 [NN] | Gate check-in | `POST SeasonPass/Reservations/{id}/CheckIn` for today's reservation | Reservation flips to `checked_in` with `checked_in_at` and staff id; lookup reflects it. |
| SPR11 [R] | Refund releases reservations | Admin refunds a paid season pass that has active reservations | Purchase cancelled + marked refunded; all non-cancelled reservations set `cancelled` (frees event capacity); Stripe refund defaults to amount minus service charge. |

---

## Edge & money / isolation

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SPE1 [NN] | Re-reserve after cancel (constraint trap) | Reserve event X with a pass, cancel that reservation, then reserve event X again with the same pass | EXPECTED-BUG WATCH: a cancelled row still exists, so the `existing.Status != "cancelled"` short-circuit is skipped and `CreateReservation` hits `UNIQUE(season_pass_purchase_id, event_id)` -> 500/constraint error. Confirm and file; intended behavior is to revive/replace the cancelled reservation. |
| SPE2 [NN] | Waiver not enforced | Buy a product with `requires_waiver = true` without signing the waiver, then reserve and check in | WATCH: purchase + reservation + check-in all succeed; `waiver_signature_id` stays null. Decide whether Buy (or gate) must block until the rider waiver is signed. |
| SPE3 [NN] | Coupon over-discount | Apply a coupon whose value exceeds the pass price | Verify the coupon validator clamps discount to subtotal so `basePrice`, service charge, and `amountCents` never go negative. If not clamped, this is a money bug. |
| SPE4 [NN] | Day-of-week timezone edge | Days-of-week pass; event starts just after local midnight on a valid day but the UTC instant lands on the prior (invalid) day | WATCH: `Reserve` derives the weekday from `StartsAt` in UTC, not tenant tz, so a late-night event can be mis-judged. Confirm against tenant timezone expectations. |
| SPE5 [NN] | Reservation capacity vs ticket capacity | Sell event tickets to capacity, then reserve season passes for the same event (and vice versa) | Document intent: the two capacities are independent (`ActiveReservationsForEvents` ignores ticket sales). Confirm an event can be ticket-sold-out yet still accept pass reservations. |
| SPE6 [NN] | Concurrent reserve near capacity | Two near-simultaneous `Reserve` calls when one spot remains | Read-then-insert with no lock can let both land (over-capacity by one). Note as a concurrency watch-item. |
| SPE7 [R] | Tenant isolation: gate lookup | As Track A staff, look up a Track B pass token via `Pass/{token}` | 404 "Pass not found." (`GetPurchaseByRedemptionToken` has no tenant filter but the controller checks `pass.TenantId == tenantId`.) |
| SPE8 [R] | Tenant isolation: check-in | As Track A staff, attempt `CheckIn` on a Track B reservation id | No-op: `UpdateReservationStatus` joins through `season_pass_purchase` filtered by `tenant_id`, so the update affects 0 rows. |
| SPE9 [R] | Tenant isolation: reserve cross-tenant pass | Reserve a pass whose tenant differs from the resolved tenant | Rejected: "That pass belongs to a different track." |
| SPE10 [R] | Rider hard-delete protection | Attempt to delete a rider who holds a paid season pass | Blocked by `ON DELETE RESTRICT` on `purchaser_user_id` (`Script0101`), preserving purchase history. |
| SPE11 [R] | Day pass removed | Search nav/admin/API for any day-pass surface | Confirm absent: no `pass_product`/`pass_purchase` endpoints or screens; only season passes + membership remain. |

---

## Known risks / watch-items

- **Reserve and gate check-in are backend-only.** `SeasonPass/Reserve`, `Pass/{token}`, and `Reservations/{id}/CheckIn` (and `SeasonPassService.reserve/checkIn/lookupByToken`) are implemented but **not called by any Vue view**. `User/SeasonPasses.vue` shows the QR but offers no "reserve a date" action, and the `Redeem.vue` scanner targets the event-ticket redeem service, not season passes. Until wired, riders cannot self-reserve and staff cannot scan passes in-app (SPR1, SPR9, SPR10 are API-only). Flag scope: confirm whether UI is planned.
- **Waiver not enforced (SPE2).** `requires_waiver` is configurable and the schema has `waiver_signature_id`, but `Buy` never checks for or stores a signature. A waiver-required pass can be bought, reserved, and checked in unsigned. Compliance gap.
- **Cancelled-reservation unique-constraint trap (SPE1).** Re-reserving an event after cancelling collides with `UNIQUE(season_pass_purchase_id, event_id)`; the short-circuit only covers a still-active row.
- **Perks are inert (SPA6).** `season_pass_event_type_perk` is stored but applied nowhere: no checkout discount and, despite the admin UI copy, no reservation gating (Reserve ignores perks/event type). Either implement or correct the copy.
- **Money correctness:** coupon discount is subtracted before the service-charge calc (SPE3); verify the validator clamps so totals cannot go negative. Free fast-path (gift card covers all) sets `paid` without Stripe (SPU5b); verify ledger/email still fire correctly. Refund default withholds the service charge (SPR11).
- **Capacity model:** season-pass reservation capacity is independent of event-ticket sales (SPE5) and is a read-then-insert with no advisory lock (SPE6); both warrant a product decision.
- **Timezone:** day-of-week eligibility uses UTC weekday, not tenant timezone (SPE4).
- **Isolation note (positive):** `GetPurchase`/`GetPurchaseByRedemptionToken` lack a tenant predicate, but every caller re-checks `TenantId` (Reserve, Refund, LookupPassByToken) and `UpdateReservationStatus` enforces tenant via a join. Keep this pattern if these queries gain new callers, or push the tenant filter into the repo.
- See the **Events / Pricing / Registration** plan for event capacity and the **Waivers** plan for signature depth.
