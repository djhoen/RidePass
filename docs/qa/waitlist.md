# QA Test Plan: Waitlist (Join / Promote / Expiry)

> Scope: joining a waitlist, class-aware bucketing for price ladders, promotion on a freed spot, prepay auto-confirm, and confirm-window expiry. Last updated: 2026-06-20.

## Surface map
- **User:** `WaitlistController` (`Join`, `Mine`, `Confirm/{token}`, `Confirm/{token}/Pay`, `Cancel`).
- **Engine:** `Services/Waitlist/WaitlistPromoter.cs` (`PromoteNext(eventId, tierId, ladderGroup)`).
- **Repo:** `EventWaitlistRepository` (bucket queries: `Enqueue`, `GetActiveForUser`, `PeekFront`, `CountAhead`).
- **Triggers (a spot frees):** `PurchaseController` admin cancel (~L1396), `MeController` self-cancel (~L383), `WaitlistExpiryWorker` (rolls an expired promotion to the next in the class).
- **Migrations:** `Script0052_EventWaitlist.sql`, `Script0126_WaitlistLadderGroup.sql`.

## Concepts under test
- A waitlist **bucket** is a class: the `ladder_group` when set, otherwise the exact `tier_id` (or tier-less = per-event). `tier_id` on the entry records the step a promotion charges (the active step at join time).
- A ladder step has no per-step inventory; the class sells against `event.capacity`. Joining is allowed only when the class is full (`group sold >= event.capacity`).
- One waiting/promoted row per rider per class (unique index on `COALESCE(ladder_group, tier_id, sentinel)`).

## Preconditions / test data
- Tenant `WaitlistEnabled` = on. Test rider has a **mobile phone** on profile (Join requires it).
- A ladder event with `event.capacity` set and ≥2 steps (e.g., $50 base, $75 after a trigger).
- A standalone-tier event and a tier-less (per-event) event for regression.

---

## Join gate

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| W1 [NN] | Join now possible for a full ladder | Sell the ladder class to `event.capacity`; rider opens waitlist and joins | Join succeeds (previously rejected as "unlimited capacity"). Entry stores the active step as its charge tier. |
| W2 [NN] | Not full | Same event below capacity; try to join | Rejected: "Spots are still available, so buy directly instead." |
| W3 [NN] | Uncapped ladder | Ladder event with no `event.capacity`; try to join | Rejected: "This event has unlimited capacity, so no waitlist is needed." |
| W4 [NN] | Dedup spans steps | A queued rider tries to join the same class again (even after the active step rose) | Rejected: "You're already on this waitlist." |
| W4b | No phone on profile | Rider without a mobile phone tries to join | Rejected with the "add a mobile phone" message. |

## Promotion (class-aware bucket)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| W5 [NN] | Cross-step promote (core) | Full ladder with a $50-step buyer and a $75-step buyer; a waiter joined when active = $75. Refund the **$50** buyer | The $75-bucket waiter is promoted (SMS/confirm) and, on confirm, charged their **join-time price ($75)**. (Before the fix this peeked an empty $50 bucket and promoted no one.) |
| W6 [NN] | Refund of the higher step | Same setup; refund the **$75** buyer instead | Same waiter promoted (bucket is the class, any step's refund frees it). |
| W7 [NN] | Position spans the class | Two riders queue the same class | Second sees "1 ahead"; first refund promotes #1, next refund promotes #2. |
| W8 [NN] | Prepaid auto-confirm | Waiter joins a full ladder with prepay; refund a class spot | Auto-confirms (no timer); a paid purchase row is created against the entry's step with `PrepayAmountCents`; confirmation SMS sent. |
| W9 [NN] | Expiry rolls within class | Promote a ladder waiter; let the confirm window lapse | `WaitlistExpiryWorker` expires them and promotes the next waiter in the same class. |
| W10 [NN] | POS interaction | A counter sale fills the last ladder spot | A later online buyer sees sold out and can waitlist; refunding the counter-sold ticket promotes a waiter. |
| W10b | Confirm-and-pay window | Promoted rider opens the SMS link and pays within the window; another opens it after expiry | First confirms and gets a ticket/QR; expired link reports the spot rolled on. |

## Regression guards

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| W11 [R] | Standalone tier | Tier with its own `Inventory`: join when full, promote on a refund of that tier | Unchanged; bucket is the tier. |
| W12 [R] | Per-event (tier-less) | Tier-less waitlist: join, position, promote on any freed spot | Unchanged. |

---

## Known risks / watch-items
- `ListForEvent` (admin) orders by `tier_id` then position; for a ladder, entries carry varying `tier_id` (active-at-join), so admin display may interleave steps within a class. Cosmetic; not bucket logic.
- Promotion honors the **join-time** price (the step stored on the entry), not the price at promotion time. Confirm this is the intended product behavior (it is rider-friendly and consistent with "the price you queued at").
