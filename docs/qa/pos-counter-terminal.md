# QA Test Plan: In-Person POS (Counter) & Stripe Terminal Tap-to-Pay

> Scope: the staff-facing counter (`CounterController`): rider find/create, multi-item cart (tickets + extras + membership), ladder/inventory/capacity enforcement, race-entry uniqueness, waiver-on-behalf, cash + free-voucher + Stripe card paths, and Stripe Terminal connection-token + card-present payment intent. Last updated: 2026-06-20.

## Surface map
- **Controller:** `webapi/Controllers/CounterController.cs`, route prefix `api/Counter`, guarded by `[Authorize(Policy = TenantPermissions.Policy.SalesCounter)]`. Every action first checks `_tenantContext.IsResolved`.
- **Endpoints:**
  - `POST /api/Counter/Riders/Find` (`RiderLookupRequest { Email }` -> `RiderLookupResponse`): global lookup `GetGlobalByEmail` then tenant-scoped `GetByEmail` fallback; returns waiver-signed state, `IsMinor`, parent fields, emergency contact.
  - `POST /api/Counter/Riders` (`CreateCounterRiderRequest`): creates a **global** rider (`TenantId = null`); dedup probe, birthdate + emergency-contact validation.
  - `POST /api/Counter/Sale` (`CounterSaleRequest` -> `CounterSaleResponse`): the cart engine.
  - `POST /api/Counter/Terminal/ConnectionToken` (-> `TerminalConnectionTokenResponse { Secret, LocationId }`).
  - `POST /api/Counter/Terminal/PaymentIntent` (`CardPresentTestChargeRequest { AmountCents>=50, ReceiptEmail? }`).
- **DTOs:** `webapi/Controllers/API/Data/Counter/` (`CounterSaleRequest`, `CounterCartItem`, `RiderLookupRequest/Response`, `CreateCounterRiderRequest/Response`, `CounterSaleLineItem`, `CounterSaleResponse`, `TerminalConnectionTokenResponse`).
- **Pricing logic:** `Services/Pricing/PriceStepResolver.cs` (`Resolve` -> active = highest-priced fired step; `Next` for "then $X"). `EventTicketTierRepository.GroupSoldCount` (cumulative active sales across a ladder group), `.SoldCount` (per standalone tier).
- **Uniqueness:** `EventTicketPurchaseRepository.HasActiveRaceEntry(tenantId, tierId, userId, email)` (matches `purchaser_user_id` OR `LOWER(purchaser_email)`, status in pending/paid/redeemed).
- **Concurrency:** `_db.AcquireAdvisoryLock($"event-capacity:{evId}")`, acquired per distinct event in sorted order, shared key space with online checkout; released before the Stripe network call.

## Concepts under test
- **Cart kinds:** `event_ticket`, `extras`, `membership` (the `pass` kind in `CounterCartItem` comments is not handled by `CreateSale` and falls through to "Unsupported cart item kind").
- **Ladder line normalizes to the active step.** If `tier.LadderGroup` is set, the cart loads all active steps in that group, computes `GroupSoldCount`, and `PriceStepResolver.Resolve` replaces `tier` with the active (highest-priced fired) step. The whole class sells against `event.capacity` (no per-step inventory). Standalone tiers (no ladder) use their own `tier.Inventory`.
- **Race-entry one-per-rider spans the class.** For `Kind == "race_entry"`: `Quantity > 1` rejected, duplicate line in the same cart rejected, and `HasActiveRaceEntry` is checked for **every** step id in the ladder group (`classStepIds`), not just the active step.
- **Service charge math** (`ComputeWithServiceCharge`): per-unit service charge = `unitPrice * tenant.ServiceChargeBps / 10000`; rider-paid portion = `serviceCharge * tier.RiderPaidServiceChargeBps / 10000` added to the amount charged. The charged amount and the `ServiceChargeCents` (ridepass cut) are tracked per row.
- **Three settlement paths:** cash (mark rows `paid`, write a `tenant_ledger` `sale` row per line with `NetToTenantCents = -serviceCharge`, skip Stripe), free-cart (`totalCents == 0` from a 100%-off voucher, mark rows `paid`, ledger row all-zeros `PaymentMethod = "voucher"`), and Stripe (one PaymentIntent for the whole cart, intent id stamped on every row for the webhook to finalize).
- **Voucher** (`RewardRedemptionId`) applies to **one unit of one ticket line** only (index 0 after re-ordering); must belong to the rider, be unused, and its program active with `RequirementKind` `event_ticket` or `any`. No qualifying ticket line -> rejected.
- **Waiver-on-behalf:** `waiverRequiredByCart` is set per item (`ev.RequiresRiderWaiver` for tickets, `product.RequiresWaiver` for extras), not by the tenant having an active waiver alone. If required and unsigned, `SignWaiver` + a valid PNG data URL are mandatory; a minor (`WaiverPolicy.IsMinor(birthdate)`) additionally requires `ParentName` + `ParentPhone` (>=7 digits).
- **Terminal Location** is lazily provisioned from the tenant address (`AddressLine`, `City`, `Country`, `PostalCode`, optional `Region`) and persisted via `SetStripeTerminalLocationId`; subsequent calls reuse it.

## Preconditions / test data
- A tenant with `SalesCounter` permission granted to the test staff account; subdomain resolves to the tenant. A second tenant (Tenant B) for isolation checks.
- Tenant settings variants: one with `RequireEmergencyContact = true`, plus `ServiceChargeBps` set (e.g. 500 = 5%), `ExtrasEnabled`, `MembershipEnabled` + `MembershipPriceCents > 0` + `MembershipDurationKind` ("yearly" vs other).
- A scheduled, not-yet-ended race event with `Capacity = 30` and a ladder class of 3 steps in one `ladder_group`: $50 base (no trigger), $65 at `min_sold = 10`, $75 at a date trigger. A standalone `race_entry` tier with its own `Inventory`, a `gate_fee`/non-race tier, an active rider waiver, an extras product with active variants + inventory, and an extras product with `RequiresWaiver = true`.
- Riders: a global rider with a signed waiver, a global rider with no waiver, a global **minor** rider, a rider missing an emergency-contact phone, and a fresh email for create-new.
- A reward redemption (voucher) belonging to one rider, program active, with a percent-off (test both <100% and 100%).
- A Stripe test account in Terminal test mode + a simulated/physical reader for the cashier mobile app.

---

## Rider lookup / create

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| POS1 [NN] | Find by global email | `POST Riders/Find` with a global rider's email | 200 with id, name, `HasSignedCurrentWaiver`, `IsMinor`, emergency-contact fields. Global account found before any tenant-scoped lookup. |
| POS2 [NN] | Find falls back to tenant-scoped | Email that exists only as a tenant-scoped user (e.g. tenant_admin) | Found via the `GetByEmail(TenantId, ...)` fallback. |
| POS3 [NN] | Find unknown email | Email with no global and no tenant account | 404 "No customer with that email." |
| POS4 [NN] | Find reports waiver state | Find a rider who signed the active waiver vs one who has not | First: `HasSignedCurrentWaiver = true` with `WaiverSignedAtUtc` (UTC-kinded) + signature data URL. Second: `false`. With no active waiver: `true` (nothing to sign). |
| POS5 [NN] | Find reports minor + parent fields | Find a minor who signed via parent | `IsMinor = true`, `WaiverSignedByParent = true`, parent name/phone populated. |
| POS6 [NN] | Create new global rider | `POST Riders` with fresh email, name, valid birthdate, emergency contact | 201/200; rider created with `TenantId = null`, role `rider`, random password. Returned id usable as `RiderId` in a sale. |
| POS7 [NN] | Create rejects duplicate | `POST Riders` with an email that already exists (global or tenant-scoped) | 400 "A customer with that email already exists. use Find instead." No second row. |
| POS8 [NN] | Create rejects bad birthdate | Future / invalid birthdate (`UserController.IsValidBirthdate` fails) | 400 "Please enter a valid birthdate." |
| POS9 [NN] | Create requires emergency contact | Blank contact name or phone with < 7 digits | 400 "Please enter a valid emergency contact name and phone number." |
| POS10 [R] | Email trimmed | Find/Create with leading/trailing whitespace on email | Whitespace trimmed before lookup/create; same row matched. |

## Cart & pricing

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| POS11 [NN] | Tenant not resolved | Any `Sale` call when subdomain/tenant does not resolve | 400 "No tenant resolved." (applies to every endpoint). |
| POS12 [NN] | Emergency-contact gate | Tenant `RequireEmergencyContact = true`; ring up a rider with no `EmergencyContactPhone` | 400 asking to update the rider's profile; no rows written. |
| POS13 [NN] | Empty / non-positive cart | Cart whose computed `totalCents <= 0` (and not the voucher free path) | 400 "Cart total must be positive." |
| POS14 [NN] | Unsupported kind | Cart item with `Kind = "pass"` or anything not event_ticket/extras/membership | 400 "Unsupported cart item kind: <kind>". |
| POS15 [NN] | Ladder line charges the ACTIVE step | Add the $50 base-step tier id while the group's date/qty trigger has fired ($75 active) | Line is normalized to the $75 step; `lineItems[].UnitPriceCents` and `LineAmountCents` reflect $75 + service charge, regardless of which step id was sent. |
| POS16 [NN] | Ladder honors whole order at active step | Active step $65; add quantity 3 (non-race tier) | All 3 units charged at $65 (no mid-order step-up within one cart). |
| POS17 [NN] | Standalone tier inventory | Standalone tier with `Inventory = 5`, 3 already sold; add quantity 3 | 400 "Tier '<name>' has only 2 left." (uses `SoldCount`, not group count). |
| POS18 [NN] | Inactive / ended event | Add a tier whose tier `IsActive = false`, or whose event status != scheduled or `EndsAt < now` | 400 tier "not available" / "for an event that has already ended." |
| POS19 [NN] | Service-charge math | Tier $100, `ServiceChargeBps = 500`, `RiderPaidServiceChargeBps = 10000` | Charged unit = $105; `ServiceChargeCents` = 500 (the ridepass cut). With `RiderPaidServiceChargeBps = 0`, charged = $100 but cut still 500. |
| POS20 [NN] | Multi-item cart | One cart: 1 race entry + 1 extras (with variant) + 1 membership | Single PaymentIntent for the summed total; one ticket row, one extras row (variant attrs frozen, own QR), one membership row; all stamped with the same intent id. |
| POS21 [NN] | Extras gated by tenant | Add `extras` item when `tenant.ExtrasEnabled = false` | 400 "Add-ons are not enabled at this track." |
| POS22 [NN] | Extras variant required | Product has active variants but item omits `VariantId` | 400 "Pick a variant for ...". Wrong/inactive variant id -> "That option isn't available". |
| POS23 [NN] | Extras variant inventory | Variant `Inventory` exceeded by quantity | 400 "Only N of that variant left." Sold-out product -> "<name> is sold out." Expired product -> "no longer being sold." |
| POS24 [NN] | Membership rules | Add membership with quantity != 1, or two membership lines, or when `MembershipEnabled=false`/price<=0 | Respectively 400 "sold one at a time", "Only one membership per sale", "Memberships aren't sold at this track." `validTo` = now+365d only when `MembershipDurationKind = "yearly"`. |
| POS25 [NN] | Race-entry quantity capped | `race_entry` line with `Quantity > 1` | 400 "Riders can only enter '<name>' once." |
| POS26 [NN] | Race-entry duplicate in cart | Two lines for the same race tier in one cart | 400 "Riders can only enter '<name>' once." |
| POS27 [NN] | Race-entry already entered (prior sale) | Rider already has an active entry in the class; ring it again | 400 "<First> is already entered in '<name>'." (matched by user id or email, statuses pending/paid/redeemed). |
| POS28 [NN] | Race-entry uniqueness spans ladder steps | Rider holds an entry at the $50 step; ring the $65/active step (same `ladder_group`) | Rejected. `classStepIds` checks every step id in the group via `HasActiveRaceEntry`, not just the active step. |
| POS29 [NN] | Waiver enforced from cart item | Cart with a tier whose event `RequiresRiderWaiver = true`, rider unsigned, `SignWaiver = false` | 400 "Rider has not signed the active waiver." |
| POS30 [NN] | Sign waiver on behalf | Same, `SignWaiver = true` + valid PNG data URL | Sale proceeds; signature recorded for the rider with the request IP; subsequent Find shows signed. |
| POS31 [NN] | Bad / missing signature image | `SignWaiver = true` but data URL not a PNG data URL or out of size bounds | 400 "A handwritten signature is required to sign the waiver." |
| POS32 [NN] | Minor parent fields required | Minor rider signing; omit `ParentName`/`ParentPhone` (or phone < 7 digits) | 400 "Riders under 18 need a parent or guardian's name and phone number on the waiver." With valid parent fields: signed with `signedByParent = true`. |
| POS33 [NN] | Waiver not re-signed if already signed | Rider already signed the active waiver; cart requires it | No re-sign; existing signature id reused; sale proceeds. |
| POS34 [NN] | Waiver only when an item requires it | Cart of extras/membership where no item flags `RequiresWaiver`, even though tenant has an active waiver | No waiver demanded; sale proceeds without `SignWaiver`. |
| POS35 [NN] | Voucher on a ticket line | Provide rider's valid 50%-off `RewardRedemptionId` with a ticket in the cart | One ticket unit discounted 50% (placed at index 0, qty 1), the rest full price; total reduced; redemption marked used on settle, bound to the first line's id. |
| POS36 [NN] | Voucher with no qualifying line | Voucher (program `event_ticket`) but cart has only extras/membership | 400 "No qualifying line for this voucher. pick a race entry or gate fee." |
| POS37 [NN] | Voucher ownership / reuse / inactive program | Voucher belonging to another rider, already-redeemed, or inactive program | 400 "isn't this rider's" / "already been used" / "program is no longer active." |

## Cash

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| POS38 [NN] | Cash sale marks rows paid + ledger negative | `PaymentMethod = "cash"`, multi-line cart (ticket + membership) | Every ticket/membership row -> `paid`; one `tenant_ledger` `sale` row per line, `GrossCents` = charged amount, `RidepassCutCents` = service charge, `NetToTenantCents = -serviceCharge`, `PaymentMethod = "cash"`. `ClientSecret` empty in response; no Stripe call. |
| POS39 [NN] | Cash extras settle without ledger | Cash cart including an extras line | Extras row flipped to `paid`; no ledger row for it (source_kind 'extras' not in the CHECK constraint). |
| POS40 [NN] | Cash + voucher | Cash cart with a voucher on a ticket | Discounted total reflected in the paid ticket; redemption marked used; ledger reflects discounted gross. |
| POS41 [R] | Invalid payment method | `PaymentMethod = "venmo"` (anything but stripe/cash; empty -> defaults to stripe) | 400 "paymentMethod must be 'stripe' or 'cash'." |
| POS42 [NN] | Cashier stamped | Any sale by a staff user with a `UserId` JWT claim | `SoldByUserId` on every created row equals the cashier id. |

## Card (Stripe)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| POS43 [NN] | Stripe PI for the whole cart | `PaymentMethod = "stripe"` (or omitted), positive total | One PaymentIntent created for `totalCents`, currency usd, `receiptEmail` = rider email, metadata `sale_kind=counter` + `rider_id` + `tenant_id` + `item_count`; `ClientSecret` returned; intent id stamped on every ticket/extras/membership row. Rows remain `pending` until the webhook. |
| POS44 [NN] | Free-cart fast path | 100%-off voucher zeroes a single-line cart (`totalCents == 0`, not cash) | No PaymentIntent; row -> `paid`; all-zero ledger row with `PaymentMethod = "voucher"`; redemption marked used; `ClientSecret` empty, `TotalAmountCents = 0`. |
| POS45 [R] | Stripe provider error surfaced | Force `CreatePaymentIntentAsync` to throw `InvalidOperationException` (e.g. missing Stripe config) | 400 with the provider message; rows already created remain pending (no intent id). |

## Terminal (tap-to-pay)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| POS46 [NN] | Connection token provisions Location | `POST Terminal/ConnectionToken` for a tenant with full address and no `StripeTerminalLocationId` | A Stripe Terminal Location is created from the tenant address and persisted; response returns `Secret` + the new `LocationId`. |
| POS47 [NN] | Connection token reuses Location | Call again for the same tenant | Same stored `LocationId` returned (idempotent, no new Location). |
| POS48 [NN] | Location blocked on missing address | Tenant missing any of address line / city / country / postal code | 400 "Cannot provision a Stripe Terminal Location ... fill in the tenant's address ... under Settings first." No token issued. |
| POS49 [NN] | Card-present PI | `POST Terminal/PaymentIntent` with `AmountCents >= 50` (+ optional `ReceiptEmail`) | Card-present PaymentIntent created scoped to the Location; metadata `sale_kind=card_present_test`, `tenant_id`, and `sold_by_user_id` when the JWT has `UserId`; response returns `paymentIntentId`, `clientSecret`, `amountCents`. |
| POS50 [NN] | Card-present amount floor | `AmountCents < 50` | 400 "Amount must be at least 50 cents." |
| POS51 [R] | Terminal token error surfaced | Provider throws on `CreateTerminalConnectionTokenAsync` | 400 with the provider message. |
| POS52 | Mobile reader flow (manual) | Cashier app fetches a token, discovers a reader scoped to the Location, collects on the card-present PI, taps a test card | PI confirms in Stripe test mode; reader shows success. Note: this PI is the v1.5 validation stub, not yet wired to a `Sale` cart (no ledger/ticket rows). |

## Edge

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| POS53 [NN] | Concurrent oversell guarded by advisory lock | Ladder class with 1 spot to `event.capacity`; fire two near-simultaneous `Sale` calls (or one counter + one online checkout) each adding 1 unit | Exactly one succeeds; the other gets 400 "Only N spot(s) left for ...". The `event-capacity:{eventId}` advisory lock serializes the recheck + inserts; same key space as online checkout so they contend. |
| POS54 [NN] | Authoritative recheck under lock | Build a cart while spots remain, but capacity fills before submit | Re-check inside the lock (`GroupSoldCount` reload) rejects with the spots-left message even though the cart-build fast-fail passed. |
| POS55 [NN] | Multi-event cart lock ordering | Cart spanning two events' tiers | Both `event-capacity` locks acquired in sorted event-id order (deadlock-safe), released before the Stripe call. Sale completes. |
| POS56 [NN] | Cart-local ladder capacity accounting | Single cart adding more ladder units than remaining capacity (e.g. 2 left, add 3 across lines in the same group) | Rejected with spots-left; the build loop sums in-cart group units (`cartGroupUnits`) plus `groupSold` against `event.capacity`. |
| POS57 [NN] | Tenant isolation, rider scope | Tenant B staff ring up against a tier id that belongs to Tenant A | `_tiers.GetById(itemId, TenantB)` returns null -> 400 "not available." No cross-tenant tier sold. |
| POS58 [NN] | Tenant isolation, ledger + counts | Cash sale at Tenant A | Ledger row, `GroupSoldCount`, and `HasActiveRaceEntry` all scoped to `_tenantContext.TenantId`; Tenant B dashboards/counters unaffected. |
| POS59 [NN] | Permission gate | Staff lacking `SalesCounter` hits any Counter endpoint | 403 (policy denies). |
| POS60 [NN] | Terminal Location uses correct tenant address | Two tenants each provision a Location | Each Location carries its own tenant's address; `StripeTerminalLocationId` stored per tenant, never shared. |

---

## Known risks / watch-items
- **Money correctness, voucher placement.** The voucher re-orders `ticketItems` so the discounted unit lands at index 0 and `voucherTicketIdx = 0`; verify the discount lands on exactly one unit and the ledger/PI total matches `totalCents` after the swap. A multi-ticket cart where the intended discounted line is not the first warrants a targeted check (the code always discounts the first ticket line, index 0).
- **Cash ledger is the only money record for cash sales.** `NetToTenantCents = -serviceCharge` means the tenant owes the platform the cut. Confirm reporting/settlement reads this sign correctly; a flipped sign silently mis-bills every cash sale. Extras cash sales write **no** ledger row (source_kind 'extras' not in the CHECK constraint), so their service charge is not captured at the counter. Flag for revenue reconciliation.
- **Terminal card-present PI is a stub.** `Terminal/PaymentIntent` takes a raw amount and creates no ticket/extras/membership rows and no ledger entry. Money can be collected on a reader with no corresponding sale record until the full cart-validating endpoint ships (v1.5). Do not treat a successful tap as a recorded sale.
- **Advisory lock vs Stripe.** Capacity locks are released before the Stripe network call, so pending rows hold the capacity. If the Stripe call fails (POS45), pending rows linger and keep counting toward `GroupSoldCount`/`SoldCount`/`HasActiveRaceEntry` (pending is an active status) until they expire or are cleaned up. Confirm a cleanup/expiry exists, else a failed card attempt can transiently block a sell-out spot.
- **Online ladder path is not advisory-locked** (per the events plan), but the counter path is; a simultaneous online + counter race relies on both contending on the same `event-capacity:{eventId}` key. Verify the online checkout actually takes the lock, or POS53/POS54 only protect counter-vs-counter.
- **Multi-tenant isolation.** Rider create makes a global account (`TenantId = null`) by design, but every sale-side read (`GetById` on tier/event/product, `GroupSoldCount`, `SoldCount`, `HasActiveRaceEntry`, ledger insert) must be tenant-scoped. POS57/POS58 are the load-bearing checks. `_users.GetById(request.RiderId)` is not tenant-scoped (riders are global) which is intended, but confirm a sale cannot attach a foreign tenant's tier to that rider.
- **Race-entry email match is case-insensitive** (`LOWER(purchaser_email)`); the user-id branch is exact. A rider with a differently-cased email on an older row is still caught. Good. Verify the global-vs-tenant lookup in Find/Create does not create a duplicate global account when only a tenant-scoped row exists with different casing (lookup uses exact `GetGlobalByEmail`).
- See the **Events / Pricing / Registration** plan for ladder semantics and the **Waitlist** plan for the sold-out follow-on flow that a counter sale can trigger.
