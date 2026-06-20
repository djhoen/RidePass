# QA Test Plan: Rewards / Loyalty

> Scope: tenant loyalty programs (CRUD), auto/opt-in enrollment, earning a voucher from qualifying purchases, percent-off redemption at online checkout and the counter, one-time-use enforcement, and proximity/earned emails. Last updated: 2026-06-20.

## Surface map
- **Admin:** `RewardController` Programs CRUD: `GET Programs/Admin`, `POST Programs`, `PUT Programs/{id}`, `DELETE Programs/{id}` (all `CatalogManage`). `GET Riders/{userId}/Redemptions` (`SalesCounter`).
- **Rider:** `GET Reward/Mine` (active programs + progress), `POST Programs/{id}/Enroll`, `POST Programs/{id}/Unenroll`, `GET Reward/MyRedemptions` (all `[Authorize]`).
- **Earning engine:** `Services/Rewards/RewardEngine.cs` `ProcessPaidPurchase`, invoked from `webapi/Payments/StripePurchaseFinalizer.cs` (line ~618) after a purchase is confirmed paid.
- **Redeeming (online):** `PurchaseController.BuyEventTicket` -> `ValidateVoucher` (line ~1018); `MarkRedemptionUsed("event_ticket", firstTicketId)`.
- **Redeeming (counter):** `CounterController` checkout (voucher block line ~412); `MarkRedemptionUsed(kind, firstPurchaseId)`.
- **Repository:** `Services/Repositories/RewardRepository.cs` (`CountQualifyingPurchases`, `CreateRedemption`, `MarkRedemptionUsed`).
- **Schema:** `Script0028_RewardPrograms.sql` (program / enrollment / redemption); `Script0029_PurchaseRewardRedemption.sql` (`applied_reward_redemption_id` on `day_pass_purchase` + `event_ticket_purchase`); `Script0056/0057` renamed `day_pass` -> `pass` in `requirement_kind`.
- **DTO:** `webapi/Controllers/API/Data/Reward/RewardDtos.cs`.

## Concepts under test
- A **program** is a rule: `requirement_kind` (`pass` / `event_ticket` / `any`) x `requirement_count` (>0) earns `reward_percent_off` (1-100). `enrollment_mode` is `auto` (enrolled on first paid purchase) or `opt_in` (rider self-enrolls). `proximity_email_threshold` is the "you're N away" nudge distance.
- A rider **earns** a voucher when, since their `enrolled_at`, their count of qualifying paid purchases minus `(redemptions_already_earned * requirement_count)` reaches `requirement_count`. The engine then mints one `reward_redemption` row (`earned_at` set, `redeemed_at` null) and sends the earned email.
- **Qualifying count** today comes only from `event_ticket_purchase` with status in (`paid`,`redeemed`) since `enrolled_at`. Day passes were retired, so `pass` and `any` both effectively count event tickets only (`RewardRepository.CountQualifyingPurchases`).
- A voucher is **redeemed** by passing `RewardRedemptionId` at checkout. It applies to ONE unit of ONE qualifying line; percent-off math is integer: `discounted = price - (price * percentOff / 100)` (truncates). After Stripe confirms (online) or at counter completion, `MarkRedemptionUsed` stamps `redeemed_at`, `redeemed_on_kind`, `redeemed_on_id`; `UPDATE ... WHERE id = @id AND redeemed_at IS NULL` is the one-time-use guard.
- A 100%-off voucher zeroes the line; the order is recorded with the ledger memo "Free purchase via reward voucher".

## Preconditions / test data
- A tenant with at least one published race event with a paid `race_entry` tier (e.g. $50.00 = 5000 cents) and a gate-fee extra, plus a second tenant for isolation checks.
- An `event_ticket` program: `requirement_kind=event_ticket`, `requirement_count=5`, `reward_percent_off=100`, `enrollment_mode=auto`, `proximity_email_threshold=1`.
- A second program: `requirement_kind=any`, `count=3`, `percent_off=50`, `enrollment_mode=opt_in`.
- Two rider accounts (Rider A authenticated; Rider B for wrong-owner tests) plus a guest email.
- SMTP configured in staging so earned/proximity emails actually send (engine no-ops when emailer is not configured).

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RW1 [NN] | Create a program | POST a program with each `enrollment_mode` and each `requirement_kind`; set count + percent | Saves; `GET Programs/Admin` lists it (active and inactive both shown; admin list passes `activeOnly:false`). |
| RW2 [NN] | Validation bounds | Try `requirement_count=0`, `reward_percent_off=0`, `reward_percent_off=101`, blank name | Rejected by DTO data-annotations (`Range(1,1000)` count, `Range(1,100)` percent, `Required` name) and DB CHECKs. |
| RW3 [NN] | Edit a program | PUT to change percent/count/active flag | Persists; reopen confirms. Existing un-redeemed vouchers keep their original program percent (rider redemption snapshots `RewardPercentOff` at list time via the program, so confirm behavior if percent changed after earn). |
| RW4 [NN] | Deactivate a program | Set `is_active=false` | Drops out of rider `GET Mine` (active-only) and out of new earning; existing un-redeemed vouchers can no longer be applied (`ValidateVoucher` rejects "program is no longer active"). |
| RW5 [NN] | Delete a program | DELETE a program that has enrollments + redemptions | Succeeds; `reward_enrollment` and `reward_redemption` cascade-delete (FK `ON DELETE CASCADE`). Confirm this is intended vs. blocking delete when redemptions exist. |
| RW6 [NN] | Rider redemption lookup at counter | As `SalesCounter` staff, `GET Riders/{userId}/Redemptions` | Returns that rider's redemptions scoped to programs in THIS tenant (`RedemptionsForUser` filters by tenant-scoped program map). |
| RW7 [R] | Permission gate | Call Programs CRUD without `CatalogManage` | 403. |

---

## User (enroll / earn / redeem)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RW8 [NN] | Auto-enroll on first paid purchase | As a brand-new Rider A, buy one event ticket; let the Stripe webhook finalize | `ProcessPaidPurchase` auto-enrolls A into every `auto` program (idempotent). `GET Mine` shows `IsEnrolled=true`, `Progress=1`. |
| RW9 [NN] | Opt-in enroll / unenroll | `POST Programs/{id}/Enroll` then `Unenroll` on the opt_in program | Enroll is idempotent (ON CONFLICT returns existing id); after Unenroll the enrollment row is gone and progress is no longer tracked. |
| RW10 [NN] | Progress accrues | Buy tickets 1..4 against the count-5 program | After each paid purchase `GET Mine` shows `Progress` incrementing, `RemainingForReward` decrementing; no redemption minted yet. |
| RW11 [NN] | Earn a voucher | Buy the 5th qualifying ticket | One `reward_redemption` row minted (`earned_at` set, `redeemed_at` null); earned email sent (subject "You earned a reward"). `GET MyRedemptions` shows it as un-redeemed. |
| RW12 [NN] | Progress resets after earning | Buy tickets 6..7 after earning | `Progress` shows 1, 2 (engine subtracts `earned * requirement_count`); a second voucher is NOT minted until the next full cycle (count 10). |
| RW13 [NN] | Proximity email fires once | With `proximity_email_threshold=1`, reach exactly `remaining==1` | "You're 1 away" email sent once; `last_proximity_emailed_at_count` stamped so re-processing the same count does not re-send. |
| RW14 [NN] | Redeem 100%-off online | Apply an earned 100% voucher to a single event ticket via `BuyEventTicket` (`RewardRedemptionId` set, quantity 1) | Line discounted to $0; order finalizes; ledger memo "Free purchase via reward voucher"; after finalize `redeemed_at` is stamped with kind `event_ticket` + the ticket id. |
| RW15 [NN] | Redeem 50%-off, percent math | Apply a 50% voucher to a $50.00 (5000c) entry at the counter | Discounted line = `5000 - (5000*50/100) = 2500`. Confirm the charged total reflects 2500 plus any service charge recomputed on the discounted price. |
| RW16 [NN] | Percent-off truncation | Apply a 33% voucher to a 5000c line | `5000 - (5000*33/100)=5000-1650=3350`. Integer division truncates (no rounding up); confirm 3350, not 3349/3351. |
| RW17 [NN] | One-time use | Redeem a voucher successfully, then attempt to apply the same `RewardRedemptionId` to a new purchase | Second attempt rejected ("That voucher has already been used"); `MarkRedemptionUsed` is a no-op because `redeemed_at` is already non-null. |
| RW18 [NN] | Voucher applies to one unit only | Online: set `RewardRedemptionId` with quantity > 1 | Rejected ("Reward vouchers can only be applied to a single ticket"). Counter: voucher discounts only the first unit of the chosen line; remaining units full price. |
| RW19 [NN] | Kind mismatch | Apply a `pass`-kind voucher to an event ticket | `ValidateVoucher` rejects ("only applies to passes"). Apply an `any` voucher: accepted on a ticket. |
| RW20 [NN] | Coupon vs voucher mutual exclusion | Online: supply both a coupon code and `RewardRedemptionId` | Rejected ("You can use either a reward voucher or a coupon, not both"). |
| RW21 [R] | Guest cannot use a voucher | Online checkout as guest with a `RewardRedemptionId` | Rejected ("Please sign in to use a reward voucher"). |

---

## Edge / cross-tenant

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RW22 [NN] | Wrong-rider voucher | Rider B tries to apply Rider A's `RewardRedemptionId` | Rejected ("That voucher isn't yours" / "isn't this rider's") via the `redemption.UserId != userId` check. |
| RW23 [NN] | Cross-tenant voucher | On tenant 2's subdomain, apply a redemption minted under tenant 1 | Rejected: `GetRedemption` is keyed by id only, but `ValidateVoucher` then loads `GetProgram(programId, tenantId)` scoped to the resolved tenant and returns null -> "program is no longer active." Confirm no discount applied and no cross-tenant `MarkRedemptionUsed`. |
| RW24 [NN] | `pass`-kind program is dead weight | Create a `requirement_kind=pass` program; have a rider buy tickets | Progress never advances toward a `pass` reward because `CountQualifyingPurchases` only counts event tickets (day passes removed). Document as a known gap: admin UI still offers `pass`. |
| RW25 [NN] | Refund after redemption | Earn + redeem a voucher, then refund/cancel the purchase it was applied to | Per `Script0029` comment, voucher restoration is "future"; confirm the redemption stays `redeemed`/consumed and is NOT auto-restored; flag the rider-experience gap. |
| RW26 [NN] | Double-fire webhook | Re-deliver the same Stripe event so `ProcessPaidPurchase` runs twice for one purchase | Risk: `CreateRedemption` has no uniqueness guard, so a duplicate run at the boundary could mint two vouchers. Confirm whether the finalizer is idempotent upstream. |
| RW27 [NN] | Suppressed email | Earn a reward for a hard-bounced address; earn proximity for a marketing-unsubscribed address | Earned email skips only hard bounces (`marketing:false`); proximity email also honors marketing opt-out (`marketing:true`). Voucher still mints regardless. |

---

## Known risks / watch-items
- **`pass` requirement kind is non-functional** (RW24): day passes were retired (`Script0118`) but the program type and DTO regex still accept `pass`. Such a program never earns. Recommend hiding `pass` in the admin UI or mapping it to tickets explicitly.
- **Qualifying count is program-agnostic** (RW10/RW11): `CountQualifyingPurchases` counts ALL paid event tickets for the rider since `enrolled_at`, not tickets tied to a specific event/program. Two `event_ticket` programs both count the same purchases, so a rider can earn from both simultaneously. Confirm intent.
- **Earning concurrency** (RW26): mint path is read-then-insert with no unique constraint on `(program_id, user_id, cycle)`. A concurrent or re-delivered webhook can over-mint. Consider an advisory lock or a uniqueness key per earned cycle.
- **No voucher restore on refund** (RW25): `applied_reward_redemption_id` is recorded for future restore but no flow un-stamps `redeemed_at`. A refunded "free" ticket consumes the voucher permanently.
- **Percent stored on program, not snapshotted on redemption** (RW3): rider redemption display reads the live program percent; editing the program after a voucher is earned changes the displayed/applied discount. Decide whether to freeze percent at earn time.
- **Cross-tenant isolation rests on the program re-fetch** (RW23): `GetRedemption` itself is not tenant-scoped; isolation depends entirely on the subsequent tenant-scoped `GetProgram`. If a future caller skips that, it leaks. Keep the program check mandatory.
