# Section 4: Day-pass + event-ticket sale paths

## Scope

Read end-to-end:

- `webapi/Controllers/PurchaseController.cs` — `BuyPass`, `BuyEventTicket`, `CancelPass`, `CancelTicket`, `ValidateVoucher`, `CheckMembershipGate`, `InsertZeroLedger`, `ComputeWithServiceCharge`, `ResolveExtraVariant`, `BuyGiftCard` (the gift-card buy action lives in the same file but is mostly Section 2 territory; included only for context).
- `webapi/Controllers/EventTicketTierController.cs` — tier CRUD + `Reorder` + `ValidateBundledCoupon`.
- `webapi/Controllers/PassProductController.cs` — product CRUD + reorder.
- `webapi/Controllers/RedemptionController.cs` — `Preview`, `Redeem`, `Order`, `RedeemBulk`, `ResolveAnchorPaymentIntentId`, `LookupAsync`, `CheckPassWindow`, `ResolveTenantTimeZone`.
- `webapi/Controllers/CounterController.cs` (read alongside `BuyEventTicket` for parity, as required).
- `Services/Repositories/PassPurchaseRepository.cs`, `EventTicketPurchaseRepository.cs`,
  `PassProductRepository.cs`, `EventTicketTierRepository.cs`.
- `Services/Repositories/Data/PaymentData/PassProduct.cs`, `PassPurchase.cs`, `EventTicketTier.cs`,
  `EventTicketPurchase.cs`.
- `Services/Coupons/CouponValidator.cs`, `Services/Repositories/CouponRepository.cs`
  (`RecordRedemption`, `CountRedemptions`, `CountUserRedemptions`).
- `Services/Repositories/RewardRepository.cs` (`GetRedemption`, `MarkRedemptionUsed`).
- `vueapp/src/views/BuyPass.vue`, `BuySpectator.vue`, `BuyTicket.vue`, `Redeem.vue`.
- `vueapp/src/components/BuyAdmissionFlow.vue` — the shared ticket-buy flow used by `BuyTicket.vue` and
  by `EventDialog.vue`. (`BuyTicket.vue` is a 46-line shell that delegates to this component, so the
  "ticket sale path" really lives here.)
- `vueapp/src/services/PassService.ts`, `TicketService.ts`.
- `webapi/Controllers/PaymentController.cs` (only the post-success block at lines ~460–510 that flips
  `applied_reward_redemption_id` → `MarkRedemptionUsed` and the bundled-coupon minter — needed to trace
  the voucher race).
- `TaskRunner/Program.cs` (looking for a janitor).

Per the Section 4 framing, Section 2 (payment / webhook / ledger), Section 1 (tenancy / auth), and
Section 3 (schema) findings are not repeated here. A handful of the things below necessarily touch the
webhook handler or the schema, but only at the boundary where it lands an invariant on a *sale row*.

## Architecture summary

**Two sale entry points and two purchase tables.** Day passes flow through `BuyPass` → `pass_purchase`,
keyed to a `pass_product`. Event tickets flow through `BuyEventTicket` → `event_ticket_purchase`, keyed
to an `event_ticket_tier` whose `kind` is either `spectator_pass` or `race_entry`. Day passes always
attach to an event (the standalone path was removed); event tickets always attach to an event by tier.
Both share the same coupon/voucher/gift-card stack, the same service-charge math, the same waiver
gate plumbing, the same `InsertZeroLedger` free-cart fast path, and the same "one PaymentIntent for
the whole cart, including extras + bundled membership" pattern.

**Cart shape.** Day-pass cart is `(product, quantity)` plus an optional add-on cart. Quantity is stored
inline on a *single* `pass_purchase` row — N spots reserved by one row, one redemption token, one QR.
Event-ticket cart is a list of `(tier, quantity)` entries, all tiers must belong to the same event,
and each unit becomes its own `event_ticket_purchase` row with its own redemption token. Extras always
get one row per unit. The webhook handler relies on the shared PaymentIntent id to fan out a single
`payment_intent.succeeded` event into "flip every row whose `stripe_payment_intent_id` matches" plus
the membership / extras / bundled-coupon side-effects.

**Free-cart fast path.** When a 100% voucher or a fully-covering gift card zeros out the combined
amount (pass net + extras + bundled membership), `BuyPass` / `BuyEventTicket` skip Stripe entirely:
flip rows to `paid`, write a `$0` ledger row per source, `MarkRedemptionUsed` on the voucher inline,
return an empty `ClientSecret`. The fast path is also responsible for any post-pay side-effects that
the webhook would normally run — see the findings.

**Waiver gating.** Three signals combine: pass/tier kind, the event flag `requires_rider_waiver`, and
per-extra `requires_waiver`. Race-entry tiers and day passes always look at `requires_rider_waiver`;
spectator tiers in this controller don't check `requires_spectator_waiver` at all (the dedicated
`SpectatorController` covers gate-fee + extras spectator purchases instead). When the waiver is
required and missing, the response is `400` with a friendly message; the FE catches `/waiver/i` and
opens a modal that links to `/Waiver`.

**Race-class one-per-rider.** `EventTicketPurchaseRepository.HasActiveRaceEntry` is the single source
of truth: matches by `purchaser_user_id` *or* lowercased `purchaser_email`, counts only
`pending|paid|redeemed` (cancelled / refunded frees the slot). Both `BuyEventTicket` and the
`CounterController.CreateSale.event_ticket` branch enforce it; both also reject `quantity > 1` for a
race-entry line and dedupe within the cart.

**Voucher policy.** `ValidateVoucher` enforces ownership (`UserId == userId`), unused
(`RedeemedAt IS NULL`), program active, and program scope vs item kind (`pass` / `event_ticket` /
`any`). Vouchers are *single-unit* — the API rejects `quantity != 1` (pass) or `totalUnits != 1`
(ticket). Voucher and coupon are mutually exclusive. Gift cards stack *on top* of either as a payment
instrument. `MarkRedemptionUsed` uses `WHERE redeemed_at IS NULL` for idempotency.

**Redemption flow.** `RedemptionController` is class-level `[Authorize(Policy=SalesRedeem)]`. `Preview`
hydrates a single token. `Order` returns every row tied to the same `stripe_payment_intent_id` (for
the gate worker's "scan once, redeem many" workflow). `Redeem` flips a single row;
`RedeemBulk` flips a set, with the invariant that every requested row must share the anchor PI so a
leaked purchase id from another order can't be redeemed. Undo (paid ← redeemed) lives on
`ReportsController.SetCheckIn` (not `RedemptionController`), and counts as a check-in toggle on the
Event Riders report.

## Findings

| # | Severity | Title | File / location |
|---|---|---|---|
| 4.1 | High | Spectator waiver never enforced on `event_ticket` tier sale; `BuyEventTicket` only checks `RequiresRiderWaiver` | `webapi/Controllers/PurchaseController.cs:788` |
| 4.2 | High | No janitor for abandoned `pending` purchase rows — they consume capacity / tier inventory forever | `TaskRunner/Program.cs` (whole file), `EventTicketTierRepository.SoldCount`, `PassPurchaseRepository.ActiveSpotsReservedForEvent` |
| 4.3 | Medium | Capacity / tier-inventory check is read-then-insert with no row lock; two concurrent buyers can both pass and oversell | `webapi/Controllers/PurchaseController.cs:265-273` (event capacity), `:662-669` (tier inventory) |
| 4.4 | Medium | `max_uses_per_user` coupon cap is checked once for the whole cart; a multi-line cart can exceed it because the validator runs once and per-row redemptions are written after | `Services/Coupons/CouponValidator.cs:53-58`, `webapi/Controllers/PurchaseController.cs:905-918` |
| 4.5 | Medium | Voucher race: two parallel checkouts using the same `reward_redemption` both pass validation; only the *post-success* `MarkRedemptionUsed` arbitrates and the loser keeps its discount through to the Stripe charge | `webapi/Controllers/PurchaseController.cs:1156-1177`, `Services/Repositories/RewardRepository.cs:153-160`, `webapi/Controllers/PaymentController.cs:487-490` |
| 4.6 | Medium | `RedemptionController.Redeem` + `RedeemBulk` allow `staffId is null` and silently fall through to `UpdateStatus` without recording `redeemed_by_user_id` / `redeemed_at_utc` — audit gap if the JWT ever lacks `UserId` | `webapi/Controllers/RedemptionController.cs:70-81, 275-276, 289-290, 303-304` |
| 4.7 | Medium | Race-number assignment has no uniqueness enforcement — two riders in the same `(event, tier)` can both be assigned `21B` | `webapi/Controllers/ReportsController.cs:192-201`, `Services/Repositories/EventTicketPurchaseRepository.cs:118-125`, `RidePass.Migrator/Scripts/Script0079_EventTicketRaceNumber.sql` |
| 4.8 | Medium | Free-cart fast path skips `applied_reward_redemption_id` and `stripe_payment_intent_id` stamping for vouchered tickets; per-ticket `applied_reward_redemption_id` only goes onto the first row (`q == 0`) and the rest of the cart silently loses its provenance | `webapi/Controllers/PurchaseController.cs:890`, `:1050-1062` |
| 4.9 | Low | `Counter` "no-prior-entry-this-class" check uses `Any(t => t.Tier.Id == tier.Id)` over the loop's own `ticketItems` — only catches an earlier line for the *same* tier, not a later one (loops forward) | `webapi/Controllers/CounterController.cs:276-280` |
| 4.10 | Low | `BuyPass` allows `quantity > 1` to land on a single `pass_purchase` row sharing one redemption token; the implication ("one QR scan redeems N spots") isn't documented and the redemption UI surfaces nothing about N | `webapi/Controllers/PurchaseController.cs:265-272, 391-409`, `webapi/Controllers/RedemptionController.cs:74-75` |
| 4.11 | Low | `BuyEventTicket` is `[AllowAnonymous]` even for `race_entry` tier purchases at events with `RequiresRiderWaiver=false` — a guest with only email + name can register as a racer with no signed waiver, no membership, no emergency contact | `webapi/Controllers/PurchaseController.cs:598`, `:673-687` |
| 4.12 | Low | Coupon "last unit absorbs rounding" math: when no unit got a positive `unitCouponDiscount` (e.g. very-small percent + small subtotal rounds to 0), the validator still recorded a non-zero `application.DiscountCents`; no `coupon_redemption` rows get written but the validator's count-up for "max uses" is unaffected | `webapi/Controllers/PurchaseController.cs:867-878, 905-918` |
| 4.13 | Low | `EventTicketTierController.Delete` rejects delete when `SoldCount > 0` *including* pending rows — combined with 4.2, a never-paid pending purchase blocks the admin from cleaning up a wrongly-created tier | `webapi/Controllers/EventTicketTierController.cs:145-153`, `Services/Repositories/EventTicketTierRepository.cs:98-104` |
| 4.14 | Low | `EventTicketPurchase` entity is missing the `CancellationReason / CancelledAt / CancelledByUserId / RefundNote` properties even though the columns exist and the SQL writes them; `Cancel` and `MarkRefunded` succeed but no read path surfaces those columns on the ticket entity | `Services/Repositories/Data/PaymentData/EventTicketPurchase.cs` (whole file), `Services/Repositories/EventTicketPurchaseRepository.cs:9-20` |
| 4.15 | Low | `BuyPass` re-checks `eventId.HasValue` inside an `if` block right after a guard that already returned on `!eventId.HasValue` — dead branch + an extra Indented level of code | `webapi/Controllers/PurchaseController.cs:232-241` |
| 4.16 | Low | `BuyPass.vue`'s `pay` and `BuyAdmissionFlow.vue`'s `pay` set `paying = true` but `confirmPayment` with `redirect: 'if_required'` can navigate away on bank-redirect flows; the pending row stays `pending` and the UI loses its handle. Loops back into 4.2 | `vueapp/src/views/BuyPass.vue:618-635`, `vueapp/src/components/BuyAdmissionFlow.vue:1306-1330` |
| 4.17 | Low | `RedemptionController.Order` redundantly fetches each redeemer twice (one pass to collect ids, another to resolve names) and runs the resolution before the loop has actually populated `RedeemedByName`; the second pass works but the first is dead code | `webapi/Controllers/RedemptionController.cs:198-239` |

### 4.1 — High — Spectator waiver never enforced on event_ticket tier sale

`BuyEventTicket` gates the waiver by looking at the event's `RequiresRiderWaiver` (and any extra-line
`RequiresWaiver`):

```csharp
if (parentEvent.RequiresRiderWaiver || extrasNeedWaiver) { ... }
```

The event row carries a separate `RequiresSpectatorWaiver` + `SpectatorWaiverId` (Section 3 schema +
`EventRepository.cs:14-15`). The dedicated `SpectatorController` consults those for gate-fee +
extras spectator purchases. But `BuyEventTicket` *also* accepts `spectator_pass` tier purchases —
`BuyAdmissionFlow.vue` passes them all the way through `/Purchase/EventTicket` — and never checks
`RequiresSpectatorWaiver`. So:

- An event with `requires_spectator_waiver=true`, `requires_rider_waiver=false`, and a
  `spectator_pass` tier (e.g. "Adult Spectator") will sell the spectator ticket without any waiver
  signature being captured or referenced on the purchase row.
- The same event sold through the SpectatorController path (gate fees as `event_extras`) *does*
  enforce the waiver.

Result: two parallel "spectator" paths with different waiver enforcement. The tier-based path is the
gap. Either replicate the spectator-waiver check inside `BuyEventTicket` when the cart has any
`spectator_pass` tier, or document a hard rule that spectator-audience sales must use extras + the
SpectatorController, and reject `spectator_pass` tiers in `BuyEventTicket`. Given the FE already
ships spectator_pass tiers through this endpoint, the former is the smaller behavior change.

### 4.2 — High — No janitor for abandoned pending purchase rows

`TaskRunner/Program.cs` runs exactly one job on a 30-minute timer — `MonthlyPayoutDrafter`. There is
no cleanup pass for `pass_purchase` / `event_ticket_purchase` / `event_extra_purchase` rows that
landed in `pending`, got their PaymentIntent created, then never got `payment_intent.succeeded` (user
closed the tab, network blip, bank redirect failure, Stripe element never mounted, etc.).

Why this matters operationally:

- `PassPurchaseRepository.ActiveSpotsReservedForEvent` and
  `EventTicketTierRepository.SoldCount` both count `pending` along with `paid` and `redeemed`. An
  abandoned cart *holds capacity*. A small-capacity race with many abandoned carts can show
  "sold out" on the buy page while having zero actual paying riders.
- `Admin → Purchases` (the `v_recent_sales` read model — see CLAUDE.md) shows the row in `pending`
  state with the rider's name + email forever. Operationally these clutter the dashboard.
- 4.13: even a pending row blocks `EventTicketTierController.Delete` because `SoldCount` is
  non-zero — admins can't clean up a mis-created tier.

The Stripe webhook handler does flip rows to `failed` on `payment_intent.payment_failed`, but only
when Stripe actually fires that event. The common "user navigated away" case never sends a Stripe
event for the abandoned intent at all.

Suggested job: every N minutes, sweep `pending` rows older than (PaymentIntent timeout + slack —
Stripe holds a PI for ~24h). For each, look up the PI via the Stripe SDK; if it's in
`canceled` / `requires_payment_method` past the slack window, flip the row to `failed` (or `expired`,
new status). Idempotency falls out of the unique `(source_kind, source_id)` constraint on
`tenant_ledger`. The job becomes the second entry in `TaskRunner` alongside the payout drafter.

### 4.3 — Medium — Inventory race on event capacity and tier inventory

The capacity / inventory check is a read-then-insert with no row lock or `UPDATE ... WHERE` guard:

```csharp
// Event capacity (BuyPass)
var reserved = await _purchases.ActiveSpotsReservedForEvent(eventId.Value);
var remaining = ev.Capacity.Value - reserved;
if (quantity > remaining) return ...
// ... arbitrary time later ...
var createdDay = await _purchases.Create(purchase);
```

```csharp
// Tier inventory (BuyEventTicket)
if (tier.Inventory.HasValue) {
    var sold = await _tiers.SoldCount(tier.Id);
    if (sold + item.Quantity > tier.Inventory.Value) return ...
}
// ... arbitrary time later ...
var created = await _ticketPurchases.Create(purchase);
```

Two concurrent buyers can both observe `remaining = 1`, both insert, and the event ends up oversold.
The window is small (one HTTP round-trip plus the await chain through extras / coupon validation /
waiver lookup) but real, especially when a popular event drops to its last few spots and two friends
race to buy.

**Realistic blast radius.** The race-class one-per-rider check (`HasActiveRaceEntry`) is also
read-then-insert and shares this window, but the same-rider case can never actually race itself
because a single user can't legitimately fire two parallel checkout requests; the more realistic
worry is two members of the same household both trying. Capacity / inventory is the bigger one
because *unrelated* buyers race each other.

**Mitigations.** Choices, lightest to heaviest:

1. `SELECT ... FOR UPDATE` on the parent `event` row inside an explicit transaction wrapping the
   capacity check + INSERT. Same for `event_ticket_tier`. Cheap, well-understood.
2. Optimistic concurrency: bump a version column on the event / tier on each sale, retry on
   conflict. Better throughput; more code.
3. Push the check into a `BEFORE INSERT` trigger using an aggregate over the existing rows. Strongest
   but DB-specific.

For a starter fix, the first option (`SELECT ... FOR UPDATE`) on the event row in `BuyPass` and the
tier row in `BuyEventTicket` is the right shape for a Dapper/DbUp stack.

### 4.4 — Medium — Coupon `max_uses_per_user` can be exceeded by a multi-line cart

`CouponValidator.ValidateAsync` runs **once** per checkout, looks at `CountUserRedemptions(coupon, user)`,
and if `perUser >= MaxUsesPerUser` rejects the whole cart. But on success, `BuyEventTicket` then
writes a `coupon_redemption` row **per ticket that received a discount**. So:

- A coupon with `max_uses_per_user = 1`, used by a fresh user on a 3-ticket cart, passes validation
  (`perUser = 0 < 1`).
- Three `coupon_redemption` rows get written for the same user from this one cart.
- The user's redemption count is now 3 of 1 allowed.

The "max" is interpreted as max-per-cart by the validator and max-per-row by the recorder, and they
disagree.

Two reasonable fixes:

- Re-interpret `max_uses_per_user` as cart-level. Adjust the validator to record one redemption per
  cart with a sum of discounts. This is a schema-affecting change.
- Keep per-row redemption rows but check `perUser + ticketsInCart > MaxUsesPerUser` in the
  validator (pass the cart size in). One-line change in `CouponValidator.ValidateAsync` plus a new
  caller parameter.

The day-pass path has the same shape — `BuyPass` records a single `coupon_redemption` for the line
(line 411), so it doesn't trip the bug as written, but if a future change splits day-pass rows per
quantity (currently they share one row) it would.

### 4.5 — Medium — Voucher race across two parallel checkouts

`ValidateVoucher` checks `redemption.RedeemedAt is not null` to reject already-used vouchers, but
the actual flip happens in `RewardRepository.MarkRedemptionUsed`:

```csharp
UPDATE reward_redemption
SET redeemed_at = now(), redeemed_on_kind = @kind, redeemed_on_id = @sourceId
WHERE id = @redemptionId AND redeemed_at IS NULL
```

The `WHERE redeemed_at IS NULL` makes the *update* idempotent, but the only callers are the free-cart
fast path (`PurchaseController:521, 1061`) and the post-success webhook
(`PaymentController:487-490`). So:

- Tab A opens checkout with voucher V, gets through validation and onto the Stripe payment step.
- Tab B opens checkout with the same voucher V (e.g. user opened two browser tabs). Validation
  passes — `RedeemedAt` is still null. Tab B creates its purchase row + PaymentIntent + discounted
  charge.
- Tab A confirms first. Webhook fires, `MarkRedemptionUsed` flips `redeemed_at`.
- Tab B confirms second. Webhook fires, `MarkRedemptionUsed`'s `WHERE redeemed_at IS NULL` is now
  false. The update no-ops. But the Stripe charge already went through *at the discounted amount*.

So the voucher *gets recorded* as used once (good) but *two purchases got the discount*. The cost to
the tenant: the second purchase was charged less than full price, and there's no compensating refund
or escalation.

This is the same shape as the inventory race but on a flag instead of a count. Mitigations:

- A `SELECT ... FOR UPDATE` on `reward_redemption` inside the validation block, then flip
  `redeemed_at` *inside the same transaction as the purchase row insert*. Race window closes.
- Lighter: use `UPDATE ... WHERE id = ? AND redeemed_at IS NULL RETURNING ...` at validation time as
  a CAS — if zero rows returned, the voucher is taken; otherwise the row is yours and you commit it
  alongside the purchase. Roll back the flip if the purchase row insert fails. Same shape but no
  explicit transaction handle in the Dapper helper, so this would need a small `IDbHelper.WithTransaction`
  hook.

(Note for the reader: the gift-card path has a related issue — `ApplyToBalance` happens in the
controller before Stripe, so a gift card debits before payment confirms. That's Section 2's
territory.)

### 4.6 — Medium — Redemption flow falls through when staffId is null

`RedemptionController.Redeem` and `RedeemBulk` both look like:

```csharp
if (staffId.HasValue) await _tickets.MarkRedeemed(t.Id, staffId.Value, nowUtc);
else                  await _tickets.UpdateStatus(t.Id, "redeemed");
```

The class is `[Authorize(Policy=SalesRedeem)]` so `staffId` should always be present from the
`UserId` claim. But:

- If the JWT issuer ever emits a token without the `UserId` claim (mis-configuration, super-admin
  impersonation token, etc.), the fallback `UpdateStatus` flips status but never sets
  `redeemed_at_utc` / `redeemed_by_user_id`. The audit log loses the "who scanned this" trail.
- The `Order/Redeem` endpoint is the staff worker's primary tool, and the Event Riders report
  (Section 5 territory) joins on `redeemed_by_user_id` to attribute check-ins. A null id means the
  staff member "disappears" from the report.

Tighten the contract: if `staffId` isn't resolvable from the claim, return `401` (the JWT is
malformed for this policy) rather than silently dropping the audit fields. One-line change in each
of the three branches.

### 4.7 — Medium — Race numbers are not unique per (event, tier)

`Script0079_EventTicketRaceNumber.sql` adds the column + a non-unique index on
`(tier_id, race_number)`. `EventTicketPurchaseRepository.SetRaceNumber` writes whatever staff sends:

```csharp
UPDATE event_ticket_purchase SET race_number = @raceNumber
WHERE id = @id AND tenant_id = @tenantId
```

No check for "is `21B` already used in this tier"? No uniqueness constraint at the DB layer either.

Practical impact: two riders in the same class with the same number is a real day-of problem (the
scoreboard, the timing system, the report — see `ReportsRepository.cs:305`). Today's only safety
catch is whichever staff member is at the laptop noticing.

Fix shape: either a partial unique index (`UNIQUE (tier_id, race_number) WHERE race_number IS NOT NULL`)
plus a 409 from the controller on insert conflict, or a pre-check in `SetRaceNumber` that returns a
"That number is already taken by X" error. The index is the safer of the two because it survives
parallel staff edits.

The audit field for "who set the race number" is also missing — the column exists only as a string;
there's no `race_number_set_by_user_id` / `race_number_set_at_utc` trail. Worth adding alongside the
uniqueness fix; the audit is cheap once the schema is being touched anyway.

### 4.8 — Medium — Free-cart fast path drops voucher provenance and PI id on non-first rows

In `BuyEventTicket`, the per-unit row creation stamps `applied_reward_redemption_id` only on the
first row:

```csharp
AppliedRewardRedemptionId =
    (q == 0 && voucherCheck.percentOff.HasValue)
        ? request.RewardRedemptionId : null,
```

That's deliberate (the voucher applies to a single unit per the validator-side `totalUnits != 1` block,
so the *single* unit gets the provenance). But the free-cart fast path then iterates all created
tickets, flips them all to `paid`, and writes zero-ledger rows for each — and `MarkRedemptionUsed` is
called once against `first.Id`. So:

- If a checkout were ever to bypass the `totalUnits != 1` guard (e.g. via an out-of-band call path
  or a future change) and have a voucher that zeros multiple rows, the second row would lose its
  provenance link entirely.
- More immediately: `stripe_payment_intent_id` is never stamped on free-cart-path rows. Section 2 noted
  that the `Order` redemption endpoint groups rows by PI id; a free-cart purchase with multiple rows
  (e.g. voucher zeros the cart but extras were also in the cart — actually no, the controller drops
  to the Stripe path in that case because extras land in `combinedStripeChargeCents` — OK, but the
  scenario where day-pass + bundled membership are both in the cart and both zero is harder to
  contrive but not impossible to imagine).
  Net effect today: the *practical* free-cart path almost always involves a single row, so the lost
  provenance / lost PI grouping is more "smells brittle" than "actively broken." But the assumption
  `totalUnits == 1` is enforced at line 814, and the per-unit voucher stamping at line 890 reads
  like it expects N rows, so the code is set up for a future where the constraint relaxes. When
  that happens the bug surfaces.

Minor cleanup: stamp `applied_reward_redemption_id` on every row that actually got a discount
(track per-row in the `createdTickets` tuple), and write `stripe_payment_intent_id = NULL`
explicitly (it already is — but document that the `Order` redemption path then can't group these
free rows). Or, document that free-cart rows are always single-row and assert it
(`if (createdTickets.Count > 1 && combinedStripeChargeCents == 0) throw new InvalidOperationException(...)`).

### 4.9 — Low — CounterController in-cart race-entry dedupe only looks at earlier lines

```csharp
if (ticketItems.Any(t => t.Tier.Id == tier.Id))
{
    return new ApiResponses().BadRequestResult($"Riders can only enter '{tier.Name}' once.");
}
```

This runs inside the foreach loop over `request.Items` *before* the current item is added to
`ticketItems`. So if the cart has two entries for the same race-entry tier, the *second* one is
caught and rejected — fine. The earlier one is also caught when it later comes up in the same
order. Actually the check is symmetric on iteration order (whichever entry is processed second
fails), so this is fine in practice.

But: a buyer who lists the same race-entry tier with `quantity=2` in the *first* line hits the
`item.Quantity > 1` guard right above this check (line 271-275). So the "two lines for the same
tier" case is the only way to even reach the duplicate check, and it works. Low-severity nit:
the in-loop `.Any()` is `O(N²)` for N tier lines, fine at counter-cart sizes.

Actual bug-ish behavior to confirm: the equivalent dedupe in `BuyEventTicket` is the `.GroupBy(i => i.TierId)`
at line 636 which silently *sums* duplicate lines into a single line. Then the `Quantity > 1` guard
at line 675 rejects the summed line. So two race-entry lines for the same tier in the FE cart get
rejected with "You can only enter '<tier>' once" — same outcome, different code path. Consistent.

### 4.10 — Low — Day-pass quantity > 1 collapses to one row with one redemption token

```csharp
var purchase = new PassPurchase { ... Quantity = quantity, ... };
```

`PassPurchase.Quantity` is an `int` on the entity; the row has *one* `redemption_token` for all N
spots. The redemption flow flips the single row to `redeemed` on first scan. So:

- A rider buying a day pass with `quantity=2` ("come bring my friend") gets one QR.
- One scan at the gate redeems both spots.

The Section 3 schema review covered the `quantity` column. The functional question is: is this
intentional? The FE allows `quantity > 1` (BuyPass.vue's `maxQuantity` computation), and capacity
math in `ActiveSpotsReservedForEvent` is correct (`SUM(quantity)`), and the receipt math at the
controller level multiplies through. So inventory-wise it works. The redemption-wise UX is "one QR
for N people, one scan redeems all" which is fine for "show this at the gate, gate worker counts
heads" but is bizarre for "everyone has their own phone." The receipt copy in `BuyPass.vue` says
"Show this QR at the gate" (singular) so the implicit promise is the former.

Two productions can clarify: (a) cap the FE quantity at 1 and force users to do separate checkouts
for separate riders; (b) split per-unit rows like `BuyEventTicket` does for tickets. Without one of
these, the design intent is invisible and a user buying 2 spots may legitimately wonder why they
got one code.

### 4.11 — Low — Guest race-entry purchase allowed at events without rider waiver

`BuyEventTicket` is `[AllowAnonymous]`. The race-entry purchase block doesn't require sign-in unless
the event has `RequiresRiderWaiver=true` (line 790-797). A guest can buy a race entry with just an
email + name at any event whose owner left `requires_rider_waiver=false`.

This isn't necessarily wrong — some tracks may want to allow walk-up race entries with no waiver —
but it's worth surfacing to the customer-success team. A tenant config flag (`require_rider_signin =
bool`, separate from waiver) would make the policy explicit. Today the only protection is the
implicit "every race tenant has a waiver" assumption.

Also worth surfacing: `RequireEmergencyContact` is checked for *authenticated* buyers (line 692-699)
but not for *guest* buyers — `request.Email` + `request.Name` are the only required fields. A guest
race-entry checkout at an emergency-contact-required tenant skips the check entirely.

### 4.12 — Low — Zero-effect coupon discount

`CouponValidator.ValidateAsync` rejects when `discount <= 0` after computation (line 66) — fine in
isolation. But in `BuyEventTicket`, the per-unit split:

```csharp
unitCouponDiscount = totalUnitsRemaining == 1
    ? couponRemaining
    : (int)((long)couponApp.DiscountCents * tier.PriceCents / couponSubtotalDenom);
```

For mixed-price carts where one tier has a much higher price than another, the integer math can
round a per-unit discount to 0. That row gets a `unitCouponDiscount = 0`, the `.Where(t => t.couponDiscountCents > 0)`
filter at line 907 then skips writing a `coupon_redemption` row for it, and the rest of the cart
silently picks up the rounding through `couponRemaining`. Net effect: the cart-total discount is
still right (last unit absorbs), but the per-row attribution is uneven. Low impact unless a tenant
runs per-row reporting on coupons. Worth a comment in the loop explaining the asymmetry; the math
itself is fine.

### 4.13 — Low — Pending rows block tier deletion

`EventTicketTierController.Delete`:

```csharp
var sold = await _tiers.SoldCount(id);
if (sold > 0)
    return new ApiResponses().BadRequestResult("This tier has purchases and cannot be deleted. Set inactive instead.");
```

`SoldCount` counts `pending|paid|redeemed`. A pending row created by an abandoned checkout (4.2)
blocks the admin from deleting a mis-created tier. The workaround ("set inactive") is fine but the
message implies *purchases* in the customer sense; pending-only is purely a checkout artifact and
shouldn't block. Either:

- Restrict the check to `paid|redeemed` (cancelled / refunded / failed / pending all allow delete).
- Or simply force-flip orphan pending rows to `failed` as part of a janitor (4.2) which lets the
  existing message stay accurate.

### 4.14 — Low — EventTicketPurchase entity is missing cancellation properties

`EventTicketPurchaseRepository.Cancel` writes `cancellation_reason`, `cancelled_at`, and
`cancelled_by_user_id`. `MarkRefunded` writes `refund_note`. The repo's `Columns` SELECT list at
line 9-20 does *not* select those columns, and the entity (`EventTicketPurchase.cs`) does *not*
declare those properties. So:

- A cancelled ticket has the data in the DB but nothing in `EventTicketPurchase` reflects it. Any
  caller reading a ticket via `GetById` / `GetByStripePaymentIntentId` / `ListByStripePaymentIntentId`
  gets a snapshot with the audit silently missing.
- `EventTicketPurchaseWithContext` (line 32-44) similarly lacks these.

The `pass_purchase` equivalent (`PassPurchase.cs:22-25`) has them all and the repo SELECT includes
them. Either bring `EventTicketPurchase` to parity or document explicitly that ticket cancellations
are write-only-via-SQL on this entity (which is bad). The parity fix is one entity + one column
list update.

### 4.15 — Low — Redundant inner `if (eventId.HasValue)` block in BuyPass

```csharp
if (!eventId.HasValue)
{
    return new ApiResponses().BadRequestResult("Day passes must be tied to an event...");
}
...
if (eventId.HasValue)
{
    var ev = await _events.GetById(eventId.Value, _tenantContext.TenantId);
    ...
}
```

After the early return, `eventId.HasValue` is provably true and the second `if` adds a level of
indentation for nothing. Cosmetic but obscures the actual control flow. Drop the inner `if`.

### 4.16 — Low — Stripe `confirmPayment` can navigate away mid-checkout

Both `BuyPass.vue` and `BuyAdmissionFlow.vue` use:

```js
const { error } = await stripe.confirmPayment({
    elements,
    confirmParams: { return_url: ... },
    redirect: 'if_required',
})
```

`redirect: 'if_required'` is correct for the common card flow, but for 3DS / bank-redirect / wallet
flows Stripe *does* navigate away. The `return_url` lands on `/User/MyPasses` (or the current URL
for spectators) and the user expects to find their purchase — but the row stays in `pending` until
the webhook lands. The FE has no logic to poll the pending row, surface "Payment processing", or
recover if the webhook is delayed (or if the user lost the original tab). This compounds 4.2:
- Webhook arrives → row flips to `paid` → user finds the QR at `/User/MyPasses` whenever they next
  visit.
- Webhook never arrives → row stays `pending` forever → janitor would have caught it.

Low severity because Stripe's webhook delivery is reliable in practice. Worth a comment noting the
implicit dependency on the webhook to "close out" the checkout, and an MVP "looking up your
purchase…" affordance on `/User/MyPasses` for any rider whose latest row is `pending`.

### 4.17 — Low — Redundant work in RedemptionController.Order

The block at line 198-239 collects `redeemerIds`, fetches each user once, then loops again over the
response items and fetches each user *again* via `staffById`. The first loop's only effect is to
populate `redeemerIds`; the second loop re-fetches with the same `redeemerIds` set. The first
fetch loop's `foreach (var item in resp.Items)` body is a comment-only block that explicitly says
"We don't have the user-id on the response right here; fill via a second pass." So that loop does
literally nothing besides populating an unused `staffById`-like dictionary, then gets re-done. Dead
code. Trim the first pass; keep only the second.

## Patterns worth replicating

- **One redemption token per unit for tickets**, paired with **`v_recent_sales` as the cross-kind
  read model**. The pattern of "row per unit, share a PaymentIntent" cleanly composes with extras /
  bundled membership / coupon-discount per row, and the `Order/Redeem` flow drops out of it for
  free. Apply this to day passes too if 4.10 is fixed.

- **`HasActiveRaceEntry` matching by user_id OR lowercased email**, with `status IN (pending, paid,
  redeemed)` so cancelled / refunded frees the slot. Reusable as a template for any other
  one-per-rider invariant (e.g. season-pass single-rider, future membership-tier-uniqueness).

- **Tier "kind" discriminator with `race_entry` vs `spectator_pass`** keeps the schema additive (one
  table, two flavors). The CounterController + BuyEventTicket sharing the discriminator means a
  third audience could be added (e.g. `pit_pass`) with one tier-row change plus a switch on the
  enforcement branches.

- **`InsertZeroLedger` with `catch Postgres 23505`** — small but the idempotency-on-unique-violation
  pattern is right and is also used in the payment webhook (Section 2) and in `CounterController`.
  Worth lifting into a shared helper so all the call sites stay consistent.

- **`BuyAdmissionFlow.vue`'s state machine** — explicit `StepKey` enum, derived `stepperItems` based
  on cart shape (`extrasNeeded`, `isRaceMode`, `waiverNeeded`), per-stepper-item gate computeds.
  Trivial to add steps. Repeatable for any flow that has conditional middle steps.

- **Centralised `CheckMembershipGate(userId, gateOn)`** taking a flag from tenant config so the gate
  can be turned off without touching the call sites. Replicated for the rider-audience gate and the
  spectator-audience gate inside the same controller. The bundled-membership escape hatch
  (`addMembership=true` → skip gate → mint a membership purchase in the same PI) is a nice pattern
  for "you must have X to buy Y" rules where "buy X in the same cart" is a sane alternative.

## Open questions

1. Is day-pass `quantity > 1` (one QR for N people, one scan redeems all) the intended UX or a
   collateral of the schema? See 4.10. If "yes", document it in `BuyPass.vue`'s success view.

2. Should spectator-audience tier sales (kind = `spectator_pass` in `event_ticket_tier`) be supported
   via `BuyEventTicket` at all, or are those reserved for the SpectatorController extras path? Both
   exist in the codebase and have different waiver enforcement — 4.1. The FE already routes spectator
   tier purchases through `BuyAdmissionFlow.vue` → `/Purchase/EventTicket`, so the answer is
   probably "yes, supported, but missing the waiver check." Confirm.

3. Race numbers: who is the source of truth — `user.race_number` (the rider's preferred number) or
   `event_ticket_purchase.race_number` (per-event override)? Reports fall back from the latter to
   the former. If two riders both have profile-level `race_number=21B` and neither has a per-event
   override, the report will show both with `21B`. Same root cause as 4.7 but at the profile layer.
   Is the policy "tracks de-dupe by hand" or "we should enforce per-event uniqueness"?

4. Are vouchers ever expected to be used parallel-tab? 4.5 assumes they aren't and the race is
   theoretical. If support reps have seen "voucher worked twice somehow" reports, that's the
   smoking gun.

5. Is there a target time-to-stale for `pending` purchase rows? Stripe's PI default lifetime is ~24h
   but tenants may want shorter (an event tomorrow shouldn't have 12h-old pending rows blocking
   capacity).

## Coverage notes

- All eight files in the explicit scope read end-to-end.
- All four entity classes verified against their repo SELECT lists + INSERT lists.
- CounterController included as required for parity comparison; only the `event_ticket` branch was
  read in depth.
- `vueapp/src/views/BuyTicket.vue` is a thin shell (46 lines) over `BuyAdmissionFlow.vue`; the
  component is what actually matters and was read end-to-end (~1370 lines).
- BuySpectator.vue read end-to-end; the spectator flow ships through `SpectatorService` → the
  `SpectatorController` (not `BuyEventTicket`), so the cross-comparison surfaced 4.1.
- `TicketService.ts` + `PassService.ts` cross-checked against the actual controller request shapes.
- `TaskRunner/Program.cs` read to confirm 4.2 — no janitor exists, payout drafter is the only job.
- Payment webhook lookups limited to lines 460-510 of `PaymentController.cs` (where
  `MarkRedemptionUsed` lives), because deeper webhook coverage is Section 2.
- Section 1 (tenancy), Section 2 (payment / webhook / ledger), and Section 3 (schema) findings
  intentionally not re-flagged; one finding (4.7 race numbers) does touch the schema with a
  recommended new constraint, called out explicitly there.
