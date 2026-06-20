# QA Test Plan: Events, Pricing Ladders & Registration

> Scope: event CRUD + duplication, ticket tiers (race entry / gate fee), dynamic price ladders (price steps), online checkout, group/multi-rider registration, and per-rider uniqueness. Last updated: 2026-06-20.

## Surface map
- **Admin:** `EventController` (CRUD, `Duplicate`), `EventTicketTierController` (tier + price-step CRUD, reorder, `GetAllForAdmin`).
- **User:** `EventTicketTierController.GetForEvent` (public, collapses a ladder to its active step), `PurchaseController.BuyEventTicket`, `PurchaseController.CompleteTicketRegistration`, `PurchaseController.GetRegistration`.
- **Pricing logic:** `Services/Pricing/PriceStepResolver.cs` (active step = highest-priced fired step), `EventTicketTierRepository.GroupSoldCount`.
- **Uniqueness:** `EventTicketPurchaseRepository.FindRaceClassConflict` (name+birthdate, race number, scoped to the class = `ladder_group ?? tier_id`).
- **Frontend:** `TicketTiersList.vue` (admin step editor), `EventCheckout.vue` (buyer), `TicketService.ts`.
- **Migrations:** `Script0124_EventPriceSteps.sql`.

## Concepts under test
- A **price ladder** is a set of `event_ticket_tier` rows sharing a `ladder_group`. A step "fires" if it is the base step (no triggers) OR a trigger is met: `min_sold` (quantity), `effective_days_before` (relative date), `effective_at_utc` (absolute date). The **active** step is the highest-priced fired step ("max-wins").
- The whole order is honored at the active step's price (the cart references the active tier id and freezes it). Order size is bounded only by `event.capacity`.
- A **class** spans every step of a ladder; uniqueness/dedup keys on `ladder_group ?? tier_id`.

## Preconditions / test data
- A tenant with at least one scheduled, published race event.
- A ladder class with three steps in one `ladder_group`: e.g., **$50** base (no trigger), **$65** at `min_sold = 10`, **$75** at a date trigger ~14 days out. `event.capacity = 30`.
- A separate standalone (non-ladder) race-entry tier and a required rider gate fee, for regression coverage.
- Two rider accounts plus one guest email.

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| EA1 [NN] | Create a quantity ladder | Add 3 steps sharing a price group; set prices ascending and `min_sold` 0/10/20 | Saves; list shows the group + per-step trigger captions. Reopen confirms persistence. |
| EA2 [NN] | Create a date ladder | 3 steps with `days-before` 60/30/14 (or absolute dates) | Saves and reopens with the date triggers intact. |
| EA3 [NN] | Duplicate a ladder event | Duplicate an event whose class has quantity + date steps | Clone has all steps copied; **absolute** date triggers shifted by the duplicate offset (+7d), **relative** `days-before` unchanged, sold counts reset to 0. |
| EA4 [NN] | Misconfigured ladder (no base step) | Create a ladder where every step has a trigger and none has fired | Document behavior: buy page surfaces a fallback (cheapest) step, but checkout rejects with "isn't available right now." Confirms the known gap (server-side base-step validation not yet enforced). |
| EA5 [R] | Standard tier CRUD | Create/edit/delete/reorder a non-ladder tier | Works unchanged. Delete blocked if the tier has purchases ("set inactive instead"). |
| EA6 [R] | Gate fee + bundled coupon | Configure a required rider gate fee and a race-entry tier with bundled coupons | Saves; bundled-coupon all-or-nothing validation enforced (count requires kind/value/scope). |
| EA7 [R] | Event lifecycle | Cancel an event with purchases; attempt delete | Cancel works; delete blocked when purchases exist. |
| EA8 | Reports reflect registration | After group registrations, open the rider/sales report | Each rider's name + race number appears per entry. |

---

## User (buy + register)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| EU1 [NN] | Active step display | Open the buy page for the ladder class | Shows only the active step + hint ("then $65" / "rises to $75 on <date>"). Sold-out cheaper step and not-yet-fired step are hidden. |
| EU2 [NN] | Quantity step-up | Cross the `min_sold` threshold (buy enough), reload | Price steps up to the next tier; `remainingToCapacity` decreases. |
| EU3 [NN] | Honor-whole-order | Only 2 spots remain to a step boundary; order 3 | All 3 charged at the current (low) step price; next buyer sees the higher step. Capacity is the only hard cap. |
| EU4 [NN] | price_changed re-confirm | Load at the low price; cross a trigger (date ~1 min out, or quantity in a 2nd session); submit | API returns 409 `price_changed`; the checkout surfaces the message; refresh shows the new price; re-submit at the new price succeeds. |
| EU5 [NN] | Capacity sell-out | Sell the class to `event.capacity` | Buy page shows sold out; quantity beyond remaining rejected ("Only N spot(s) left"). |
| EU6 [NN] | Group registration | One order: rider gate + 2 race entries for 2 different riders; complete "finish registration" | Each rider captured; both entries complete; resume flow (`GetRegistration`) works mid-way. |
| EU7 [NN] | Uniqueness, person | Register rider A in class X; in another order register a rider with the same name + birthdate in class X | Second rejected with the person-conflict message. |
| EU8 [NN] | Uniqueness, number | Two entries in class X with the same race number | Second rejected with the number-conflict message. |
| EU9 [NN] | Uniqueness spans steps | Enter class X at the $50 step, then attempt the $65 step (same `ladder_group`) for the same rider | Rejected (class-scoped, spans steps). |
| EU10 [NN] | Birthdate-absent person match | Two distinct riders with the same name and **no** birthdate in class X | Known trade-off: the second is treated as a duplicate. Confirm whether to require birthdate for race entries. |
| EU11 [R] | Standard single-tier purchase | Buy a non-ladder tier as guest and as an authed rider | Works unchanged. |
| EU12 [R] | Required rider gate enforcement | Buy a race entry without the required rider gate fee | Rejected until the gate fee is added. |
| EU13 [R] | Waiver capture | Purchase requiring a rider waiver; minor rider needs parent/guardian | Waiver enforced; minor path requires parent name + phone. (See waivers plan for depth.) |
| EU14 [R] | Refund / cancel | Cancel a paid ticket (self or admin) | Refund issued per policy; waitlist promotion fires (see waitlist plan). |

---

## Known risks / watch-items
- **No server-side base-step validation** for a ladder (EA4): a misconfigured class can show a buyable price the checkout then refuses. Recommend validating "exactly one base step + ascending prices" in the tier upsert and the admin editor.
- **Birthdate-optional person match** (EU10): name-only collisions when birthdate is absent reject distinct people.
- **`event.capacity` with mixed tiers:** `GroupSoldCount` caps only that ladder group's own sales against `event.capacity`; spectators/other groups don't count toward it and each group caps independently. Confirm intent for events that mix a ladder with spectator/gate tiers.
- **Concurrency:** `BuyEventTicket` + `CompleteTicketRegistration` are read-then-insert (no advisory lock on the online ladder path); a rare double-submit could land an extra sale at the low step or slip a duplicate rider.
- See the **POS / Counter** plan for the counter's ladder handling and the **Waitlist** plan for sold-out flow.
