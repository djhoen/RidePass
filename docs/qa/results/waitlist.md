# QA Results: Waitlist (Join / Promote / Expiry)

Static trace of each case against the current code. Verdicts: PASS / FAIL / NEEDS-LIVE / N/A.
Reviewed 2026-06-20.

## Join gate

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| W1 | PASS | Ladder join allowed only when full: `WaitlistController.cs:80-91` requires `event.Capacity`, computes `GroupSoldCount`, resolves the active step as `chargeTier`, and rejects "buy directly" only when `sold < capacity`. Entry stores `TierId = chargeTier.Id` and `LadderGroup` (`:118-130`), capturing the active step as the charge tier. |
| W2 | PASS | Below capacity rejected "Spots are still available, so buy directly instead." (`WaitlistController.cs:89-90`; standalone branch `:97-98`). |
| W3 | PASS | Uncapped ladder rejected "This event has unlimited capacity, so no waitlist is needed." (`WaitlistController.cs:82-83`). |
| W4 | PASS | Dedup spans steps: `GetActiveForUser` keys on the ladder group, not the step (`WaitlistController.cs:103`, `EventWaitlistRepository.cs:81-92`), plus the unique active index on `COALESCE(ladder_group, tier_id, sentinel)` (`Script0126:33-38`). Re-join rejected "You're already on this waitlist." (`:104-105` and 23505 catch `:136-138`). |
| W4b | PASS | No mobile phone rejected with the add-a-phone message (`WaitlistController.cs:55-59`). |

## Promotion (class-aware bucket)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| W5 | PASS | Refunding the $50 buyer calls `PromoteNext(eventId, $50tierId, ladderGroup)` (`PurchaseController.cs:1288-1290`). `PeekFront` buckets on `ladder_group` when set, ignoring the per-step tier (`EventWaitlistRepository.cs:112-123`), so the $75-bucket waiter is found. Charge tier = `next.TierId` (join-time step) (`WaitlistPromoter.cs:76`); `ConfirmAndPay` charges that tier's price = $75 (`WaitlistController.cs:291-298`). The pre-fix empty-$50-bucket bug is resolved by the `ladder_group` bucket key. |
| W6 | PASS | Refunding the $75 buyer routes through the same group-keyed `PromoteNext` (`PurchaseController.cs:1288-1290`); any step's refund frees the class bucket. Same waiter promoted. |
| W7 | PASS | Position counts within the class bucket: `Enqueue` next-position and `CountAhead` both key on `ladder_group` (`EventWaitlistRepository.cs:40-46, 201-211`). Second waiter sees "1 ahead"; sequential refunds promote #1 then #2 (`PeekFront` lowest waiting position). |
| W8 | PASS | Prepay PI succeeds -> webhook `MarkPrepaid` sets `is_prepaid=true` (`StripePurchaseFinalizer.cs:134-141`). On a freed spot the prepaid branch auto-confirms with no timer: creates a paid purchase against `chargeTierId` with `PrepayAmountCents`, links the prepay PI, `MarkConfirmed`, sends SMS (`WaitlistPromoter.cs:81-111`). SMS delivery itself is runtime. |
| W9 | PASS | `WaitlistExpiryWorker` sweeps promoted-past-deadline rows, `MarkExpired`, then `PromoteNext(eventId, entry.TierId, entry.LadderGroup)` (`WaitlistExpiryWorker.cs:48-58`); ladder `LadderGroup` keeps the roll within the same class bucket. |
| W10 | NEEDS-LIVE | Capacity counting (`GroupSoldCount` counts pending/paid/redeemed, `EventTicketTierRepository.cs:116-127`) and refund-driven promotion (`PurchaseController.cs:1284-1290`) are sound, so a counter sale fills the class and a later online buyer can waitlist. The POS counter sale + its refund route are not in the supplied files; confirm the counter-sold ticket refund promotes a waiter on stage (cross-ref POS plan). |
| W10b | PASS | `ConfirmAndPay` rejects a non-promoted or past-deadline link "This confirm window has expired. The spot has already rolled to the next person." (`WaitlistController.cs:279-282`) and otherwise creates the ticket/PI for the QR (`:300-328`). QR issuance is runtime. |

## Regression guards

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| W11 | PASS | Standalone tier: join uses `tier.Inventory`/`SoldCount` and `LadderGroup` stays null (`WaitlistController.cs:93-99`), so the bucket is the tier id; `PromoteNext` with null ladderGroup buckets on `tier_id` (`EventWaitlistRepository.cs:112-123`). Unchanged. |
| W12 | FAIL | Tier-less (per-event) join is no longer possible: `WaitlistController.Join` hard-requires a tier - "Pick an admission to join its waitlist." (`WaitlistController.cs:67-68`). The plan expects tier-less join "Unchanged," but no documented endpoint creates a tier-less entry. Appears INTENTIONAL: `ConfirmAndPay` comments that "Every waitlist entry is tier-based now... A tier-less entry is legacy/invalid." (`WaitlistController.cs:331-333`). Repo/index still carry the tier-less sentinel for legacy rows. Recommend updating the plan to drop the tier-less guard (or restore a tier-less join path) rather than treating it as a code bug. |

## Notes
- Promotion honors the join-time step price (the tier stored on the entry), per `WaitlistPromoter.cs:76` and `ConfirmAndPay` charging `entry.TierId`. This matches the documented intended behavior in the plan's watch-items.
- `ListForEvent` orders by `tier_id` then position (`EventWaitlistRepository.cs:94-101`), so a ladder's admin view may interleave steps within a class. Cosmetic, as the plan notes; not a bucket-logic defect.
