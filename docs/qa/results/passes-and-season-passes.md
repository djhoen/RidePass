# QA Results: Passes & Season Passes

Verified by static trace against current code (no live browser). File:line citations point at the source backing each verdict.

Key recent-fix confirmations:
- Season-pass purchase now ENFORCES product `RequiresWaiver`: `SeasonPassController.Buy` blocks unless the holder has signed the tenant's active waiver and records `waiver_signature_id` on the purchase (`webapi/Controllers/SeasonPassController.cs:210-224,259`). `BuySeasonPass.vue` signs before buy (`vueapp/src/views/BuySeasonPass.vue:202-218`).
- Gate check-in is now waiver-gated: `CheckIn` runs the reservation through `IWaiverCheckInGate.BlockReason` before flipping status (`webapi/Controllers/SeasonPassController.cs:514-520`; `Services/Waivers/WaiverCheckInGate.cs:60-84`).

## Admin

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SPA1 | PASS | `CreateProduct` saves kind-specific fields only: `ValidDaysOfWeek` for days_of_week, `TotalCredits` for credits (SeasonPassController.cs:109-110); list via `ToResponse` (SeasonPassController.cs:532-552). |
| SPA2 | PASS | Credits <= 0 and empty days_of_week rejected server-side (SeasonPassController.cs:91-98). |
| SPA3 | PASS | Valid-to < valid-from rejected (SeasonPassController.cs:87-90) + DB `chk_season_pass_dates` (Script0035_SeasonPasses.sql:30). |
| SPA4 | PASS | DTO `[Range(1,10_000_000)]` (SeasonPassDtos.cs:13) + DB `CHECK price_cents > 0` (Script0035:18). |
| SPA5 | PASS | UI sends pct*100 as bps (Admin/SeasonPasses.vue:273), reads bps/100 (:253); controller stores `RiderPaidServiceChargeBps` verbatim (SeasonPassController.cs:112). |
| SPA6 | PASS | `ReplacePerks` persists (SeasonPassController.cs:117-120,147-150; SeasonPassRepository.cs:149-162). Note: perks remain inert (no checkout discount, Reserve ignores them). |
| SPA7 | PASS | Purchase freezes `ValidFromDate`/`ValidToDate`/`AmountCents` at buy (SeasonPassController.cs:255-256,249); edits touch product only. |
| SPA8 | PASS | `Products/Reorder` persists sort_order (SeasonPassController.cs:155-164); both lists ORDER BY sort_order, name (SeasonPassRepository.cs:75). |
| SPA9 | PASS | PG 23503 mapped to the exact message (SeasonPassController.cs:173-176); FK `ON DELETE RESTRICT` on product_id (Script0035:47). |
| SPA10 | PASS | Public `ListActive` filters `is_active = true` (SeasonPassRepository.cs:70); admin lists all (SeasonPassController.cs:77). |
| SPA11 | PASS | Buy rejects when off (SeasonPassController.cs:186-189); public Buy page bounces home (BuySeasonPass.vue:186-189). NOTE: `Reserve` has NO `SeasonPassesEnabled` guard, so a pass bought earlier could still reserve while the feature is off (gap vs the "Reserve reject" wording; Reserve is API-only and unwired). |
| SPA12 | PASS | `UpdateSettings` (SettingsManage) persists; `Status` reflects; rider Buy gated by `MembershipEnabled`/price (MembershipController.cs:96-100,165-179). |
| SPA13 | PASS | `v_recent_sales` has a `season_pass` UNION branch (Script0080_RecentSalesView.sql:78-90). |

## User (buy)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SPU1 | PASS | Buy creates pending + token (SeasonPassController.cs:244-263); `OnSeasonPassPaid` flips paid, writes ledger, sends email (StripePurchaseFinalizer.cs:295-323). |
| SPU2 | PASS | `IsValidPhotoDataUrl` (JPEG/PNG, 1KB..2MB) (SeasonPassController.cs:201-204,492-499); Continue disabled without photo (BuySeasonPass.vue:70). |
| SPU3 | PASS | `amountCents = basePrice + riderPortion`, riderPortion = serviceCharge*bps/10000 (SeasonPassController.cs:240-242); pay table shows Subtotal/Service/Total (BuySeasonPass.vue:84-90). |
| SPU4 | PASS | Coupon scope `season_pass`, discount subtracted pre-service-charge (SeasonPassController.cs:231-240); redemption source `season_pass` (:265-276); coupon error shown inline (BuySeasonPass.vue:238-241). |
| SPU5 | PASS | Partial: Stripe charged remainder (SeasonPassController.cs:297,341-349); full: free fast-path sets paid, empty clientSecret, amount 0 (:300-312), UI redirects with "Gift card covered the pass" (BuySeasonPass.vue:227-231). NOTE: free fast-path bypasses `OnSeasonPassPaid`, so NO ledger sale row and NO purchase email fire on a gift-card-covered pass (no PI to trigger the webhook). |
| SPU6 | PASS | `Season passes aren't sold at this track.` (SeasonPassController.cs:186-189). |
| SPU7 | PASS | Emergency-contact gate (SeasonPassController.cs:193-196). |
| SPU8 | PASS | `Buy` is `[Authorize]` (SeasonPassController.cs:181). |
| SPU9 | PASS | `GetProduct` tenant-scoped (SeasonPassRepository.cs:81) + `!product.IsActive` => "Pass is not available." (SeasonPassController.cs:198-199). |
| SPU10 | PASS | `Membership/Buy` pending (MembershipController.cs:88-162); webhook flips paid (StripePurchaseFinalizer.cs:147-154); yearly `valid_to = now+365` (MembershipController.cs:107); `RiderServiceChargeCents = 0` (:160). |

## Redemption & reservations (API-only; not wired into any Vue view)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SPR1 | PASS | Reserve creates `reserved`, returns `alreadyReserved=false` (SeasonPassController.cs:436-443). API/Postman only (no UI). |
| SPR2 | PASS | Date-window check (SeasonPassController.cs:399-402). |
| SPR3 | PASS | Days-of-week gate via `ev.StartsAt.DayOfWeek` (SeasonPassController.cs:406-413). |
| SPR4 | PASS | Credit check + `DecrementCredits` floored at 0 (SeasonPassController.cs:414-417,442; SeasonPassRepository.cs:254-262). |
| SPR5 | PASS | Capacity vs `ActiveReservationsForEvents` (SeasonPassController.cs:420-428; SeasonPassRepository.cs:366-377). |
| SPR6 | PASS | Active existing reservation short-circuits to `alreadyReserved=true` (SeasonPassController.cs:430-434). |
| SPR7 | PASS | Ownership + `status != paid` checks (SeasonPassController.cs:382-393). |
| SPR8 | PASS | Requires `scheduled` and `ends_at >= now` (SeasonPassController.cs:394-398). |
| SPR9 | PASS | `LookupPassByToken` returns holder/photo/credits/today's reservations (SeasonPassController.cs:447-490). API only. |
| SPR10 | PASS | `CheckIn` flips `checked_in` + staff id (SeasonPassController.cs:522; SeasonPassRepository.cs:335-343); now additionally waiver-gated (SeasonPassController.cs:514-520). API only. |
| SPR11 | PASS | Refund cancels purchase, cancels non-cancelled reservations, marks refunded; default refund = amount - serviceCharge (PurchaseController.cs:1242,1292-1300). |

## Edge & money / isolation

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SPE1 | PASS (bug confirmed) | Cancelled row is not short-circuited (`existing.Status != "cancelled"`, SeasonPassController.cs:431) so re-reserve hits `UNIQUE(season_pass_purchase_id, event_id)` (Script0035:83) => constraint error. Trap exists as documented. |
| SPE2 | FAIL | Documented expected (unsigned purchase + reserve + check-in all succeed, `waiver_signature_id` null) NO LONGER holds: Buy now blocks unsigned waiver-required passes and records the signature (SeasonPassController.cs:210-224,259) and CheckIn is waiver-gated (:514-520). The compliance gap is closed; the test text is now stale. |
| SPE3 | PASS | Validator clamps `if (discount > subtotalCents) discount = subtotalCents` (Services/Coupons/CouponValidator.cs:65); basePrice/serviceCharge/amount stay >= 0. |
| SPE4 | PASS (watch confirmed) | Weekday derived from `ev.StartsAt.DayOfWeek` in UTC, not tenant tz (SeasonPassController.cs:408). Late-night-local events can be mis-judged exactly as flagged. |
| SPE5 | PASS | `ActiveReservationsForEvents` counts only `season_pass_reservation`, ignores ticket sales (SeasonPassRepository.cs:366-377); capacities independent. |
| SPE6 | PASS (watch confirmed) | Read-then-insert with no lock (SeasonPassController.cs:420-441); over-capacity-by-one race is possible. Not observable via static trace. |
| SPE7 | PASS | `pass.TenantId != tenantId` => 404 "Pass not found." (SeasonPassController.cs:453-455). |
| SPE8 | PASS | `UpdateReservationStatus` joins through `season_pass_purchase` filtered by tenant_id => 0 rows cross-tenant (SeasonPassRepository.cs:328-363). |
| SPE9 | PASS | Cross-tenant pass rejected: "That pass belongs to a different track." (SeasonPassController.cs:386-389). |
| SPE10 | PASS | `purchaser_user_id` switched to `ON DELETE RESTRICT` (Script0101_*.sql:21-23). |
| SPE11 | PASS | `Script0118_RemoveDayPass.sql` present; no `pass_product`/`pass_purchase` endpoints remain (grep hits are `season_pass` references only). |

Counts: PASS 41, FAIL 1 (SPE2), NEEDS-LIVE 0, N/A 0.
</content>
