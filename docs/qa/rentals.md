# QA Test Plan: Rentals

> Scope: rental product CRUD (pool vs per-item tracking), per-item units + maintenance windows, availability/overlap math, rider booking (deposit, waiver, coupon, gift card), counter check-out / return with deposit capture and condition photos, and tenant isolation. Last updated: 2026-06-20.

## Surface map
- **Admin (CatalogManage):** `RentalController` product CRUD (`Products/Admin`, `POST/PUT/DELETE Products`, `Products/Reorder`), per-item units (`Products/{id}/Items` GET/POST, `PUT/DELETE Items/{id}`), maintenance windows (`Items/{id}/Maintenance` GET/POST, `PUT/DELETE Maintenance/{id}`).
- **User:** `RentalController.ListActive` (`GET Products`, gated on `tenant.RentalsEnabled`), `RentalController.Buy` (`POST Buy`, authed), `RentalController.ListMine` (`GET Mine`).
- **Counter (SalesCounter):** `Counter` (date-windowed booking list with per-item photo rows), `Counter/{id}/MarkOut`, `Counter/{id}/MarkReturned` (deposit capture + Stripe refund of the un-captured remainder, condition photos/notes).
- **Repo:** `RentalRepository` / `IRentalRepository` - availability (`SumOverlappingPoolReserved`, `CountAvailablePerItemUnits`, `PickAvailablePerItemUnits`), assignment (`AssignItems`, `ListAssignedItems`), condition capture, status transitions.
- **Migrations:** `Script0048_Rentals.sql` (rental_product / rental_item / rental_purchase), `Script0049_RentalsPhase2.sql` (maintenance, per-item assignment, deposit capture, condition photos).

## Concepts under test
- A **product** (`rental_product`) has a `daily_rate_cents`, a `deposit_cents`, a `tracking_kind` of `pool` or `per_item`, `requires_waiver`, and `rider_paid_service_charge_bps`. Pool products carry an `inventory_pool` count; per-item products carry individual `rental_item` units.
- **Tracking kind is immutable after creation** - the update endpoint blocks switching pool <-> per-item because the per-item assignment table doesn't translate.
- **Availability** is date-overlap based: a unit/pool slot is reserved if a `paid` or `out` purchase overlaps `[start_date, end_date]` (inclusive both ends). Per-item units must also be `status='available'` and free of an overlapping `rental_item_maintenance` window.
- **Booking math:** subtotal = daily_rate * days * qty where days = (end - start).Days + 1; coupon (scope `rental`) discounts subtotal; rider service-charge portion added; deposit added per unit on top; gift card applies after discounts but before Stripe; a single PaymentIntent charges rental fee + service charge + deposit. Deposit is refunded (or partly captured for damage) on return.
- **Per-item assignment** is persisted at booking so the rider gets the same units on pickup; pool products skip assignment.
- **Lifecycle:** pending -> paid (webhook or gift-card-covered fast path) -> out (MarkOut) -> returned / damaged (MarkReturned). Each purchase has its own `redemption_token`.

## Preconditions / test data
- A tenant with `RentalsEnabled = true`; a second tenant for isolation; a third with `RentalsEnabled = false`.
- A **pool** product with `inventory_pool = 3`, `daily_rate_cents = 2000`, `deposit_cents = 5000`.
- A **per-item** product with 3 units (`available`), plus one `retired` unit and one `maintenance` unit; `daily_rate_cents = 3000`, `deposit_cents = 10000`.
- A product with `requires_waiver = true`; an active waiver; a rider with a signed signature and one without.
- A tenant with `RequireEmergencyContact = true` and a rider missing an emergency contact phone.
- A `rental`-scoped coupon and a gift card with a known balance.
- Rider accounts plus a SalesCounter staff user. Stripe Terminal location resolvable (tenant address complete) for any card-present checks.

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RN1 [NN] | Pool product CRUD | Create a pool product with `inventory_pool = 3`; edit rate/deposit; reopen | Saves with `tracking_kind='pool'`, `inventory_pool` persisted; rate/deposit edits stick; created with current `tenant_id`. |
| RN2 [NN] | Pool needs positive inventory | Create/edit a pool product with `inventory_pool` null or <= 0 | 400 "Pooled rentals need a positive inventory." |
| RN3 [NN] | Service-charge share bounds | Set `rider_paid_service_charge_bps` to -1 and to 10001 | 400 "Service-charge share must be 0-100%." Values 0..10000 accepted. |
| RN4 [NN] | Tracking kind immutable | Create a per-item product, then PUT it with `tracking_kind='pool'` | 400 "Tracking kind can't be changed after creation." Other fields on a same-kind update still save. |
| RN5 [NN] | Per-item unit CRUD | Add units (label/serial/notes/status) to the per-item product; edit one; delete an unbooked one | Units created with `tenant_id` + `product_id`; `CreateItem` on a pool product returns 400 "uses pooled inventory, not per-item tracking"; delete of an unbooked unit succeeds. |
| RN6 [NN] | Delete unit with bookings | Book a per-item unit, then delete it | 400 "has bookings on file. Set status to retired instead." (FK RESTRICT). |
| RN7 [NN] | Delete product with bookings | Delete a product that has any rental_purchase rows | 400 "has bookings on file ... Set inactive instead." |
| RN8 [NN] | Maintenance window CRUD | Add a maintenance window to a unit (end before start, then valid); edit; delete | End-before-start rejected 400; valid window saves with `tenant_id`; window makes the unit unavailable for overlapping dates (see RN15). |
| RN9 [R] | Reorder products | Drag-reorder and POST `Products/Reorder` | sort_order updated in one round-trip scoped by tenant; foreign ids in the payload ignored. |
| RN10 [R] | Admin list item counts | Open `Products/Admin` for the per-item product | `PerItemTotal` = all units, `PerItemAvailable` = units with `status='available'` (does not subtract date-overlapping bookings - it is a status count, confirm intent). |

## User (book)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RN11 [NN] | Pool booking happy path | Book 1 unit of the pool product for valid future dates | One `rental_purchase` (status `pending`), days = inclusive span, amount = rate*days*qty + rider service charge, deposit added; single PaymentIntent for the gross; redemption_token issued. |
| RN12 [NN] | Pool capacity | With `inventory_pool = 3`, book 2 overlapping units, then attempt 2 more overlapping | Second request rejected "Only 1 unit available - you asked for 2"; a non-overlapping date range books freely. |
| RN13 [NN] | Per-item assignment | Book 2 units of the per-item product | Two specific available units picked (`PickAvailablePerItemUnits`) and persisted via `AssignItems`; the same units show on the counter list. |
| RN14 [NN] | Per-item exhaustion | Book all 3 available units, then attempt a 4th overlapping | 4th rejected "Only N unit(s) available across those dates." Retired/maintenance units never offered. |
| RN15 [NN] | Maintenance blocks availability | Put a window on one unit spanning the booking dates, then book up to capacity | That unit is excluded from `PickAvailablePerItemUnits`; effective availability drops by one for overlapping dates. |
| RN16 [NN] | Date validation | Book with end < start, with start > 1 day in the past, and with a 31-day span | "End date must be on or after start date.", "Start date is in the past.", "Rentals are limited to 30 days." respectively. |
| RN17 [NN] | Waiver gate | Book the waiver-required product unsigned, then signed | Unsigned 400 "Please sign the current waiver ..."; signed sets `waiver_signature_id`; no active waiver -> proceeds without one. |
| RN18 [NN] | Emergency contact gate | On the `RequireEmergencyContact` tenant, book as a rider with no emergency phone | 400 "Please add an emergency contact on your profile before booking a rental." |
| RN19 [NN] | Coupon (rental scope) | Apply a valid rental coupon, then a non-rental-scoped / expired one | Valid: subtotal reduced, `CouponRedemption` recorded `SourceKind='rental'`; invalid: 400 with validator message. |
| RN20 [NN] | Gift card partial + full | Book with a gift card covering part, then one covering the full gross | Partial: Stripe charges the remainder, redemption + balance applied; full coverage: status set `paid` immediately, `ClientSecret` empty, AmountCents 0. |
| RN21 [NN] | Feature flag off | On the `RentalsEnabled = false` tenant, call `GET Products` and `POST Buy` | `GET Products` empty list; `POST Buy` 400 "This tenant doesn't offer rentals." |
| RN22 [R] | Mine list | After a booking, open `GET Mine` | Row shows product name, dates, qty, amount, deposit, status; ordered by start_date desc. |

## Counter (check-out / return)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RN23 [NN] | Counter list window | Open `Counter` with default and custom from/to + status filter | Lists bookings whose `[start,end]` overlaps the window, tenant-scoped; per-item rows carry assigned-unit labels + photo fields. |
| RN24 [NN] | Mark out | MarkOut a `paid` booking with per-item checkout photos | Status -> `out`, `checked_out_at` set; checkout photo/notes saved per assigned unit; MarkOut on a non-paid booking returns 400. |
| RN25 [NN] | Photo validation | MarkOut with a non-JPEG/PNG or oversized data-url | 400 "Photo must be a JPEG/PNG data-url under ~2MB."; null photo allowed (optional). |
| RN26 [NN] | Return, no damage | MarkReturned with `DepositCapturedCents = 0` | Status -> `returned`; full deposit refunded against the rental PI; idempotency key includes id + amount. |
| RN27 [NN] | Return with damage capture | MarkReturned capturing part of the deposit | Captured clamped to `[0, deposit]`; status -> `damaged`; only the un-captured remainder refunded; `deposit_captured_cents` recorded; condition notes/photos saved. |
| RN28 [NN] | Return refund failure | Force the Stripe refund to fail on MarkReturned | 400 "Could not issue deposit refund via Stripe. Mark the rental returned again ..." - VERIFY whether status flips: the code comment says "still flip status" but the method returns before calling `MarkReturned`, so the booking stays `out`. Confirm intended behavior (re-mark must be safe / not double-refund). |
| RN29 [R] | Tenant isolation on counter ops | MarkOut / MarkReturned a booking id belonging to another tenant | 404 "Rental not found." (`GetPurchase` then `TenantId` re-check). |
| RN30 [R] | Status guard | MarkReturned a `pending` or already `returned` booking | 400 "Can't mark returned a rental with status '...'." (only `out` or `paid` allowed). |

---

## Known risks / watch-items
- **Availability is racy** (documented in code): pool `SumOverlappingPoolReserved` and per-item `PickAvailablePerItemUnits` are read-then-write with no row lock or transaction. Two simultaneous bookings of the last unit can both succeed and oversell. The per-item path picks units twice (check, then assign) with a guard ("Lost the units between check and assignment - please retry") but a concurrent booking can still steal a unit between the two picks.
- **Coupon / gift card mutate state before payment confirmation.** `RecordRedemption` (coupon), `RecordRedemption` + `ApplyToBalance` (gift card), and per-item `AssignItems` all run while the purchase is still `pending`. If the Stripe payment later fails or is abandoned, the coupon use and the gift-card balance deduction are not rolled back, and the assigned units stay reserved (their booking is `pending`, which does not count toward overlap, so units are effectively freed - but the gift-card balance loss is real). Confirm the webhook/failure path reverses these.
- **Refund-failure status discrepancy (RN28):** comment vs code disagree on whether the booking flips to returned when the deposit refund errors. As written it stays `out`, so a retry re-attempts the refund - validate the idempotency key prevents a double refund and that a partial-capture retry can't refund twice.
- **`MarkOut` / `MarkReturned` / `UpdateStatus` repo methods filter by id only** (no tenant predicate); the controller re-checks `TenantId` after `GetPurchase`, so isolation depends on that guard staying in place. The webhook-driven `UpdateStatus(id, 'paid')` similarly trusts the PI lookup.
- **`PerItemAvailable` is a status count, not a date-aware availability count** (RN10) - it ignores overlapping bookings and maintenance, so the admin number can overstate what a rider can actually book on given dates.
- **30-day cap and 7-day Stripe hold:** deposits are charged outright (not pre-authorized) precisely because Stripe holds expire at 7 days; confirm refunds work for rentals longer than a week and that the deposit line is clearly itemized to the rider.
- Cross-tenant isolation: product/item/maintenance reads all go through `(id, tenantId)`; verify a foreign productId on the Items/Maintenance endpoints returns 404 and reorder ignores foreign ids.
