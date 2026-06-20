# Dynamic event pricing + group registration

Two related features, sequenced into two phases. Phase 1 (price steps) is independent
and ships first. Phase 2 (group registration + rider uniqueness) is a coupled bundle.

---

# Phase 1: Dynamic price steps

A ticket type can be **Standard** (one fixed price, today's behavior) or **Stepped**: a set
of *steps*, each with a price and zero or more triggers. The live price is the
**highest-priced step whose trigger has fired**. Triggers:

- **Quantity** (`min_sold`): fires when cumulative sold for the group reaches a threshold.
- **Date** (`effective_days_before`, relative to event start; or absolute `effective_at_utc`):
  fires when the event is within N days / after a timestamp.

A step with no triggers is the base (starting) price, always fired. This one rule yields
scarcity ladders, date-based early-bird, and "max wins" combinations of both.

## Data model (one additive migration)

Reuse `event_ticket_tier` (each step keeps the existing purchase / QR / redemption /
reporting machinery):

```sql
ALTER TABLE event_ticket_tier
  ADD COLUMN ladder_group          text        NULL,   -- NULL = standalone tier (unchanged)
  ADD COLUMN min_sold              int         NULL,   -- quantity trigger (group cumulative)
  ADD COLUMN effective_days_before int        NULL,   -- date trigger, relative to event.starts_at
  ADD COLUMN effective_at_utc      timestamptz NULL;   -- date trigger, absolute (optional)
```

- Steps of one ticket share a `ladder_group`; `price_cents`/`sort_order`/`name` already exist.
- Base step has all three trigger columns NULL.
- `event.capacity` is the hard sell-out cap (steps drive price, capacity drives availability).
- All existing rows have `ladder_group = NULL` -> zero behavior change.

## Active-step resolution

`GetActivePriceStep(eventId, ladderGroup, tenantId)`:

```
group_sold = COUNT(event_ticket_purchase WHERE tier_id IN <group> AND status IN ('pending','paid','redeemed'))

fired(step) =
     (min_sold IS NULL AND effective_days_before IS NULL AND effective_at_utc IS NULL)        -- base
  OR (min_sold IS NOT NULL AND group_sold >= min_sold)
  OR (effective_days_before IS NOT NULL AND now() >= event.starts_at - (effective_days_before||' days')::interval)
  OR (effective_at_utc IS NOT NULL AND now() >= effective_at_utc)

active   = fired step with MAX(price_cents)
sold_out = group_sold >= event.capacity
```

Date math uses the event start in the **tenant timezone**. The method also returns the next
change (next price + its trigger) for messaging.

## Public API + display

Collapse each `ladder_group` to its active step plus `remainingToCapacity`, `nextPriceCents`,
`nextChange { kind: 'sold'|'date', soldThreshold?, changesAtUtc? }`. UI: "Only 4 left at $50,
then $65" and/or "Goes up to $65 on Jul 20 (in 3 days)." Buyers can only add the active step.

## Checkout (`PurchaseController.BuyEventTicket`)

1. `IDbHelper.AcquireAdvisoryLock` keyed on the `ladder_group`.
2. Re-resolve the active step + `group_sold` vs `event.capacity`.
3. If the requested step is no longer active (sales or a date rollover moved it since page
   load), return a structured **`price_changed`** response with the new step + price; the
   client re-confirms. Never silently charge more. Covers both triggers.
4. **Honor the active-step price for the ENTIRE order.** Crossing a `min_sold` threshold
   partway through an order does not bump that order's tickets; it only affects the next
   order. Every ticket in the order freezes `amount_cents` at the active step's price.
5. **Order size is bounded only by remaining `event.capacity`** (no separate per-order cap).
   Known, accepted tradeoff: a single large order can buy all remaining spots at the current
   low price and the ladder won't climb within that order.

## Behavior decisions (settled)

- **Capacity source:** `event.capacity` (hard cap; only order-size limit).
- **Combine rule:** max-wins (step fires on either trigger; charge highest fired step).
- **Quantity overflow:** honor active-step price for the whole order (above).
- **Step-down:** quantity steps MAY step back down when spots free up (refund/abandon), since
  `SoldCount` counts pending and the reconciler releases abandoned holds. Date steps never
  reverse. (No high-water-mark lock in v1.)

## Cross-cutting (mostly free)

- Price freeze: `amount_cents` already records the charged price -> refunds/reporting unaffected.
- Holds: `SoldCount` counts pending, so in-flight checkouts hold a spot; the pending reconciler
  frees abandoned ones.
- Coupons apply to the resolved step price. Waitlist unchanged (triggers at `event.capacity`).
- Reporting: each step is a tier (works today); group by `ladder_group` for ticket-type rollups.

## Admin UX

Per ticket type: **Pricing: Standard | Price steps**. Steps editor = ordered rows of
`{ price, trigger }` where trigger is *Starting price* (base) / *After N sold* / *N days before
event* / *On date*. Save assigns a shared `ladder_group` + `sort_order`. Validate: exactly one
base step, ascending prices, `event.capacity` set for quantity ladders. Preview:
"$50 -> $65 (10 sold) -> $75 (2 wks before) - cap 30".

## Phase 1 build list

1. Migration (4 nullable columns).
2. `GetActivePriceStep` + public projection.
3. Advisory-locked active-step re-check + `price_changed` response + whole-order-at-active-price
   in `BuyEventTicket`.
4. Admin steps editor + Standard/Stepped toggle + validation.
5. Frontend: active-step display, scarcity/countdown copy, re-confirm prompt on `price_changed`.

---

# Phase 2: Group registration + rider uniqueness (coupled bundle)

Enables one account to register multiple riders for the same class, while guaranteeing a rider
can't enter a class twice. These ship together because removing the old buyer-based check
before the new rider-based check exists would leave a window with no uniqueness at all.

## Today vs target

- Today: `event_ticket_purchase` effectively assumes **buyer = rider** (stores `purchaser_*`
  and one `race_number`); `HasActiveRaceEntry` blocks duplicates by *purchaser* user/email,
  which wrongly stops a parent registering several kids.
- Target: each entry carries **its own rider identity**, and uniqueness is enforced on the
  rider, not the buyer.

## Per-ticket rider capture

Capture per entry at finish-registration: rider first name, last name, birthdate, race number
(birthdate is also needed for the minor-waiver path). Storage: add rider fields to
`event_ticket_purchase` (or a per-entry registration row); `race_number` already exists there.

## Uniqueness: TWO rules, keyed on the class (= ladder group)

"Class" spans all price steps, so key on `ladder_group` (or the tier id when standalone), NOT
`tier_id`, or a rider could enter the $50 step and the $65 step of the same class.

Among active entries (`status IN ('pending','paid','redeemed')`) in a class:
1. `(class, lower(first_name), lower(last_name), birthdate)` unique - the person.
2. `(class, race_number)` unique where number present - the racing rule.

Enforced at **registration** (not checkout - deferred entries have no rider yet), inside an
advisory lock on the class. Optional DB partial-unique-index backstop once a stable `class_key`
column exists.

## Checkout change

Remove the buyer-based `HasActiveRaceEntry` guard from `BuyEventTicket`. Capacity is the only
checkout-time limit (per Phase 1). This is what unblocks multi-rider orders.

## Phase 2 build list

1. Per-ticket rider fields (migration) + finish-registration UI to collect each rider.
2. `class_key` derivation (ladder_group or tier) for uniqueness scope.
3. Registration-time uniqueness check (person + number), advisory-locked, with clear errors.
4. Remove `HasActiveRaceEntry` from checkout (ships in the same release as #1-#3).
5. Admin/exports: show per-rider identity (already partly there via `race_number`).
