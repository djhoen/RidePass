# Section 6: Counter (POS) sale flow, multi-line PI cart, cancellation & refund admin paths

## Scope

Read end-to-end:

- `webapi/Controllers/CounterController.cs` — `Riders/Find`, `Riders` (create), `Sale`. The mixed-cart
  flow that produces a single PaymentIntent over passes + event tickets + extras + memberships.
- `webapi/Controllers/API/Data/Counter/*.cs` — `CounterCartItem`, `CounterSaleRequest`,
  `CounterSaleResponse` + `CounterSaleLineItem`, `RiderLookupRequest/Response`,
  `CreateCounterRiderRequest/Response`.
- `webapi/Controllers/PurchaseController.cs` — `CancelPass`, `CancelTicket`, `ListForAdmin`,
  `ListDisputes`.
- `webapi/Controllers/MeController.cs` — `CancelMyPass`, `CancelMyTicket`, the
  `tenant.AllowSelfCancel` branch, `EmitCancelRequest`, the rider-share-coupon flow (touched only for
  audit-coverage comparison).
- `webapi/Controllers/SuperAdminController.cs` — `ListRefundQueue`, `ProcessPassRefund`,
  `ProcessTicketRefund`, `WriteRefundLedgerEntry`, dispute list / fetch endpoints.
- `webapi/Controllers/PaymentController.cs` — re-verified the webhook fan-out against everything a
  counter sale can produce; dispute handler coverage.
- `webapi/Helpers/HttpContextAuditLogger.cs` — actor / impersonation capture for cancel + refund actions.
- `Services/Repositories/PassPurchaseRepository.cs`,
  `EventTicketPurchaseRepository.cs`, `EventExtraRepository.cs`, `MembershipRepository.cs` —
  `Cancel`, `MarkRefunded`, `UpdateStatus`, `HasActiveRaceEntry`, `SoldByUserId` writes.
- `Services/Helpers/RefundCalculator.cs`.
- `vueapp/src/views/Admin/Counter.vue`, `Purchases.vue`, `vueapp/src/services/CounterService.ts`.

Sections 1 (auth/tenancy), 2 (payments/webhook/ledger), 3 (schema), 4 (day-pass + event-ticket
online), and 5 (extras / season-pass / membership / gift-card / rental online) findings are not
repeated. Specifically not repeated here: webhook handler dispute coverage gaps (Section 2 #12),
`UpdateStatus` not row-count-guarded (Section 2 #11), extras source-kind CHECK gap (Section 2 #5),
no rental ledger row (Section 2 #1), no refund UI for membership/season-pass/rental/gift-card/extras
(Section 2 open question 5), missing Cancel endpoints for those same five kinds (Section 5.11),
abandoned-cart janitor absent (Section 4 #2), `impersonated_by` claim not flowed into audit log
(Section 1, deferred to a follow-up), the bundled-membership free-cart strand-in-pending
(Section 5.4) — the same row creation path is reused by the counter cart but counter sales don't
expose `addMembership` bundling, only a membership *line item*.

## Architecture summary

**Single PI per cart, four line kinds, server-driven inventory + waiver + voucher.**
`CounterController.CreateSale` builds a unified cart of `pass | event_ticket | extras | membership`,
validates each kind, computes a single total, optionally signs the waiver on the rider's behalf
(with parent-guardian fields for minors), and either (a) creates one Stripe PI for the whole cart
and stamps `stripe_payment_intent_id` on every row, or (b) short-circuits for `paymentMethod=cash`
or for a 100%-off voucher.

| Kind                | Created in counter | Counter waiver gated? | Membership-gated by counter? | Cash sale ledger? | Cancellable post-paid? |
|---------------------|--------------------|------------------------|-------------------------------|-------------------|-------------------------|
| pass                | Yes                | Yes (product.RequiresWaiver) | **No** | Yes (`source_kind='pass'`) | Yes (admin + rider self) |
| event_ticket        | Yes (backend only — FE has no UI) | Yes (event.RequiresRiderWaiver) | **No** | Yes | Yes (admin + rider self) |
| extras              | Yes                | Yes (product.RequiresWaiver) | **No** | **No** (CHECK gap) | **No** — no Cancel endpoint |
| membership          | Yes                | n/a                    | n/a                            | Yes (`source_kind='membership'`) | **No** — no Cancel endpoint |
| (gift card, rental, season pass — not sellable at counter today) | — | — | — | — | — |

Counter's Stripe-success path goes through the same `PaymentController.StripeWebhook` fan-out as
online sales — `ListByStripePaymentIntentId` is queried for every kind, and the per-kind branch
runs in turn. The fan-out *does* cover every kind the counter sells today (passes, tickets, extras,
membership all matched), so a mixed-cart counter PI doesn't have a stranded row class.

**Cash sale path.** `CounterController` writes ledger rows inline at sale time (skipping Stripe),
flips status to `paid` on each line, and posts a per-line entry with
`payment_method='cash'`, `net_to_tenant=-service_charge`, `ridepass_cut=service_charge`. Extras
rows have status flipped but no ledger entry (CHECK constraint blocks `source_kind='extras'`).
Memberships ride along with `source_kind='membership'`. Idempotency handled by the unique partial
index on `(tenant_id, source_kind, source_id) WHERE entry_kind='sale'`.

**Cancel paths today (per kind, per actor).**

| Actor / kind        | pass | event_ticket | extras | membership | season_pass | rental | gift_card |
|---------------------|------|--------------|--------|------------|-------------|--------|-----------|
| Admin (SalesCancel) | `PurchaseController.CancelPass` | `PurchaseController.CancelTicket` | **none** | **none** | **none** | **none** | **none** |
| Rider self          | `MeController.CancelMyPass` | `MeController.CancelMyTicket` | **none** | **none** | **none** | **none** | **none** |
| Super admin refund  | `SuperAdminController.ProcessPassRefund` | `ProcessTicketRefund` | **none** | **none** | **none** | **none** | **none** |

So a cart sold at the counter as `{pass, ticket, extras, membership}` for one PI only has Cancel
buttons for the pass and ticket lines. The extras + membership rows can't be cancelled, refunded,
or even surfaced in the super-admin refund queue. Combined with Section 5.11 (no cancel endpoints
for the other five kinds at all), the counter is selling more shapes than the rest of the system
can remediate.

**Rider self-cancel state machine.** Two-mode driven by `tenant.AllowSelfCancel`:
- `false`: no state change. `EmitCancelRequest` notifies tenant admins via in-app notification +
  audit row `rider.cancel_request`, returns `{status: "request_submitted"}`.
- `true`: inline cancel + Stripe partial refund using `RefundCalculator.RefundableCents`
  (rider service-charge portion is withheld; tenant cut stays earned). `MarkRefunded` writes the
  refund note + flips status to `refunded`. Audit row `rider.self_cancel`. Waitlist promotion is
  fire-and-forget. Errors on Stripe refund leave row `cancelled` (not `refunded`) and don't roll
  back — by design, but means a manual retry path is needed.

**Admin cancel state machine.** `PurchaseController.CancelPass` / `CancelTicket` do NOT call
Stripe — they only flip the row to `cancelled` and put it in the super-admin refund queue. The
super admin runs `ProcessPassRefund` / `ProcessTicketRefund` which calls Stripe and then writes
the `entry_kind='refund'` ledger row. So the timeline for a typical admin cancel is:

```
paid → admin clicks Cancel → row.status='cancelled' (no money moved, no email to rider)
     → super admin opens queue → ProcessRefund → Stripe partial refund + ledger refund row + row.status='refunded'
```

This is documented above the Cancel button in the FE dialog ("queued for super-admin"), so the UX
matches the back-end semantics. But there is **no rider notification when the admin cancels** —
the rider only finds out via Stripe's refund-arrival email days later (and only if the super admin
gets to it).

## Findings

| #    | Severity     | Title | File / location |
|------|--------------|-------|-----------------|
| 6.1  | **Critical** | Counter sales skip the membership gate entirely — a track with `MembershipRequiredForRiders=true` can have a cashier sell a day pass or race-entry ticket to a rider without an active membership, while the online flow rejects the same purchase | `webapi/Controllers/CounterController.cs:215-218, 234-295` |
| 6.2  | **Critical** | Counter sales support neither coupons nor gift cards — riders with a Stripe-funded gift card balance can't redeem it at the counter, and admins have no way to apply a discount coupon in person. The online flow has both | `webapi/Controllers/CounterController.cs` (no `CouponCode` / `GiftCardCode` handling); `vueapp/src/views/Admin/Counter.vue:200-207` (UI only exposes rewardVoucher) |
| 6.3  | **Critical** | Admin Cancel writes nothing to `audit_log`. `PurchaseController.CancelPass` and `CancelTicket` flip status + queue waitlist promote, but never call `_audit.Log`. The only paper trail is `cancelled_by_user_id` on the row itself — no metadata, no reason searchable, no IP, no super-admin impersonation context | `webapi/Controllers/PurchaseController.cs:1231-1287` (no `IAuditLogger` injected at all) |
| 6.4  | High         | `MeController.CancelMyPass` self-cancel commits the DB cancel BEFORE attempting the Stripe refund; if Stripe throws, the row is `cancelled` but `refund_note` is null and `status != 'refunded'`. The audit row records `refundId=null`. Worse: there is no retry endpoint anywhere — the super-admin refund queue only sees `cancelled` rows from admins, not from rider self-cancels, so a Stripe-failed self-cancel is invisible until a human notices | `webapi/Controllers/MeController.cs:239-254, 291-305` |
| 6.5  | High         | No rider notification when an admin cancels their purchase. The rider's calendar still shows the event, no email is sent saying "your purchase was cancelled — your refund is being processed." The only signal is the eventual Stripe refund-arrival email (often days later) | `webapi/Controllers/PurchaseController.cs:1231-1287`; `Services/Notifications/INotificationService.cs` (no `EmitToUser` call on admin cancel) |
| 6.6  | High         | Counter UI `Counter.vue` doesn't expose event-ticket sale at all — the FE cart type is `'pass' | 'extras' | 'membership'` and there's no UI affordance to add an `event_ticket` line. The backend `CounterController.CreateSale` fully supports `kind='event_ticket'` (including race-entry one-per-rider check + tier inventory + voucher application), so the gap is FE-only — cashiers can't sell race entries or general-admission tickets at the counter today | `vueapp/src/views/Admin/Counter.vue:432-443` (`CartKind` union) and 81-196 (catalog tabs); `webapi/Controllers/CounterController.cs:247-295` (backend support) |
| 6.7  | High         | Cancel does not restore a consumed reward voucher. A rider's $50-off voucher is `MarkRedemptionUsed`'d at sale time; when the admin cancels and the super-admin refunds, `reward_redemption.redeemed_at` is never cleared. The rider loses the voucher even though the purchase reversed | `webapi/Controllers/PurchaseController.cs:1232-1287` (no `_rewards.*` call); `webapi/Controllers/SuperAdminController.cs:376-465` (no restore); `Services/Repositories/RewardRepository.cs:153-160` (no `UnmarkRedemption` exists) |
| 6.8  | High         | Cancel does not credit-back applied gift card balance. A rider who paid $30 of a $100 cart with a gift card has the gift card balance permanently debited; when the admin cancels and super-admin refunds, the Stripe refund only covers the Stripe charge portion ($70), not the gift card portion — and `gift_card.balance_cents` is never restored | `webapi/Controllers/SuperAdminController.cs:376-465` (no `_giftCards.ApplyToBalance(+amt)`); `Services/Repositories/GiftCardRepository.cs` (no `RestoreBalance` method) |
| 6.9  | High         | Counter has no Cancel/Refund path for the membership and extras lines it sells. A cashier ringing up `{pass + membership + 2× t-shirt}` produces a row in each kind's table; cancelling the pass leaves the membership active and the extras paid forever. The super-admin refund queue only knows about pass + ticket cancellations, so even doing it in two steps (cancel pass + manually refund membership) requires SQL | `webapi/Controllers/PurchaseController.cs:1231-1287` (only pass/ticket); `webapi/Controllers/SuperAdminController.cs:326-465` (refund queue only pass/ticket) |
| 6.10 | High         | Race-entry one-per-rider check in CounterController is read-then-write (lines 281-287); two cashiers can both pass `HasActiveRaceEntry` for the same rider+tier and both insert. Same pattern as the online `BuyEventTicket` (`PurchaseController.cs:680-687`), but the counter compounds it because two simultaneous walk-ups at two stations are the realistic concurrency model for a busy race-day check-in. The fix is the same — either move the check to a unique partial index on `(tenant_id, tier_id, purchaser_user_id) WHERE status IN ('pending','paid','redeemed') AND tier.kind='race_entry'` or wrap in a serializable transaction | `webapi/Controllers/CounterController.cs:269-287`; `Services/Repositories/EventTicketPurchaseRepository.cs:148-174` |
| 6.11 | High         | `MarkRedemptionUsed` is tenant-agnostic — no `WHERE tenant_id = @tenantId` predicate (and no tenant_id on the redemption row to filter by, since `reward_redemption` joins through `reward_program` for tenant scope). A voucher id passed in by the counter is implicitly trusted; combined with the lookup `GetRedemption(redemptionId)` also being tenant-agnostic (line 393), a counter operator at tenant A could in theory redeem a voucher belonging to tenant B (if they obtain the id). Today the FE only displays the operator's own tenant's vouchers, but the back-end has no defense | `Services/Repositories/RewardRepository.cs:153-160` and `GetRedemption` ~line 130s; `webapi/Controllers/CounterController.cs:393-405` |
| 6.12 | Medium       | `CounterController.CreateRider` accepts any past birthdate from year 1900 onwards with no minimum-age check (`UserController.IsValidBirthdate` only enforces `b.Date < today && b.Year >= 1900 && age <= 130`). A cashier can create a profile for a 5-year-old then immediately sell them a race-entry. The waiver minor-flow (parent name + phone + parent signature) does run, but there's no policy floor saying "tenants can configure 'minimum rider age = 8'" | `webapi/Controllers/UserController.cs:574-578`; `webapi/Controllers/CounterController.cs:152-155` |
| 6.13 | Medium       | Counter sale `payment_method='cash'` writes ledger rows BEFORE the cashier actually has the money in hand. The cashier could click "Confirm $$ cash received" by mistake then realise the rider walked away; the rows are flipped to `paid`, ledger is written, and there's no Void / Reverse button. The only remediation is the regular Cancel flow which leaves rows `cancelled` not `voided` and adds a noise refund row to the ledger — except the cash sale has no Stripe charge to refund, so the cancel cascades into "super-admin queue" with no actionable next step | `webapi/Controllers/CounterController.cs:660-704`; `SuperAdminController.cs:386-389` (rejects cancel-with-no-PI) |
| 6.14 | Medium       | Cash sale path mints a ledger row with `NetToTenantCents = -serviceCharge` (tenant owes the platform the service charge). If a cancel-then-refund flow ever lands for cash sales, the refund ledger row must also be negative-of-negative (i.e. `+serviceCharge` netToTenant) to back it out cleanly. Today no path exists, so the gap is dormant, but the asymmetry between cash and Stripe ledger arithmetic is worth documenting before the first cash refund | `webapi/Controllers/CounterController.cs:670-685`; `webapi/Controllers/SuperAdminController.cs:471-490` (refund ledger always assumes Stripe sale shape) |
| 6.15 | Medium       | `Counter.vue`'s step lockout (`stepperItems.editable`) prevents jumping back once `clientSecret` is set, but if the cashier closes the page mid-Stripe-payment-element, the PaymentIntent stays open and the rows stay `pending` until the abandoned-cart janitor that doesn't exist (Section 4 #2) cleans them up. Inventory and race-entry slots remain held by the `pending` row indefinitely | `vueapp/src/views/Admin/Counter.vue:458-468`, no FE retry / cleanup; `webapi/Controllers/CounterController.cs` (no janitor) |
| 6.16 | Medium       | The lookup endpoint `Riders/Find` checks "global user by email, then tenant-scoped user by email" and returns 404 only if both are null. Probing for the existence of any rider account at any tenant is a feature of this endpoint by design (the cashier is authorised SalesCounter), but it doesn't rate-limit; a compromised SalesCounter credential can enumerate a tenant's customer base at high throughput | `webapi/Controllers/CounterController.cs:78-94`; no rate-limit middleware applied to `/api/Counter/Riders/Find` |
| 6.17 | Medium       | The voucher application logic mutates `ticketItems` in place by splitting the discounted unit into its own quantity=1 entry at index 0 (lines 439-457). Logic looks correct but is hard to follow under multi-tier carts — and the `voucherTicketIdx = 0` reassignment at line 456 is load-bearing for the per-unit emit at line 543. A future refactor that changes loop order would re-introduce the wrong-line discount. Extract into a helper that returns the discounted line + the rest, instead of in-place mutation | `webapi/Controllers/CounterController.cs:439-457` |
| 6.18 | Medium       | `CounterCartItem.EventId` is declared in the DTO (`Guid? EventId`) and forwarded by `CounterService.ts:items[].eventId=null`, but `CounterController.CreateSale`'s extras branch reads `item.EventId` and stores it on the row even though the FE always sends null. Two consequences: (a) the row's `event_id` is null for counter-sold extras (intentional — counter sells "as merch"), and (b) the legacy single-SKU per-event inventory cap (`event_extra_eligibility.inventory`) is skipped because the lookup `_extras.GetEligibility(eventId, productId)` never runs in the counter flow — so a per-event inventory cap is silently ignored at the counter while the online flow enforces it. Either document "per-event inventory caps don't apply at the counter" or wire eligibility | `webapi/Controllers/CounterController.cs:296-355` (extras branch skips eligibility check) vs `PurchaseController.cs:312-338` |
| 6.19 | Medium       | The race-entry one-per-rider check in CounterController uses `rider.Email` (line 282) which is the *target rider's* email looked up from `GetById`. But for counter sales where a cashier creates a brand-new rider then sells them a race-entry in the same session, the email is whatever the cashier typed — case-preserved. `HasActiveRaceEntry` lowercases for comparison (good), but if the rider had a previously-cancelled race entry under a different email casing (e.g. via the online flow), the duplicate check still finds it. Reads correctly today but worth a regression test if email normalisation ever changes | `Services/Repositories/EventTicketPurchaseRepository.cs:148-174`; `CounterController.cs:281-287` |
| 6.20 | Medium       | `EventExtraPurchase` rows sold at the counter for `cash` get `Status='paid'` (line 691) and `payment_method='cash'`, but Section 2 #5's CHECK gap means no ledger row ever gets written for them — even on the cash path that writes ledger rows for every other kind. The dashboard's cash-sales-by-day widget under-reports the cashier's till by the value of cash extras. Confirmation in `CounterController.cs:687-692`: `// Extras don't go through ledgerLines (no source_kind='extras' in the tenant_ledger CHECK constraint)` | `webapi/Controllers/CounterController.cs:687-692`; `RidePass.Migrator/Scripts/Script0058_Memberships.sql:87-89` (CHECK still missing 'extras' and 'gift_card') |
| 6.21 | Low          | `CounterSaleLineItem.Kind` comment says `"pass" or "event_ticket"` (line 12 of `CounterSaleResponse.cs`) but the response actually emits `pass | event_ticket | extras | membership`. Stale doc comment — keep it in sync with reality or it'll bite a new dev | `webapi/Controllers/API/Data/Counter/CounterSaleResponse.cs:12` |
| 6.22 | Low          | `MeController.CancelMyPass`'s "Stripe refund failed" branch logs a warning but does NOT EmitToTenantAdmins — so admins don't know about a failed self-cancel refund. The rider sees `{status:"cancelled", refundCents, refundId:null}` and assumes the refund is in progress, while admins have no visibility | `webapi/Controllers/MeController.cs:248-254, 300-304` |
| 6.23 | Low          | `Counter.vue` payment-error UX (`paymentError.value` at line 829) surfaces the Stripe `error.message` directly; many Stripe error messages are technical ("Your card was declined: insufficient_funds. payment_intent_authentication_failure"). Wrap with a friendlier "Payment was declined — try another card or pay cash" for the typical decline cases. Today's behaviour confuses cashiers and slows the queue | `vueapp/src/views/Admin/Counter.vue:823-838` |
| 6.24 | Low          | `Counter.vue`'s receipt step (step 5) shows QR codes for each line but has no print button or "Email receipt" option — the cashier has to take a photo of the screen or rely on Stripe's receipt email (which goes to the rider but says nothing about which line item is which QR). Add a print-friendly route + an "Email receipt to rider" button | `vueapp/src/views/Admin/Counter.vue:374-407` |
| 6.25 | Low          | Cancel reason is captured as free-text only; there's no enum (`rider_request | duplicate | track_closure | weather | other`) for the admin to pick from. Cancellation reporting on the dashboard groups by raw string, so misspellings or per-cashier wording variations splinter the buckets | `webapi/Controllers/API/Data/Purchase/CancelPurchaseRequest.cs`; `vueapp/src/views/Admin/Purchases.vue:107-108` |
| 6.26 | Low          | `Purchases.vue:canCancel(p)` returns true only for `pass` and `event_ticket`. The other kinds (`event_extra`, `membership`, `season_pass`, `gift_card`, `rental`) read from `v_recent_sales` and display in the list, but the inline Cancel button is hidden because the back-end has no endpoint. The comment at line 240-243 acknowledges this, but a more user-visible affordance ("Cancel via SQL only — request from RidePass support") would help admins triage refund-requests instead of silently ignoring the row | `vueapp/src/views/Admin/Purchases.vue:240-247` |
| 6.27 | Low          | Webhook dispute handler `HandleDispute` only fetches `passes` + `tickets` (line 596-597); a counter PI for a `membership + extras` cart whose card-holder later disputes the charge produces a `dispute_opened` notification with `tenant_id` pulled from "pass-or-ticket-first", which is null → the function early-returns at line 603 with "no matching purchase" and never writes the dispute row. The dispute exists at Stripe but never surfaces in the tenant's admin Disputes list. Section 2 #12 covers the broader gap; calling it out here as the counter sale that triggers it | `webapi/Controllers/PaymentController.cs:587-608` |
| 6.28 | Low          | `RiderLookupRequest` accepts only an email; the FE has no "search by phone" or "search by name" mode. For a walk-in rider who's forgotten their email but knows their phone (common — riders sign up once and remember the phone), the cashier is stuck. Add a `RiderSearchRequest` that takes any of `{email, phone, lastName+birthdate}` | `webapi/Controllers/API/Data/Counter/RiderLookupRequest.cs`; `vueapp/src/views/Admin/Counter.vue:14-26` |
| 6.29 | Low          | `CounterController.CreateRider` doesn't sanitise/normalise email casing on insert; the rider record uses whatever the cashier typed (`request.Email.Trim()`). Later lookups via `GetGlobalByEmail` are case-insensitive at the SQL layer (assumed, but worth verifying), but downstream comparisons in `HasActiveRaceEntry` use `LOWER()` — so the data is fine. Recommend lowering email at insert time anyway for hygiene; matches what the online registration flow does | `webapi/Controllers/CounterController.cs:143, 171` |
| 6.30 | Low          | `CounterSaleResponse.ClientSecret` is set to `string.Empty` for the cash and voucher fast paths — a sentinel value the FE checks against (`!clientSecret.value`). A `null` would be more idiomatic in C#, and the DTO declares it `null!` already; the empty-string discipline only exists because the JSON-serialised payload's downstream consumer (TS) does `!data.clientSecret`, which is true for both empty and null. Cosmetic — but a refactor to make the type `string?` would surface the intent | `webapi/Controllers/CounterController.cs:698-703, 741-746` |

## Critical findings expanded

### 6.1 — Critical — Counter sales skip the membership gate

`CounterController.CreateSale` validates pass / ticket / extras / membership inputs against
tenant config (`ExtrasEnabled`, `MembershipEnabled`, `MembershipPriceCents`) and per-product
inventory, but **never consults `tenant.MembershipRequiredForRiders` or
`MembershipRequiredForSpectators`**. The online flows do — `PurchaseController.BuyPass`
(line 220), `BuyEventTicket` (line 723), `BuyPass`'s extras branch (line 308), and analogous
calls in `SeasonPassController.Buy` and `ExtraController.Buy`.

So a track configured "you must hold a $200/yr membership to ride here" enforces that against
the website + the rider's app — but a cashier can ring up a day pass or a race-entry to a
non-member at the counter. The check is missing whether the rider has a current
`membership_purchase.status='paid'` row.

Real-world impact: a friendly cashier (or an angry one) can bypass the entire revenue floor of
the membership program. Track managers run this gate as the gatekeeper of who's allowed to ride;
the counter is exactly where the gate matters most (race day, walk-in spectators), and it has
no check.

Fix: lift `CheckMembershipGate` out of `PurchaseController` (it's already designed to be reused),
call it from `CounterController.CreateSale` for any `pass` or `event_ticket` (race-entry tier) or
extras line where the relevant gate is on, EXCEPT when the cart includes a `membership` line
(rider is buying the membership now, same logic as `bundleMembership` in the online flow). The
`membershipItem` variable is already tracked, so the bundle-bypass is trivial.

### 6.2 — Critical — Counter has no coupon or gift-card support

`CounterController.CreateSale` accepts `RewardRedemptionId` and applies a voucher (single-line
discount), but the request DTO has no `CouponCode` field and no `GiftCardCode`. Online flows
accept both (`PurchaseController.CreatePurchaseRequest`, `CreateTicketPurchaseRequest`).

So:
- A rider with a $50 Stripe-paid gift card can't redeem it at the gate. The cashier has to take
  the card code, look up the balance via SQL, manually deduct the price from the next purchase
  in a paper ledger, and remember to debit `gift_card.balance_cents` later. Practically: track
  staff turn riders away, or fudge the cash drawer.
- A track-wide "20% off Memorial Day" coupon (a real, configured `coupon` row) is invisible to
  the counter. Riders walking up for the promo get full price, then call the office on Tuesday.

Real-world impact: combined with Finding 6.20 (cash extras don't write ledger), 6.1 (no
membership gate), and the missing cancel paths (6.9), the counter is materially behind the
online flow on three of the most-used feature surfaces.

Fix: extend `CounterSaleRequest` with `CouponCode` + `GiftCardCode`, wire the same
`_couponValidator.ValidateAsync` + `_giftCardValidator.ResolveAsync` calls used in the online
flow, distribute coupon discount pro-rata across cart lines (the online `BuyEventTicket` already
has this logic — line 851-878 — extract to a shared helper). Gift-card application reduces the
final PI charge (or makes the cart free-cart eligible) and writes `gift_card_redemption` rows
per affected line.

### 6.3 — Critical — Admin Cancel writes nothing to audit_log

`PurchaseController.CancelPass` (line 1232-1258) and `CancelTicket` (line 1260-1287) do not
inject or call `IAuditLogger`. The only trace of the action is the `cancelled_by_user_id`,
`cancelled_at`, and `cancellation_reason` columns on the purchase row itself.

`MeController.CancelMyPass` / `CancelMyTicket` DO log to audit_log (line 255-256, 306-307).
`SuperAdminController.ProcessPassRefund` / `ProcessTicketRefund` DO log (line 409-410, 453-454).
So the entire write-audit pattern exists; the admin Cancel just doesn't follow it.

Real-world impact: a tenant admin can quietly cancel any paid purchase across their tenant with
no searchable trace beyond a column on the row. There's no answer to "show me every cancel that
operator X has done this month" or "what reason did the operator give for cancelling this
$2,000 race entry?" beyond a hand-rolled join across the seven purchase tables.

Combined with Section 1's `impersonated_by` claim NOT being flowed into audit_log (the global
known gap), a super-admin impersonating a tenant admin to cancel a refund-disputed purchase
leaves no impersonation trail at all — the cancel is invisible AND the impersonation isn't
recorded.

Fix: inject `IAuditLogger` into `PurchaseController`, call `_audit.Log("admin.cancel_pass", ...)`
and `"admin.cancel_ticket"` in the cancel handlers with the reason + amount + waitlist
promote outcome in metadata. Mirror the pattern in MeController. Address the `impersonated_by`
gap from Section 1 in `HttpContextAuditLogger` at the same time.

## High findings worth pulling forward

### 6.4 — Self-cancel commits DB before Stripe refund

`MeController.CancelMyPass` order of operations:
1. `await _passes.Cancel(id, ...)` — flips row to `cancelled` immediately.
2. `try { var refund = await _payments.RefundAsync(...); ... MarkRefunded(... ) } catch { log warning }`.

If Stripe throws (network blip, rate limit, etc.), the row stays `cancelled`, `refund_note` stays
null, no audit-trail `refundId` is recorded, the rider sees `{refundId:null}` in the response and
naturally assumes the refund worked. There is no automatic retry, no admin notification, no
super-admin queue entry. The next time anyone notices is when the rider emails support saying
"I cancelled three weeks ago and never got my refund."

Fix: either (a) record the cancel into a "needs refund" queue (mirror the admin cancel pattern —
just leave it cancelled and let super-admin pick it up) when Stripe fails, OR (b) wrap in a
proper saga that doesn't commit the cancel until the refund succeeds. The admin-cancel-then-
super-admin-refund flow already has the right shape; consider unifying the two paths so the
super-admin queue catches every cancel regardless of who initiated it.

### 6.7 — Cancel does not restore reward voucher

`reward_redemption.redeemed_at` gets stamped at sale time (online: in `OnPaymentSucceeded`'s
`_rewards.MarkRedemptionUsed` call line 487-490; counter: at the cash fast-path line 696 or
free-cart fast-path line 739). When the purchase is later cancelled, the redemption row is never
touched.

Consequence:
- A rider's voucher has `redeemed_at` set to the moment of purchase. The voucher is permanently
  consumed.
- When the admin cancels + super-admin refunds, the rider gets their money back BUT not their
  voucher back. They walk away $0 net of money but down a voucher that they could have used on a
  different purchase.

Fix: add `_rewards.UnmarkRedemption(redemptionId)` (UPDATE redeemed_at=NULL, redeemed_on_kind=NULL,
redeemed_on_id=NULL) and call it from both admin Cancel and super-admin refund flows. Idempotent
on already-null. Also call from `MeController` self-cancel branches. Audit log the restoration.

### 6.8 — Cancel does not credit-back applied gift card

Same shape as 6.7 but for gift cards. `GiftCardRepository.ApplyToBalance(cardId, amount)` debits
the balance at purchase time. The refund flow (super-admin or self) only refunds the Stripe
portion. The applied gift-card portion is permanently lost — the rider's $30 of pre-funded balance
evaporated.

Fix: add `_giftCards.RestoreBalance(cardId, amount)` and call from the cancel/refund paths.
Per-line tracking is already there (`gift_card_redemption` rows are per-source-id), so the
restore amount is deterministic. Audit log the restoration with the original redemption row id.

### 6.9 — No counter-level cancel/refund for extras + membership

Already summarised in the architecture matrix. A counter cart of `{pass + membership + 2 t-shirts}`
produces 4 rows in 3 tables (1 pass, 1 membership, 2 extras). Admin cancel from `Purchases.vue`
only fires `cancelPass` — the membership and extras stay `paid` forever, with no UI affordance
to fix them. Super-admin can't see them in the refund queue either (queue only lists `pass` and
`event_ticket` `cancelled` rows).

Fix: extend the cancel + super-admin refund infrastructure to all five missing kinds (the schema
already has the columns — `cancelled_at`, `cancelled_by_user_id`, `cancellation_reason`,
`refund_note`). Section 5.11 already flagged the broader gap; this finding is the counter-sale
amplifier — every multi-line counter PI today produces some rows that can't ever be reversed.

### 6.10 — Race-entry one-per-rider check is racy at the counter

`HasActiveRaceEntry` is a read; the subsequent `INSERT` is a separate statement. On race day,
two cashiers can both call `Riders/Find` for the same rider, both add the same race-entry tier
to the cart, both call `Sale`, both see "no active entry" at line 282, both insert. Result: rider
gets two race-entry rows for the same class. Refund logic later has to figure out which one to
keep.

The online `BuyEventTicket` has the same race, but in practice the rider only buys once. The
counter's busy-race-day-two-stations scenario is much more concurrency-prone.

Fix: a partial unique index on `(tenant_id, tier_id, purchaser_user_id) WHERE status IN
('pending','paid','redeemed') AND tier_kind='race_entry'` would close it. Schema needs the tier
kind denormalized onto `event_ticket_purchase` (or a join in the index, which Postgres doesn't
support). Alternative: pre-flight the insert in a SERIALIZABLE transaction; retry on
serialization failure surfaces to the cashier as "Couldn't enter '{tier.Name}' — someone may
have just entered them." Either way, today the check is advisory.

### 6.11 — Reward redemption lookup + use are tenant-agnostic

`RewardRepository.GetRedemption(redemptionId)` returns the row by id with no tenant filter.
`MarkRedemptionUsed(redemptionId, kind, sourceId)` updates by id with no tenant filter.
`CounterController.CreateSale` line 393-405 fetches the redemption, then validates `voucher.UserId
!= rider.Id`, then validates the *program* via `GetProgram(programId, tenantId)` which IS tenant-
scoped — so the program check would catch a cross-tenant voucher. But:
- An attacker with a SalesCounter credential at tenant A and a voucher id from tenant B can call
  `Counter/Sale` with that id; the rider-mismatch check catches it (voucher.UserId is a different
  user). So today there's a defense.
- BUT a rider with accounts at both tenant A and tenant B could in theory present a voucher from
  B at A's counter and have it apply, IF the program scope check happened to pass (rare, but
  possible). The defense is through `GetProgram(programId, _tenantContext.TenantId)` line 402.

So this is hardened in CounterController by design. The general repo gap is worth flagging — a
copy-pasted query from a future endpoint that omits the program check would leak across tenants.

## Concurrency / state machine summary

State machine across kinds (pass / event_ticket are mature; others incomplete):

```
pending → paid       (webhook OR free-cart fast path OR cash sale)
pending → failed     (webhook OR explicit admin action)
pending → cancelled  ❌ never — admin Cancel requires status='paid'; pending rows linger forever (Section 4 #2)
paid    → cancelled  (admin Cancel; rider self-cancel; rider request-cancel just notifies)
paid    → redeemed   (gate scan / check-in)
paid    → refunded   (super-admin process refund — comes via cancelled)
cancelled → refunded (super-admin process refund — the only canonical path)
cancelled → paid     ❌ no path
refunded → *         ❌ terminal
redeemed → cancelled (allowed by schema; only triggered by `UndoRedeemed` → admin Cancel; doc unclear)
disputed (status not in machine — dispute lives on `dispute` table side, doesn't flip purchase row)
```

Concurrency windows worth naming:
- Race-entry duplicate insert (Finding 6.10).
- Tier inventory read-then-write (Section 5 #3 family).
- Webhook racing admin cancel: admin cancels a `pending` row at the moment the webhook flips it
  to `paid`. The admin sees a 400 ("Cannot cancel a purchase with status 'paid'") since cancel
  requires `status='paid'`... but actually the admin Cancel requires *exactly* `paid`, so:
  - If admin runs Cancel WHILE webhook is processing, the row might still be `pending`.
    `_purchases.Cancel`'s SQL has `WHERE status='paid'` — affected rows = 0, but no error
    propagates. Controller returns 200 with `status:cancelled`. Subsequent webhook hits
    `WHERE status='pending'` in `OnPaymentSucceeded` (line 412 filter) — also affected rows = 0
    on the flip. End state: row is still `pending`, both operations think they succeeded. Stripe
    has the money, the rider sees `cancelled` in the UI, the ledger row is never written.
    **This is a real bug — admin Cancel against a `pending` row needs to explicitly reject, not
    silently no-op.** Add `if (UpdateAffected != 1) return BadRequest("Status changed under you;
    refresh and try again.")`.

## Audit & traceability

- `audit_log` is missing entries for: admin cancel pass (6.3), admin cancel ticket (6.3),
  Stripe-failed self-cancel (6.4 — currently a log-warning), counter rider lookup (no audit
  needed but worth knowing — the PII probe of 6.16 is unlogged), counter rider create (also
  unlogged).
- `impersonated_by` claim still not flowed (Section 1 known gap).
- Cancel notification to the rider doesn't exist (6.5).

## Open questions

1. **Should the counter sell event tickets at all?** The backend supports it (`kind='event_ticket'`
   handled in `CreateSale`); the FE doesn't expose it (6.6). If the intent is "counter is for
   day-of walk-ins, not advance ticket purchase," remove the backend support to keep things
   honest. If the intent is "counter can sell anything online sells," wire the FE.
2. **Counter membership-gate policy:** should a cashier be allowed to bypass the membership
   requirement (6.1), perhaps with a "manager override" prompt? Today there's no gate at all,
   which is almost certainly wrong; whether the policy should be "block always" or "override
   with reason" is a product call.
3. **Cash refunds:** the back-end has no path for refunding a cash sale (6.13, 6.14). What's the
   intended workflow — cashier opens till and gives the money back, then runs Cancel + the
   ledger backout is manual? Or do we need a "Cash refund" button that posts the inverse ledger
   row inline?
4. **Multi-line cancel UX:** when a counter cart of `{pass + ticket + extras + membership}` needs
   partial refund (rider returns the t-shirt but keeps the pass), is the policy "rider eats the
   t-shirt cost" or "Cancel + refund just that line"? Today only the pass/ticket are
   cancellable, so the answer is forced — but as soon as 6.9 lands, the FE needs a clear UX for
   "cancel just this line" vs "cancel the whole PI."
5. **Voucher restoration policy on cancel:** is a refunded purchase supposed to restore the
   voucher (6.7)? If yes (the natural rider expectation), the restore needs to happen at refund
   time, not cancel time, so a cancelled-but-not-refunded row doesn't double-spend the voucher.
6. **Race-day overload concurrency:** 6.10 is a real bug, but at peak track usage the
   read-then-write pattern across inventory + race-entry-dedup + waiver-fetch + several
   create-then-update statements is going to be the dominant performance + correctness story.
   Consider a Section 7 dedicated to "all reservation-style writes" — race entries, season-pass
   reservations, rental items, variant inventory — and applying a single uniform pattern (either
   partial unique indexes + INSERT ... ON CONFLICT, or wrapping handlers in
   SERIALIZABLE transactions with retry).
