# Buddy Passes on Season Passes

Implementation-ready design. Status: proposed, not implemented. 2026-07-25.

Supersedes an earlier draft of this file that modelled buddy passes as shareable coupons
minted at pass purchase. Three requirements retired that model, and section 3 says why.

## 1. Requirements

1. The buddy must have an **account**. No anonymous redemption.
2. The **season pass holder must be present** and the buddy pass is redeemed **for** the buddy,
   at the counter, by staff.
3. The tenant chooses **which event types the buddy pass is good for, including non-event
   (walk-up) days**.
4. A pass grants a countable number of buddy admissions per season.
5. An admin can **return a spent buddy credit** to the holder. Entitlement only: no money
   moves.

## 2. Current state, verified

A tenant **can** store the grant but **cannot** configure or use it.

- `season_pass_benefit.benefit_type` has accepted `'buddy_pass'` since
  `Script0178_SeasonPassBenefits.sql` line 33, with `quantity` documented at line 42 as the
  countable case, "2 buddy passes per season".
- The API accepts it: `SeasonPassBenefitInput.cs` line 12, and `SeasonPassController.cs`
  lines 359 and 374-376 exempt it from needing a discount value while requiring a quantity.
- The rider is already **told** about it: `BuySeasonPass.vue` lines 276-280 and
  `SeasonPassLanding.vue` lines 213-217 both render "N buddy passes a season: bring a friend
  at {discount}".
- **No admin control.** The benefits editor writes only `'event'` rows (`SeasonPasses.vue`
  line 393) and whole-surface `'rental'` / `'retail'` rows (line 419).
- **No redemption.** `ListActiveBenefitGrantsForUser` (`SeasonPassRepository.cs` line 253) is
  the sole benefit consumption path and its only caller passes `benefitType: "event"`
  (`PurchaseController.cs` line 601).
- **No usage tracking.** No benefit-usage table exists, so `quantity` is never enforced.

So a tenant configuring this via the API today advertises a perk nobody can use.

What already exists and this design leans on heavily:

- **Counter can create the buddy's account at the window**: `POST Counter/Riders`
  (`CreateCounterRiderRequest`: email, first, last, birthdate, emergency contact) and
  `POST Counter/Riders/Find` for the returning case.
- **Counter can sell an event ticket** for that rider: `POST Counter/Sale` with a cart of
  `event_ticket` items keyed by **tier id** (`CounterCartItem.cs`).
- **Pass scanning proves presence**: `GET SeasonPass/Pass/{token}`
  (`SeasonPassController.cs` line 966) resolves a pass from its QR, already used by the gate
  scanner.
- **Per-pass advisory lock** for redemption races: `IDbHelper.AcquireAdvisoryLock`
  (`DbHelper.cs` line 117), the pattern `CreateWalkUpGateCheckIn` documents as a load-bearing
  caller contract (`SeasonPassRepository.cs` line 604).

## 3. Why the coupon model is out

The earlier draft minted N single-use coupons at pass purchase, to be shared by the holder and
redeemed by the buddy at checkout. Requirement 2 kills it: if the holder must be present and
staff perform the redemption, a transferable code is the wrong object. It would let a buddy
redeem alone from their sofa, which is exactly what the requirement forbids, and no amount of
validation makes a bearer code prove that two specific people are standing at a window.

Consequences of dropping it, stated plainly because the previous draft claimed the opposite:

- **A new table is required.** There is no pre-minted artifact to count, so entitlement usage
  must be recorded explicitly. The earlier "no new tables" claim does not survive requirement 2.
- `MintBuddyPassesForSeasonPass` is dropped. `BundledCouponMinter` is untouched.
- The `MyPasses.vue` coupon-sharing UI is not the surface, and the counter's missing coupon
  support (a real gap, but a separate one) is **no longer on this feature's critical path**.
  Buddy redemption is not a coupon redemption.

## 4. Configuration model

Requirement 3 needs a **set** of scopes, and requirement 4 needs **one** quantity pool. Those
are different cardinalities, so they cannot both live on `season_pass_benefit`, whose unique
index is one row per `(pass_product_id, benefit_type, scope_id)`. Three buddy passes valid at
Lift Days *and* Clinics is one entitlement with two scopes, not two entitlements of three.

**Parent** stays the existing benefit row, so the buy-page copy keeps working unchanged:

```
season_pass_benefit: benefit_type='buddy_pass', scope_id=NULL,
                     quantity=<pool per season>, discount_kind/value=<free or discount>
```

**Child** is new, one row per thing the buddy pass is good for:

### 4.1 Migration: `Script02NN_SeasonPassBuddyScope.sql`

```sql
CREATE TABLE IF NOT EXISTS season_pass_buddy_scope (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    benefit_id    uuid NOT NULL REFERENCES season_pass_benefit(id) ON DELETE CASCADE,
    -- Exactly one of these is set, enforced below. event_type_id names a tenant_event_type;
    -- is_walk_up covers admission on a day with no event at all (Script0236's walk-up mode).
    event_type_id uuid NULL REFERENCES tenant_event_type(id) ON DELETE CASCADE,
    is_walk_up    boolean NOT NULL DEFAULT false,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_buddy_scope_target CHECK (
        (event_type_id IS NOT NULL AND is_walk_up = false)
     OR (event_type_id IS NULL     AND is_walk_up = true)
    )
);

-- One row per target. Two "Lift Day" rows would double-list the perk in the admin UI.
CREATE UNIQUE INDEX IF NOT EXISTS uk_buddy_scope_event_type
    ON season_pass_buddy_scope (benefit_id, event_type_id) WHERE event_type_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_buddy_scope_walk_up
    ON season_pass_buddy_scope (benefit_id) WHERE is_walk_up;
```

**Zero child rows means valid nowhere, not everywhere.** A tenant who enables buddy passes and
picks no event types has configured a perk that admits no one, so the server rejects saving a
`buddy_pass` benefit with an empty scope set. The alternative default (empty = everywhere) is
the more dangerous one: it silently grants free admission to races.

`ON DELETE CASCADE` on `event_type_id` means deleting an event type narrows the perk rather
than orphaning a dangling scope. If that deletion empties the set, the perk stops admitting
anyone, which is the safe direction.

## 5. The non-event pricing problem

Verified: **there is no default day rate anywhere.** No `day_rate`, no `walk_up_price`, no
tenant-level admission price. A counter ticket sale prices off an `event_ticket_tier`
(`CounterCartItem.ItemId` is a tier id), and a tier belongs to an event.

So on a non-event day there is nothing to discount. "50% off a buddy" has no base price.

**Constraint: a buddy pass scoped to walk-up days must be free (100%, 10000 bps).** A
discounted buddy pass is only offerable on event-type scopes, where a tier supplies the price.
Enforced server-side on save, with a message that says so, and the admin UI disables the
discount option while a walk-up scope is selected.

This is a real limitation rather than an oversight, and the alternative is inventing a
tenant-level day rate, which is a much larger feature with its own tax and revenue questions.
Note that this is not theoretical for Highland: they have 167 distinct Lift Day dates across a
368-day span, so roughly half their operating days have no event.

## 6. Redemption

Two shapes, because the platform already treats event days and non-event days differently and
pretending otherwise would invent pricing that does not exist.

### 6.1 Event day: a real ticket sale for the buddy

Reuses the counter path wholesale, so waiver capture, QR, gate check-in, wristbands, tax,
revenue, and the Rider Report all work with no new code:

1. Staff scans the holder's pass (`GET SeasonPass/Pass/{token}`), which is the presence proof.
   The response gains `buddyPass: { remaining, total, scopes }`.
2. Staff finds or creates the buddy: `Counter/Riders/Find`, else `Counter/Riders`.
3. Staff picks the tier. The server validates the tier's event's type is in the scope set.
4. `Counter/Sale` for the buddy's rider id, with a new
   `BuddyPassRedemption { PassPurchaseId }` field on `CounterSaleRequest`. The server applies
   the benefit discount to that one ticket line and records the redemption.

The buddy's waiver is captured by the existing `SignWaiver` / `SignatureDataUrl` fields on
`CounterSaleRequest`. Nothing special is needed: a buddy is a rider.

### 6.2 Non-event day: a buddy admission row

Mirrors `CreateWalkUpGateCheckIn` (`SeasonPassRepository.cs` line 577) exactly, including its
event-or-date anchoring. The redemption row **is** the admission. Free only, per section 5, so
no money path and no tier is involved.

### 6.3 Migration: `Script02NN_SeasonPassBuddyRedemption.sql`

```sql
CREATE TABLE IF NOT EXISTS season_pass_buddy_redemption (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- Which pass spent the entitlement. The quantity check counts these.
    pass_purchase_id    uuid NOT NULL REFERENCES season_pass_purchase(id) ON DELETE CASCADE,
    buddy_user_id       uuid NOT NULL REFERENCES users(id),
    -- Event-anchored (6.1) or walk-up-anchored (6.2), never neither. Same shape as
    -- season_pass_reservation after Script0236, deliberately.
    event_id            uuid NULL REFERENCES event(id) ON DELETE SET NULL,
    check_in_date       date NULL,
    -- Set only for 6.1: the discounted admission this redemption paid for.
    ticket_purchase_id  uuid NULL REFERENCES event_ticket_purchase(id) ON DELETE SET NULL,
    discount_cents      int  NOT NULL DEFAULT 0,
    redeemed_at         timestamptz NOT NULL DEFAULT now(),
    redeemed_by_user_id uuid NULL REFERENCES users(id),
    -- Credit returned to the holder (section 7). Soft on purpose: the row stays as the record
    -- that this admission happened, and only the entitlement comes back.
    credit_returned_at      timestamptz NULL,
    credit_returned_by_user_id uuid NULL REFERENCES users(id),
    credit_return_reason    text NULL,
    CONSTRAINT chk_buddy_redemption_anchor CHECK (event_id IS NOT NULL OR check_in_date IS NOT NULL),
    -- A returned credit always carries who and why; a live one carries neither.
    CONSTRAINT chk_buddy_redemption_return CHECK (
        (credit_returned_at IS NULL     AND credit_returned_by_user_id IS NULL AND credit_return_reason IS NULL)
     OR (credit_returned_at IS NOT NULL AND credit_returned_by_user_id IS NOT NULL
         AND credit_return_reason IS NOT NULL AND length(btrim(credit_return_reason)) > 0)
    )
);

CREATE INDEX IF NOT EXISTS idx_buddy_redemption_pass
    ON season_pass_buddy_redemption (pass_purchase_id) WHERE credit_returned_at IS NULL;

-- One buddy, one admission, per pass per local day. Stops a double-tap at the window from
-- burning two entitlements, the same rule uk_season_pass_reservation_walkup enforces.
--
-- `credit_returned_at IS NULL` in the predicate is load-bearing, not decoration: without it a
-- mis-scan that was returned would permanently block re-admitting that same buddy on that same
-- day, which is precisely the situation a return exists to recover from.
CREATE UNIQUE INDEX IF NOT EXISTS uk_buddy_redemption_walkup_once
    ON season_pass_buddy_redemption (pass_purchase_id, buddy_user_id, check_in_date)
    WHERE check_in_date IS NOT NULL AND credit_returned_at IS NULL;
```

### 6.4 Quantity enforcement

`COUNT(*) FROM season_pass_buddy_redemption WHERE pass_purchase_id = @id AND
credit_returned_at IS NULL` against the benefit's `quantity`. The returned-credit predicate is
what makes section 7 work at all. Checked **under the per-pass advisory lock** (`AcquireAdvisoryLock`, keyed on the
pass purchase id), because two registers serving the same family at once would otherwise both
read "1 remaining" and both write. This is the same caller contract
`CreateWalkUpGateCheckIn` documents at `SeasonPassRepository.cs` line 604, and it should carry
the same comment so the next reader does not quietly drop the lock.

The unique index in 6.3 is a backstop for the walk-up shape only; the lock is the primary
guard, since the event shape legitimately allows the same buddy twice on one day (two
different events).

## 7. Returning a buddy credit (no money)

Requirement 5. An admin hands a spent credit back to the holder: staff redeemed the wrong pass,
the buddy never actually rode, or a goodwill call. **No money moves, and the buddy's admission
is not cancelled.** Those are separate operations with separate permissions, and conflating them
is how a "give the credit back" click ends up refunding a card.

### 7.1 Soft, never a delete

The redemption row is history: it records that a specific buddy was admitted, when, and by
which staff member. Deleting it to free the credit would erase that, and for the walk-up shape
(6.2) the row **is** the admission record, so a delete would assert that someone who rode never
did.

So a return sets `credit_returned_at` / `credit_returned_by_user_id` / `credit_return_reason`,
and the quantity count in 6.4 ignores returned rows. The entitlement comes back; the history
stays.

### 7.2 What it does and does not touch

| Shape | Admission after a return | Who cleans up the admission |
|---|---|---|
| Event day (6.1) | The buddy's `event_ticket_purchase` is **untouched** and still valid | Existing `POST Purchase/Ticket/{id}/Cancel` (`PurchaseController.cs` line 2326, `SalesCancel`), or the money refund path if they paid a discounted price |
| Walk-up (6.2) | Row stays, marked credit-returned. The admission record is preserved | Nothing to clean up; there was never a ticket |

The event-day case has a consequence worth stating out loud: returning the credit on a **free**
buddy ticket leaves a valid free admission with no entitlement behind it. That is intended
(the person rode, the ticket is real), but it means the usage report must show returned credits
rather than hiding them, or the numbers will look like free admissions appeared from nowhere.

The counter UI must say which of the two things is happening. Staff who click "Return credit"
expecting the buddy's ticket to disappear will otherwise let someone in twice.

### 7.3 Permission: `SalesCancel`, deliberately not `SalesCounter` or `SalesRefund`

- Not `SalesRefund`: no money moves, and reusing the money permission would blur the audit story.
- Not `SalesCounter`: **verified that `CashierSet` is `SalesCounter, SalesRedeem, SalesView,
  CashTurnIn` (`TenantPermissions.cs` line 186) and does not include `SalesCancel`**, while
  manager and admin do. So the person who can spend a credit at the window cannot also hand it
  back. That separation is the whole point: a returnable credit is a thing of value, and the
  cashier standing with the holder's friends is exactly who should not control it.
- `SalesCancel` is already the established non-monetary undo: `CancelTicket` uses it.

### 7.4 Guards and audit

1. Redemption exists and belongs to this tenant (scoped through `pass_purchase_id`'s pass).
2. Not already returned. A second return is a no-op with a clear message, not a second credit.
3. Reason is required and non-blank; the CHECK constraint enforces it at the storage layer too,
   so no code path can write a reasonless return.
4. Audit it, following the `shop.refund` shape at `BikeShopRegisterController.cs` line 558:

```csharp
await _audit.Log(
    "season_pass.buddy_credit_return",
    $"Returned a buddy pass credit on {product.Name} (buddy: {buddyName}) — {reason}",
    targetKind: "season_pass_buddy_redemption",
    targetId: redemption.Id,
    tenantId: TenantId,
    metadata: new { passPurchaseId, buddyUserId, ticketPurchaseId, discountCents });
```

Returning a credit on a pass whose season has already ended is **allowed** (the record should be
accurate) but the UI should say the pass has expired, because the holder gains nothing usable
and staff should know that before promising otherwise.

## 8. Validation, in the order the server should check it

1. Tenant resolved, and the caller holds **both** `SalesRedeem` (to scan the pass:
   `LookupPassByToken` is gated on it, `SeasonPassController.cs` line 964) and `SalesCounter`
   (to ring the sale: `CounterController` line 26). Verified: `tenant_cashier` holds both
   (`CashierSet`, `TenantPermissions.cs` line 186), as do manager and admin. **`tenant_scanner`
   holds only `SalesRedeem`**, so a scanner-only staffer can scan the pass and then fail at the
   sale. Do not surface the buddy panel on scanner-only screens, and say why if they reach it.
2. Pass exists, belongs to this tenant, `status = 'paid'`, and today is within
   `valid_from_date .. valid_to_date`.
3. Product has a `buddy_pass` benefit with `quantity >= 1`.
4. Buddy user exists and is **not** the pass holder. (Requirement 2 is "bring a friend"; the
   holder already has their own admission. This is the one self-redemption case worth blocking,
   and unlike the coupon model it is cheap to block because staff pick the buddy explicitly.)
5. Scope: for 6.1, the tier's event's `event_type_id` is in the scope set; for 6.2, a
   `is_walk_up` scope row exists.
6. Under the advisory lock: remaining > 0.
7. For 6.2, the walk-up unique index has no live row for this pass + buddy + local date.

Every failure gets its own message. "Buddy pass not available" for seven distinct causes is the
kind of error that generates a support call per occurrence.

## 9. Admin UI

`vueapp/src/views/Admin/SeasonPasses.vue`, benefits section of the product editor:

- **Buddy passes** quantity stepper (0 removes the benefit).
- Free / discount mode toggle, matching `setEventBenefitMode` (line 411). Disabled to Free
  while a walk-up scope is selected, with the reason shown, per section 5.
- **Good for** checkbox set: one per `tenant_event_type` plus "Days with no event (walk-up)".
  Renders the tenant's own names, so Highland sees Lift Day and Clinic.
- Save-time guard mirroring the server: quantity >= 1 requires at least one scope.

## 10. Counter UI

`vueapp/src/views/Admin/Counter.vue`, in the flow that already scans and sells:

- After a pass scan, a **Buddy pass: 2 of 3 remaining** panel, listing what it is good for.
- **Add buddy** opens the existing find-or-create rider form, then either the tier picker
  (event day) or a single confirm button (walk-up day).
- Show the discount being applied on the cart line, so staff can see the perk landed before
  taking payment.
- Every failure from section 8 surfaced verbatim. A spent entitlement must say "all 3 buddy
  passes used this season", not "unavailable".
- Gate the panel on the caller holding both permissions from section 8 step 1, so a
  scanner-only worker is not offered a flow that cannot complete.

## 11. Reporting and the Rider Report

A buddy admission is a genuinely distinct way through the gate, so it earns a bucket:

- **`buddy_pass` purchase type** in the Rider Report. For 6.1 the row is already an
  `event_ticket_purchase`, so the derivation is a `LEFT JOIN season_pass_buddy_redemption` on
  `ticket_purchase_id` in the ticket branch, ahead of the `day_ticket` fallback in
  `TicketPurchaseTypeExpr`.
- For 6.2 there is no ticket row, so the report needs a **third branch** over
  `season_pass_buddy_redemption`, anchored `COALESCE(event.starts_at, check_in_date at tenant
  midnight)` exactly like the walk-up season-pass branch. Ship this with the walk-up report
  change, not separately: they are the same shape and the same date-conversion trap.
- **Buddy pass usage** per pass product: issued entitlements, used, remaining, and who the
  buddies were. Straight off the new table. This is also where the section 7 return action
  lives, which promotes it out of the "nice to have" tail: without a surface, an admin has no
  way to return a credit.
- Returned credits must be **visible, not filtered out**, with their reason and who returned
  them. Hiding them makes free admissions look unexplained (section 7.2).

## 12. Sequencing note

Section 10's second bullet depends on the walk-up Rider Report change (already prepared,
waiting on Script0236 to deploy). Land that first, or the third branch has to be written twice.

## 13. Phasing

- **Phase 1**: scope + redemption tables, admin config UI, event-day redemption (6.1),
  quantity enforcement under the lock, counter UI, and the usage report **including the credit
  return action** (section 7). The return is not deferrable: the moment staff can spend credits,
  a mis-scan needs an undo, and the alternative is a manual SQL fix in production.
- **Phase 2**: walk-up redemption (6.2) and its Rider Report branch, once the walk-up report
  change has landed.
- **Phase 3**: nothing outstanding. Fold any remaining reporting polish here.

## 14. Refunded or cancelled pass

Section 7 settles what used to be an open question here. Because an admin can now return a
credit by hand, the pass refund path needs **no automatic entitlement teardown**: doing nothing
is the correct default, and the tenant has a tool for the cases where they want to intervene.

Already-taken buddy admissions stand regardless. Those people rode, and retroactively voiding
an admission after the fact would corrupt the gate history for a refund that has nothing to do
with them. The usage report surfaces them so a tenant reviewing a refunded pass can see what
the pass was used for before deciding anything.
