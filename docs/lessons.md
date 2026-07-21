# Lessons, clinics, and training camps

Design doc for turning coached sessions from "an ordinary event that happens to have coaches
attached" into a product MX tracks can actually sell. Same format as `docs/bike-shop.md`: what
exists, what the market does, what we build, in what order, and what we deliberately are not
building.

Status: **steps 1 through 7 BUILT** (2026-07-18). Written and revised the same day after the tenant
confirmed the actual shape (see 2.1). Only step 8 (inclusions / bundles) remains, and the doc's
own advice is to leave it until a tenant needs it.

Shipped so far: `event.capacity` actually enforced (it never was for plain tiers); training groups
on ticket tiers with an OPTIONAL coach, ability and equipment bands, and an optional per-group
window; coach capacity as a second cap; lesson-aware copy; party pricing; per-day attendance so
multi-day camps can be checked in each morning; and a per-day roster. Migrations 0201 to 0203.
Customers never choose a coach, by design (see 2.2).


## 1. Where we are today

A lesson is an `event` whose `event_type.code = 'lesson'`. There is no group, no skill level, no
seat model, and no instructor involvement at booking time. Concretely:

* **Sold as ordinary tickets.** A lesson has no `race_entry` tier, so it sells through the rider
  gate-fee group. The customer books what the UI calls a "Riding Pass" (`EventCheckout.vue:592`)
  under a page header reading "Buy Entry" (`EventCheckout.vue:5`). Quantity is a plain stepper.
* **Discovery is generic.** Hidden on apex (`Events.vue:265` excludes `lesson` and
  `private_booking`) but mixed in with races and practices on the tenant's own events list
  (`Events.vue:513`).
* **Instructors are admin-only.** Assigned in `EventDialog.vue:202`, stored in `event_instructor`
  (Script0177), carried on the public response (`EventController.cs:663`), typed in
  `EventService.ts`, then never rendered by any customer-facing component. The coach is invisible
  at booking time.
* **Instructor availability never gates a sale.** `CheckInstructorConflicts`
  (`EventController.cs:355`) runs only when an admin saves an event. Nothing consults it during
  checkout, and nothing models how many students a coach can take.
* **`event.capacity` is not enforced.** The check at `PurchaseController.cs:352` sits inside the
  `foreach` over price-ladder groups opened at `:328`, which filters to `ladder_group IS NOT NULL`.
  A plain tier never enters the loop. Same shape at `PurchaseController.cs:1566` and
  `CounterController.cs:313` / `:642`. **Live oversell bug on all event types.**
* **`event_ticket_tier.inventory` IS enforced** (`PurchaseController.cs:265` pre-check, `:634`
  locked recheck). This matters: per-group caps already work today if an admin sets them. It is
  the event-level cap that silently does nothing.
* **The bike add-on works.** Since Script0200 it books a `shop_rental` against the shop catalog,
  time-scoped to the lesson window, fee on the lesson's PaymentIntent, deposit as a separate hold.
* **Camping already exists.** `event_extra_product` seeds a `camping` product for every tenant
  (Script0055), with per-event inventory on `event_extra_eligibility` because "different events at
  the same track have different physical capacities." Parking and pit-vehicle too.
* **Rider details already captured.** `event_ticket_purchase` carries `bike text` and `race_number`
  (Scripts 0065 / 0079 / 0115), collected in the post-payment registration step.
* **Redemption is one-shot.** `MarkRedeemed` flips `status = 'redeemed'` with a single
  `redeemed_at_utc` (`EventTicketPurchaseRepository.cs:330`). There is no per-day attendance
  record. This is the blocker for multi-day camps.


## 2. What we are actually building for

### 2.1 The tenant's answer (2026-07-18)

MX tracks run **scheduled group lessons and training classes**, segmented by **skill level** and
**bike size**. Length varies across the whole range: a **half-day clinic**, a full day, or a
**multi-day training camp package**, the longer ones often **bundled with camping**. They do not
sell hourly private lessons off a coach's calendar.

The useful consequence: one model covers the entire range. A coached session is an event with a
start and an end. A half-day clinic is a 4-hour window, a camp is a 3-day window, and nothing in
the design branches on which. Only two things key off duration at all: per-group staggering inside
a single day (3.1), and per-day check-in once the event spans more than one date (3.3). Everything
else, groups, coaches, caps, camping, bikes, pricing, is duration-agnostic.

This kills the largest piece of the previous draft. Open-availability booking (instructor working
hours, slot generation, a separate `lesson_product` / `lesson_booking` purchase table) is cut
entirely. Everything below fits the fixed-schedule shape we already have.

### 2.2 MTB resorts (the other tenant type)

RidePass serves `mountain_bike` tenants too, so the model has to hold for bike parks. Checked
Deer Valley, Mountain Creek, and Trestle (2026-07-18). The tier-as-group model holds, and MTB
actually validates the skill dimension harder than MX does. Three things it adds:

**Ability zones are the primary segmentation, and the vocabulary differs.** Deer Valley runs five
zones using trail-difficulty symbols: Beginner (green circle), Advanced Beginner and Intermediate
(blue square), Advanced and Expert (black diamond). Group clinics are "organized by age and
ability level." MX segments by skill plus displacement, MTB by ability zone plus bike type. So
`skill_level` and the equipment band must be **tenant-configurable free text, not a shared enum**,
which is why 3.1 calls the second field `equipment_label` rather than a bike size. This answers
open question 3.

**Party pricing has three shapes in the wild, and one model covers all of them.**
* Trestle: flat price for up to 3 riders, "no additional fee for adding on participants"
* Mountain Creek: $229.99 for a 2-hour private including lift, "+$129.99 per 2 hours to add a friend"
* Deer Valley: private lessons for 1-5 guests, priced per person

Generalize with three columns on the tier: `party_size_included` (how many the base price covers),
`party_price_cents` for each rider beyond that, and `party_size_max`. Trestle is included=3 with
additional=0; Mountain Creek is included=1 with additional=12999; Deer Valley is included=1 with
additional equal to base. This was in the first draft as a private-lesson concern and I had
deprioritized it after the MX answer. MTB puts it back.

**Implementation note (revised during build, 2026-07-18).** The obvious reading of "one price covers
3 riders" is one ticket row covering 3 people. Do NOT do that: event capacity counting, the check-in
roster, post-payment registration, and per-rider waivers all rest on one-ticket-one-rider, and
breaking that invariant would ripple through every one of them. Instead keep a ticket per rider and
vary the PRICE by position within the purchase: position 0 pays the base, positions 1 through
included-1 are free, and anything beyond pays `party_price_cents` (falling back to the base when
null). Totals are identical for all three market shapes, defaults reproduce today's per-person
pricing exactly, and nothing downstream changes. The rule lives in `Services/Pricing/PartyPricing.cs`
as a pure class, mirroring `PriceStepResolver`.

**Lift tickets and included equipment.** Deer Valley: "Lift tickets are required for every bike
participant" and "Bike rental equipment is not included with any lesson unless otherwise noted."
The required-lift-ticket case is already solved: that is a **required rider gate fee**, which
`event_ticket_tier` has supported since Script0117 (kind `gate_fee`, audience `rider`, required
flag). The harder case is the packages that bundle: Mountain Creek's "Experience Downhill Package"
is $149.99 for bike rental plus trail access plus protective equipment plus instruction, and Deer
Valley's Intro Packages include lift access and equipment. That is genuine bundle pricing, and it
is the same requirement MX camps raised with camping. See 3.5.

Two smaller MTB behaviors worth recording but not building yet: a **minimum to run** ("a minimum of
two participants is required per ability zone to hold the group clinic, or it converts to a
two-hour private lesson"), and **on-site upgrades** (Mountain Creek sells a $59.99 full-day upgrade
first-come at the counter). Also note Deer Valley's "Custom: minimum two hours, instructor
scheduled" at $116/hour: even the hourly case is arranged offline, not self-served off a calendar,
which further supports cutting slot generation.

### 2.3 Market research (2026-07-18)

Researched ski schools, bike parks, and the platforms underneath them. Two findings survive the
narrowing and still shape the design.

**Customers do not pick their instructor.** Across ski schools and bike parks, coach assignment is
an operator decision. Ski school directors place riders into the right group; software matches on
skill and language and load-balances peak times, but that is a staff dashboard. Trestle Bike Park
books first and matches after. So we show the coach and assign staff-side, and never build a
customer-facing coach picker. What matters is that coach time and coach capacity constrain the
sale.

**Group lessons are segmented by ability and grouped under one coach.** Ski group lessons run 6 to
10 students of similar ability in a 3-hour block. That is exactly the MX shape: a group has a
coach, a cap, an ability band, and a time window.

**Resources are the underlying abstraction.** FareHarbor generalizes equipment, vehicles, and staff
into resources with limited quantity; booking one closes the others. RidePass already implements
this for bikes (`shop_rental`, half-open window overlap). Instructors are the same shape.

Sources: Bloowatch, Roverd (ski school software); SkiBro, PeakRankings (group vs private); Trestle
Bike Park, Crank It Up MTB (bike park lesson products); Mindbody (classes vs appointments);
FareHarbor, Checkfront (resources).


## 3. Design

Guiding principle: **a training group is a ticket tier.** MX race classes are already segmented by
skill and displacement ("250C Novice", "85cc 12-15") and are already modeled as `race_entry` tiers
with per-tier inventory and price. A training class is the same object with a coach attached. This
means no new purchase table, no new checkout, no new ledger source kind, and no `v_recent_sales`
work.

### 3.1 Groups: extend the tier

```sql
ALTER TABLE event_ticket_tier
    ADD COLUMN instructor_id         uuid NULL REFERENCES instructor(id) ON DELETE SET NULL,
    -- Free text, tenant-configurable. MX: 'Beginner' / 'Novice' / 'Intermediate'.
    -- MTB: 'Green Circle' / 'Blue Square' / 'Black Diamond' ability zones.
    ADD COLUMN skill_level           text NULL,
    -- Equipment band. MX: '50cc' | '65cc' | '85cc' | '250F'. MTB: 'Trail' | 'Downhill' | 'E-bike'.
    ADD COLUMN equipment_label       text NULL,
    -- Group runs at its own time; NULL = inherit the event's window.
    ADD COLUMN starts_at             timestamptz NULL,
    ADD COLUMN ends_at               timestamptz NULL,
    -- Party pricing. Base price covers `party_size_included` riders; each rider beyond that costs
    -- `party_price_cents`, up to `party_size_max`. Defaults reproduce today's per-head behavior.
    ADD COLUMN party_size_included   int NOT NULL DEFAULT 1,
    ADD COLUMN party_price_cents     int NULL,
    ADD COLUMN party_size_max        int NULL;
```

* **Capacity per group** is the existing `tier.inventory`. Already enforced, no new code.
* **Coach per group** is `instructor_id`. `event_instructor` stays as the event-level roster (who
  is working this clinic at all); the tier points at which one runs that group.
* **Skill and equipment** are structured so the customer can filter and self-select, and so a coach
  roster can be printed. The tier `name` remains the human label ("Beginner 50-65cc", "Blue Square
  Downhill"). Both fields are free text because the vocabulary is tenant-specific (2.2); offer the
  tenant a seeded picklist per tenant type rather than a database enum.
* **Party pricing** covers the MTB private shape without a separate product: a tier with
  `party_size_included = 3` and `party_price_cents = 0` is Trestle's "up to 3, one price", while
  `included = 1` with a nonzero `party_price_cents` is Mountain Creek's add-a-friend. Leaving the
  defaults reproduces today's per-head pricing exactly, so no existing tier changes behavior.
* **Per-group times** let a clinic stagger groups (beginners 9-12, intermediate 1-4) inside one
  event without creating separate events. NULL inherits the event window, so nothing changes for
  existing tiers.

### 3.2 Instructors as a constrained resource

Two rules, both cheap:

1. **Conflict check moves to checkout.** `CheckInstructorConflicts` already knows how to detect a
   coach double-booked across overlapping events. Call the same logic when a tier with an
   `instructor_id` is sold, using the group's effective window. Reuse the half-open overlap
   predicate from `BikeShopRepository`.
2. **Coach capacity caps the group.** `instructor.max_students_per_session int NOT NULL DEFAULT 8`.
   Effective group cap becomes `min(tier.inventory, instructor.max_students_per_session)`, so a
   coach cannot be oversubscribed even if an admin leaves tier inventory blank.

No `instructor_booking` table. Groups are already event-scoped rows with times; the overlap query
runs against tiers directly. That table only became necessary for the open-availability shape we
just cut.

### 3.3 Session length: half-day through multi-day

Length is just the event window, so a half-day clinic (9am-1pm) and a full-day clinic need nothing
beyond what 3.1 and 3.2 give them. Groups can stagger inside the day via per-group `starts_at` /
`ends_at`, which is how one clinic runs beginners in the morning and intermediates in the
afternoon without becoming two events.

A camp is **one event spanning multiple days** (`starts_at` day 1, `ends_at` day 3). One ticket
covers the camp; groups, coaches, caps, and camping all work as above. This is the correct model
for a package sold as a unit, and it needs almost nothing new.

The one real gap is **check-in**. Redemption is a one-shot status flip, so a day-1 scan marks the
ticket `redeemed` and day 2 has nothing to record. Add per-day attendance:

```sql
CREATE TABLE event_ticket_attendance (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    ticket_id     uuid NOT NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE,
    on_date       date NOT NULL,
    checked_in_at timestamptz NOT NULL DEFAULT now(),
    by_user_id    uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    UNIQUE (ticket_id, on_date)
);
```

Gate behavior for a multi-day event: the ticket stays valid for the event's whole span, and each
scan writes (or rejects as duplicate) one attendance row for the local date. Single-day events keep
today's behavior untouched; the `redeemed` flip stays as-is so nothing existing changes. This also
gives camps a daily roster, which is what a coach actually wants on the ground.

### 3.4 Camping and add-ons

Mostly already built. Camping is a seeded extra with per-event inventory, and extras already ride
the same PaymentIntent as tickets. For a camp, camping is bought as quantity = nights, or as an
extra whose price already covers the camp span. Two small gaps to close:

* Extras have no notion of "per night" versus "per stay", so a 3-night camp relies on the admin
  pricing the camping extra for the whole camp. Acceptable for v1; document it.
* Verify a multi-day event renders sensibly in the events list, calendar, and checkout summary.

### 3.5 Packages that include equipment and access

Both tenant types landed on this independently, which is what promotes it from "maybe" to a real
requirement:

* MX: a multi-day camp bundled with camping.
* MTB: Mountain Creek's Experience Downhill Package, $149.99 covering bike rental, trail access,
  protective equipment, and instruction; Deer Valley's Intro Packages including lift and equipment.

Two distinct cases hide behind the word "package", and only one needs new machinery.

**Case A, one price for a span.** A 3-day camp sold as a unit. Fully covered by 3.3; nothing new.

**Case B, one price covering other sellable things.** A lesson tier whose price includes a bike
rental and a lift ticket. The pieces already exist separately (a `shop_rental` against the shop
catalog, a required rider `gate_fee` tier, `event_extra_purchase` for camping) but nothing lets one
price subsume them, and each currently books its own ledger line.

Proposed shape, deliberately narrow:

```sql
CREATE TABLE event_ticket_tier_inclusion (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    tier_id     uuid NOT NULL REFERENCES event_ticket_tier(id) ON DELETE CASCADE,
    -- what the tier price already covers, one row per included thing
    kind        text NOT NULL CHECK (kind IN ('shop_rental','extra','gate_fee')),
    variant_id  uuid NULL REFERENCES shop_variant(id)        ON DELETE RESTRICT,
    product_id  uuid NULL REFERENCES event_extra_product(id) ON DELETE RESTRICT,
    quantity    int  NOT NULL DEFAULT 1
);
```

At checkout an inclusion provisions its thing at **zero incremental charge**: the rental row is
created and the bike's capacity reserved for the session window, the extra is issued, the gate fee
is satisfied. The whole price stays on the ticket, so revenue lands in one place and refunds stay
simple, at the cost of the reports not attributing part of a package to rental revenue. That
tradeoff is worth taking for v1 and should be written down where the reports live, because it is
exactly the kind of thing that looks like a bug later.

Note this is the same accounting shape the gift-card work already established: recognize the whole
amount on the primary sale and let the subsidiary object carry no money of its own.

### 3.6 Capacity fix (independent)

Move the `event.capacity` check out of the ladder loop so it runs for every cart, using a new
`EventSoldCount(eventId, tenantId)` counting active rider-audience purchases across all tiers on
the event. Ladder groups keep their group-scoped check. Applies to `PurchaseController` (pre-check
and the locked recheck at `:1552`) and `CounterController` (`:313`, `:628`). Ship first and alone;
it is a bug fix, not a feature.


## 4. Customer-facing flow (target)

A clinic appears on the events list with a lesson badge. The event page shows the coaches working
it (photo, name, bio, all already on the API response and currently discarded). Checkout shows the
groups as selectable tiers labeled by skill and bike size, each with its own price, its own time if
staggered, its coach, and an honest "N spots left" from the effective cap. Camping and other extras
attach as they do for any event. A bike can be added, time-scoped to the session.

For a camp, the same flow with a date range on the card, and per-day check-in at the gate.

No coach picker. No slot generation. No new checkout.


## 5. Build order

1. **Capacity fix.** Bug, no schema change, ships alone.
2. **Groups.** Tier gains `instructor_id`, `skill_level`, `equipment_label`, optional per-group
   window. Admin UI on the tier editor; customer-facing group selection and coach display.
3. **Coach constraints.** `max_students_per_session`, effective cap, conflict check at checkout.
4. **Clinic copy pass.** Lesson-aware strings in `EventCheckout` and `Event.vue`, lesson badge in
   the events list, drop the "Race #" field for lessons.
5. **Party pricing.** `party_size_included` / `party_price_cents` / `party_size_max`. Unlocks the
   MTB private shape (Trestle, Mountain Creek) with no new product model.
6. **Multi-day attendance.** `event_ticket_attendance`, gate check-in per day, camp roster view.
7. **Camp polish.** Multi-day rendering, camping-nights clarity, camp confirmation email.
8. **Inclusions.** `event_ticket_tier_inclusion`, for MTB intro packages and MX camp-plus-camping.

Steps 1 through 4 make the clinic flow correct for both tenant types and are worth doing
regardless. Step 5 is what makes MTB privates sellable. Steps 6 and 7 unlock camps. Step 8 is the
largest and should wait until a tenant actually needs a bundled package.


## 6. Deliberately not building

* **Open-availability private lessons**, instructor working-hour rules, and slot generation. The
  tenant does not sell this shape. Revisit only if that changes.
* **`lesson_product` / `lesson_booking` tables.** A group is a tier; no new purchase model.
* **Customer-facing instructor choice.** The market does not do it; it constrains inventory and
  invites dead ends. Show the coach, assign staff-side.
* **Instructor pay, commission, payroll.** Instructors are a scheduling resource, not a payee.
* **Rider skill profiles and automatic group matching.** Riders self-select a group by skill and
  bike size. A stored skill level per rider can come later.
* **Per-day pricing within a camp.** A camp is one price for the span. "Attend day 2 only" means
  the track sells a separate one-day clinic.
* **Instructor self-service login.** Admins manage coaches.


## 7. Open questions

1. **Which "package" case do tenants actually need first?** One price for a multi-day span (3.3,
   free) or one price that includes rental and access (3.5, the biggest item on the list)?
2. **Do groups within one clinic run at different times**, or do all groups run the same window
   with different coaches? Drives whether per-group `starts_at` / `ends_at` is needed in step 2 or
   can wait.
3. ~~Fixed skill list or free text?~~ **Answered by 2.2:** free text, tenant-configurable, because
   MX uses skill plus displacement and MTB uses trail-difficulty ability zones. Seed a picklist per
   tenant type.
4. **Is `private_booking`** (an existing seeded event type, hidden on apex alongside lessons) meant
   to be "rent the whole track"? If so it wants the same resource treatment.
5. **Does a camp ticket imply gate admission** for each day, or is a gate fee separate? MTB answers
   this cleanly (lift tickets required and sold separately, which the required `gate_fee` tier
   already models); MX camps are less obvious.
6. **Is minimum-to-run worth building?** Deer Valley cancels a clinic under two riders and converts
   it to a private. Needs a `min_to_run` on the tier plus a cancel-and-refund or convert flow.
   Recorded in 2.2, not scheduled.
