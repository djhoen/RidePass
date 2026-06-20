# QA Test Plan: Online Checkout, Payments & Discounts

> Scope: Stripe PaymentIntent checkout for event tickets + gift-card purchase, webhook/reconciler finalization (pending -> paid), and the three discount/payment instruments that ride on a cart: coupons, reward vouchers, and gift cards. Refunds are covered in a separate plan. Last updated: 2026-06-20.

## Surface map
- **User checkout (cart -> PI):** `PurchaseController.BuyEventTicket` (`POST /api/Purchase/EventTicket`, `[AllowAnonymous]` so guests can buy), `PurchaseController.BuyGiftCard` (`POST /api/Purchase/GiftCard`, `[Authorize]` so only signed-in buyers). Post-payment identity capture: `CompleteTicketRegistration` (`POST /api/Purchase/EventTicket/CompleteRegistration`).
- **Payment plumbing:** `webapi/Payments/StripePaymentProvider.cs` (PI create, `VerifyAndParseWebhook`, status read, cancel). `webapi/Controllers/PaymentController.cs` (`POST /api/Payment/Webhook`, signature-verified) delegates to `webapi/Payments/StripePurchaseFinalizer.cs`, the single fulfillment path that flips pending -> paid, writes ledger rows, sends QR emails, mints bundled coupons, runs rewards.
- **Missed-webhook safety net:** `webapi/Workers/PendingPurchaseReconciler.cs` (5-min tick; 20-min grace; 2-hour abandon cutoff; cancels the PI at Stripe before failing rows).
- **Discount / instrument validators:** `Services/Coupons/CouponValidator.cs` (`ICouponValidator`), `Services/GiftCards/GiftCardValidator.cs` (`IGiftCardValidator`), reward voucher via `PurchaseController.ValidateVoucher` against `IRewardRepository`. Code generation: `Services/Coupons/CouponCodeGenerator.cs`.
- **Admin:** `CouponController` (`/api/Coupon`, policy `CampaignsManage`) CRUD + case-insensitive duplicate guard. Gift-card sale settings via `UpdateGiftCardSettingsRequest` (enable, min/max). `PurchaseController.ListForAdmin` (`/api/Purchase/Admin`, `SalesView`) reads `v_recent_sales`.
- **Repos:** `CouponRepository.GetByCode` and `GiftCardRepository.GetByCode` both filter `tenant_id = @tenantId AND lower(code) = lower(@code)`. Redemptions: `CouponRepository.RecordRedemption` (unique `(source_kind, source_id)`), `GiftCardRepository.RecordRedemption` + `ApplyToBalance`.
- **Frontend:** `Event.vue` (ticket cart + Stripe Elements confirm), `BuyGiftCard.vue`, `FinishRegistration.vue` (resume), `Admin/Coupons.vue`, `User/MyPasses.vue` (vouchers/coupons).

## Concepts under test
- **One PaymentIntent per cart.** `BuyEventTicket` creates one pending purchase row per unit (each with its own QR token), then a single PI for the whole combined charge (tickets + extras + bundled membership, minus any gift-card amount). Every row is stamped with the PI id so the webhook can find them all.
- **Webhook is the money step.** Rows are created `status = "pending"`; only `payment_intent.succeeded` (via the finalizer) flips them to `paid`, writes the sale ledger row, and emails the QR. `payment_intent.payment_failed` flips pending rows to `failed`.
- **Idempotency is layered.** The finalizer filters out rows already `paid`/`redeemed`, and the ledger insert is guarded by a unique `(tenant_id, source_kind, source_id)` index (Postgres `23505` swallowed). A duplicate webhook is a no-op.
- **Discount precedence in `BuyEventTicket`:** reward voucher (percent off, single-unit only) and coupon are mutually exclusive; coupon applies to the tier subtotal and is split pro-rata across units (last unit absorbs rounding). Gift card applies last, as a payment instrument, against the post-discount total. If the combined Stripe charge is `0` the free-cart fast path flips rows straight to `paid` with no PI.
- **Gift-card balance is debited at PI-creation time**, before payment confirms (`ApplyToBalance` + per-ticket `gift_card_redemption` rows are written inside `BuyEventTicket`, not in the webhook). Coupon `coupon_redemption` rows are likewise written at buy time.

## Preconditions / test data
- A tenant ("Track A") with Stripe keys configured (`Stripe:SecretKey`, `Stripe:WebhookSecret`) and a Stripe CLI listener or test webhook so `payment_intent.*` events reach `/api/Payment/Webhook`. A second tenant ("Track B") for isolation.
- Track A has: a scheduled, future event with an active standalone ticket tier (e.g. $50) and a quantity > 1 available; gift cards enabled with min $10 / max $500.
- Coupons on Track A: `SAVE10` (10% off, scope `event_ticket`, active), `EXPIRED5` ($5 off, `valid_to_utc` in the past), `ONCE` (amount off, `max_total_uses = 1`), `PERUSER1` (`max_uses_per_user = 1`). One coupon on Track B (`BONLY`).
- A reward program on Track A that is active and grants a percent-off voucher (e.g. 100% and a separate 25%); a signed-in rider with one unredeemed voucher redemption.
- A gift card on Track A with a known balance (e.g. $30) in `active`/delivered state; one `refunded` card; one future-`ScheduledDeliveryAtUtc` card.
- Stripe test cards: `4242...` (succeeds), `4000000000000002` (declined), `4000002500003155` (requires 3DS / can be abandoned).

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CP1 [R] | Create coupon | Create `SAVE10` (10% / 1000 bps, scope event_ticket) | Saves; appears in list with redemption count 0. |
| CP2 [NN] | Duplicate-code guard is case-insensitive | Create a coupon `save10` (lowercase) while `SAVE10` exists | Rejected: "Coupon code 'save10' is already in use." (CouponController calls `GetByCode`, which lowers both sides). |
| CP3 [R] | Percent cap | Create a percent coupon with value > 10000 bps | Rejected: "Percent discount can't exceed 10000 bps (100%)." |
| CP4 [NN] | Event-scoped coupon | Create a coupon with `ApplicableEventId` set to event X | Validation later rejects it on event Y (CP21). |
| CP5 [NN] | Gift-card sale settings | Set min $10 / max $500, enable gift cards | `BuyGiftCard` enforces the band (CP15); disabling blocks purchase with "This tenant doesn't sell gift cards." |
| CP6 [R] | Sales list reflects paid only | After a happy-path ticket buy (CP7) open Admin -> Purchases | Row shows `paid` with the right amount; a still-pending or failed cart shows its status, sourced from `v_recent_sales`. |

---

## User: Stripe payment lifecycle

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CP7 [NN] | Happy path, single ticket | Buy 1 x $50 tier as a guest (email + name); confirm with `4242`; let the webhook arrive | Response carries a `clientSecret` and `amountCents` = price + rider service charge. Row starts `pending`; on `payment_intent.succeeded` it flips to `paid`, a `sale` ledger row is written, and the QR confirmation email is sent to the guest. |
| CP8 [NN] | Happy path, multi-unit single PI | Buy 3 x $50 in one cart | Three purchase rows, each with its own redemption token, all stamped with one PI id; one Stripe charge for the combined total. Webhook flips all three and writes three ledger rows; the PI-level Stripe fee is split pro-rata (last row absorbs rounding). |
| CP9 [NN] | Duplicate webhook is a no-op | After CP7 succeeds, redeliver the same `payment_intent.succeeded` from the Stripe CLI | No second ledger row (unique `(tenant,source_kind,source_id)` -> `23505` swallowed); status stays `paid`; no duplicate confirmation email (finalizer filters rows already `paid`). |
| CP10 [NN] | Card declined | Confirm with `4000000000000002` | Stripe fires `payment_intent.payment_failed`; pending rows flip to `failed`; no ledger row; held inventory is released. Buyer sees the decline in Elements and can retry. |
| CP11 [NN] | Abandonment leaves rows pending then reconciled | Start a buy, get the `clientSecret`, never confirm (close tab) | Rows stay `pending` (capacity held). After the 20-min grace + 2-hr abandon cutoff, `PendingPurchaseReconciler` cancels the PI at Stripe first, then fails the rows, freeing inventory. Verify no charge ever lands. |
| CP12 [NN] | Missed webhook recovered | Pay successfully but block/disable the webhook endpoint | Rows sit `pending`; on the next reconciler tick Stripe reports `succeeded`, the shared finalizer runs (paid + ledger + email + rewards). A paying customer is never left stuck. |
| CP13 [NN] | Cancel race during reconcile | Force the abandon path while completing payment in the same window | Reconciler's `CancelPaymentIntentAsync` returns `succeeded` (cancel rejected on a terminal PI); it finalizes as paid instead of failing the rows. No double-book, no lost payment. |
| CP14 [NN] | Stripe not configured | With `Stripe:SecretKey` empty, attempt a paid cart | `CreatePaymentIntentAsync` throws `InvalidOperationException`; controller returns a clean 400 with the config message (no rows left dangling that a buyer thinks are paid). |

---

## User: Gift cards (purchase + redemption)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CP15 [NN] | Buy a gift card | Signed-in buyer purchases a $50 card to a recipient email | `gift_card` row minted up front `status=active`, `delivery=pending`, unique `GIFT-` code; PI charges $50 + service charge. On `payment_intent.succeeded` the delivery email is sent inline (immediate) or left for the worker (scheduled). |
| CP16 [NN] | Amount outside band | Attempt $5 (below min) and $600 (above max) | Both rejected with the "must be between $X and $Y" message; no card minted. |
| CP17 [NN] | Guest blocked | Hit `POST /api/Purchase/GiftCard` unauthenticated | 401/Invalid token (endpoint is `[Authorize]`, unlike ticket checkout). |
| CP18 [NN] | Partial cover against a cart | Apply the $30 card to a $50 ticket cart | Gift card covers $30; `stripeChargeCents` = remainder; per-ticket `gift_card_redemption` rows written and balance decremented by the applied chunk. Response includes `giftCardAppliedCents`. |
| CP19 [NN] | Full cover -> free-cart fast path | Apply a card whose balance >= the full combined total, no extras | Combined Stripe charge resolves to $0; rows flip straight to `paid` with no PaymentIntent; zero-value ledger rows written; gift-card redemption rows still recorded. |
| CP20 [NN] | Insufficient / depleted / refunded / undelivered | Try a card with $0 balance, a `depleted` card, a `refunded` card, and a future-scheduled-undelivered card | Each rejected with its specific message ("no balance remaining", "has been refunded", "hasn't been delivered yet"). Cart unchanged. |
| CP21 [R] | Gift-card code case-insensitive | Apply the card using lowercased code | Resolves (repo lowers both sides), same balance applied. |

---

## User: Coupons

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CP22 [NN] | Percent coupon applies | Apply `SAVE10` to a 3 x $50 cart | $15 off split pro-rata across the 3 units (last unit absorbs the rounding cent); one `coupon_redemption` row per discounted ticket. Stripe charge reduced accordingly. |
| CP23 [NN] | Case-insensitive code | Apply `save10` | Resolves and discounts (validator + repo both lower). |
| CP24 [NN] | Expired coupon | Apply `EXPIRED5` | Rejected: "That coupon has expired." |
| CP25 [NN] | Inactive coupon | Deactivate `SAVE10`, apply it | Rejected: "That coupon is no longer active." |
| CP26 [NN] | Total usage limit | Redeem `ONCE` once (let it pay), then apply again on a new cart | Second attempt rejected: "That coupon has been fully redeemed." |
| CP27 [NN] | Per-user usage limit | Signed-in rider applies `PERUSER1` twice | Second rejected: "You've already used this coupon the maximum number of times." (per-user count requires a `userId`; verify a guest is not capped per-user). |
| CP28 [NN] | Wrong scope | Apply a `pass`-scoped coupon to an event-ticket cart | Rejected: "That coupon doesn't apply to event tickets." |
| CP29 [NN] | Wrong event | Apply the CP4 event-scoped coupon to a different event | Rejected: "That coupon doesn't apply to this event." |
| CP30 [NN] | Coupon + voucher mutually exclusive | Send both `couponCode` and `rewardRedemptionId` | Rejected: "You can use either a reward voucher or a coupon, not both." |
| CP31 [NN] | 100%-off coupon -> free cart | Apply a 100% (10000 bps) coupon to a single-tier cart with no extras | Discount caps at subtotal; combined charge $0; free-cart fast path flips to `paid` with no PI. |

---

## User: Reward vouchers

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CP32 [NN] | Percent voucher, one unit | Signed-in rider applies a 25% voucher to a single $50 ticket | Unit price reduced 25%; only the first/only unit carries `AppliedRewardRedemptionId`. On payment success the finalizer marks the redemption used. |
| CP33 [NN] | Voucher requires single unit | Apply a voucher to a 2+ unit cart | Rejected: "Reward vouchers can only be applied to a single ticket..." |
| CP34 [NN] | Voucher requires sign-in | Apply a voucher as a guest | Rejected: "Please sign in to use a reward voucher." |
| CP35 [NN] | Wrong rider | Apply voucher redemption id that belongs to another user | Rejected: "That voucher isn't yours." (ties `redemption.UserId` to the caller). |
| CP36 [NN] | Already used | Apply a redemption whose `RedeemedAt` is set | Rejected: "That voucher has already been used." |
| CP37 [NN] | Inactive / wrong-kind program | Apply a voucher whose program is inactive, then one scoped to passes only | "That voucher's program is no longer active." / "That voucher only applies to passes." |
| CP38 [NN] | 100% voucher -> free cart | Apply a 100% voucher to a single ticket | Combined charge $0; free-cart fast path flips to `paid`, zero ledger row, and `MarkRedemptionUsed` is called immediately (no webhook). |

---

## Edge / money-correctness / isolation

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CP39 [NN] | Empty / zero-qty cart | Submit a cart with no items or all `Quantity <= 0` | Rejected: "Cart is empty." |
| CP40 [NN] | Mixed-event cart | Put tiers from two different events in one cart | Rejected: "All admissions in a single purchase must be for the same event." |
| CP41 [NN] | Tenant isolation of coupon codes | From Track A apply `BONLY` (a Track B coupon) | Rejected as invalid here (`GetByCode` is scoped by `tenant_id`). Confirm Track A's `SAVE10` does not resolve on Track B. |
| CP42 [NN] | Tenant isolation of gift cards | From Track A apply a Track B gift-card code | "That gift card code isn't valid here." Balance on the Track B card unchanged. |
| CP43 [NN] | Tenant isolation of webhook fulfillment | Drive a paid cart for Track A | Ledger row, emails, and rewards all stamped with Track A's tenant id only; nothing written under Track B. |
| CP44 [NN] | Gift-card debit on abandoned cart (watch) | Apply a $30 card to a $50 cart, get the PI, then abandon (CP11) | KNOWN GAP: the card balance was decremented and `gift_card_redemption` rows written at PI-creation time. When the reconciler fails the rows there is no balance restore. Confirm/triage whether the $30 is silently lost to the buyer. |
| CP45 [NN] | Coupon usage consumed on failed/abandoned cart (watch) | Apply `ONCE` to a cart, then decline/abandon | KNOWN GAP: `coupon_redemption` is recorded at buy time, before payment confirms, so a declined or abandoned cart can burn a single-use coupon. Confirm whether a failed payment should release the redemption. |
| CP46 [NN] | Amount/currency sanity | Inspect the created PI for a discounted cart | `amount` = combined Stripe charge in integer cents, `currency = "usd"` hard-coded; service charge math uses `long` intermediates (no overflow); rounding remainders land on the last unit so per-row sums equal the cart total exactly. |
| CP47 [R] | Service charge applied | Buy a tier with a tenant service-charge bps and a tier `RiderPaidServiceChargeBps` | Rider-paid portion is added to `amountCents`; `riderServiceChargeCents` returned; ledger fee math reconciles on payment. |

---

## Known risks / watch-items
- **Gift-card balance is debited before payment confirms (CP44).** `BuyEventTicket` calls `ApplyToBalance` and writes `gift_card_redemption` rows synchronously at PI-creation, but the `payment_failed` / reconciler-abandon paths only flip purchase rows to `failed`. There is no compensating credit-back, so a declined or abandoned card-funded cart appears to permanently consume the applied balance. This is the highest-value money-correctness item to confirm.
- **Coupon redemptions are consumed pre-payment (CP45).** Same shape: `coupon_redemption` rows (which drive `MaxTotalUses` / `MaxUsesPerUser`) are recorded at buy time, so a `max_total_uses=1` coupon can be burned by a cart that never pays. Reward vouchers do NOT have this problem: they are only `MarkRedemptionUsed` in the finalizer on success (or on the free-cart fast path).
- **Per-user coupon cap is skipped for guests.** `MaxUsesPerUser` is only checked when `userId.HasValue`; a guest can reuse a per-user-limited coupon across orders. `MaxTotalUses` still bounds total abuse. Confirm intent.
- **Gift-card redemption has no DB idempotency guard.** `GiftCardRepository.RecordRedemption` has no `ON CONFLICT` / unique `(source_kind, source_id)` (unlike coupon and ledger). The buy path writes these once before the PI, so a normal retry creates new purchase rows anyway, but any future reuse of this method in a webhook-retry context could double-apply. Coupon and ledger inserts are idempotent; gift-card redemption is not.
- **Read-then-write on discount limits.** Coupon `MaxTotalUses` / `MaxUsesPerUser` are validated then redeemed without a lock, so two near-simultaneous carts could both pass a `max_total_uses=1` check (the capacity advisory lock covers inventory, not coupon counts). Low volume, but flag for high-demand drops.
- **Free-cart fast path bypasses the webhook entirely.** A 100% voucher/coupon or fully-covering gift card flips rows to `paid` inline. Verify the ledger, reward-mark-used, and admin Purchases list all behave identically to the webhook path, since the finalizer code never runs for these.
- **Webhook authenticity depends on `Stripe:WebhookSecret`.** `VerifyAndParseWebhook` rejects everything if the secret is unset (returns null -> 400). Confirm staging/prod both have it; a missing secret silently disables all fulfillment until the reconciler catches up.
