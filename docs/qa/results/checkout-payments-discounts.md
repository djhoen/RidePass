# QA Results: Online Checkout, Payments & Discounts

Traced against current code on 2026-06-20. Verifier cannot drive a live browser/Stripe, so each case is judged by reading the implementation. Verdict key: PASS (code implements the expected), FAIL (code contradicts), NEEDS-LIVE (logic is correct but only a Stripe/runtime run can confirm the end-to-end result), N/A.

The recent gift-card/coupon RESTORE-on-failure fix was the focus. It is present and correct: `StripePurchaseFinalizer.RestoreDiscountsFor` runs on `payment_intent.payment_failed` for both event tickets and rentals, and the reconciler's cancel/abandon paths feed through that same failed path. CP44 and CP45 are now FIXED.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| CP1 | PASS | `CouponController.Create` persists then returns `ToResponse`, whose `RedemptionCount` comes from `CountRedemptions` (0 for a new coupon). CouponController.cs:39-71, 115-134. |
| CP2 | PASS | `Create` calls `GetByCode` first; on a hit returns "Coupon code '...' is already in use." `GetByCode` lowers both sides. CouponController.cs:47-49; CouponRepository.cs:44. |
| CP3 | PASS | `ValidateDiscountValue` rejects percent > 10000 bps with the exact message. CouponController.cs:137-146. |
| CP4 | PASS | `ApplicableEventId` is stored on Create/Update; later rejected on a different event by the validator (see CP29). CouponController.cs:62, 93; CouponRepository.cs:53-59. |
| CP5 | PASS | `BuyGiftCard` enforces `GiftCardsEnabled` ("This tenant doesn't sell gift cards.") and the min/max band. PurchaseController.cs:1467-1474. Settings-write endpoint (`UpdateGiftCardSettingsRequest`) not inspected; enforcement side is correct. |
| CP6 | PASS | `ListForAdmin` reads `v_recent_sales` via `_recentSales.List`, returning each row's status as-is. PurchaseController.cs:1364-1389. Actual row values are runtime data. |
| CP7 | NEEDS-LIVE | Code is correct: response carries `ClientSecret` + `AmountCents` (= combined charge incl. rider service charge) and `RiderServiceChargeCents`; rows start `pending`; `OnPaymentSucceeded` flips paid + writes sale ledger + sends QR email. PurchaseController.cs:826-835, 559-589; StripePurchaseFinalizer.cs:522-632. Webhook round-trip needs live Stripe. |
| CP8 | NEEDS-LIVE | One PI for all units; each row stamped with the same intent id; per-row redemption token from `Create`. Fee split pro-rata by gross with the last line absorbing the remainder. PurchaseController.cs:584-589, 813-816; StripePurchaseFinalizer.cs:561-567. Live run confirms the actual fee split. |
| CP9 | PASS | Duplicate `succeeded`: lines filter out rows already `paid`/`redeemed` and the method returns when none remain (so no second email loop); ledger insert swallows Postgres `23505`. StripePurchaseFinalizer.cs:533-543, 592-597, 628-632. |
| CP10 | NEEDS-LIVE | `payment_intent.payment_failed` flips pending tickets to `failed`; no ledger row written. `SoldCount` counts `pending/paid/redeemed`, so failing the rows releases held inventory. StripePurchaseFinalizer.cs:232-237; EventTicketTierRepository.cs:105-111. Elements decline UX is client-side. |
| CP11 | NEEDS-LIVE | Reconciler: 20-min grace, 2-hr abandon cutoff; cancels the PI at Stripe FIRST, then on `canceled` finalizes as failed (frees inventory). PendingPurchaseReconciler.cs:25-28, 87-108. Timing/"no charge ever lands" needs a live run. |
| CP12 | NEEDS-LIVE | Reconciler `succeeded` branch calls the shared finalizer (paid + ledger + email + rewards). PendingPurchaseReconciler.cs:76-82. |
| CP13 | PASS | `CancelPaymentIntentAsync` re-reads status when cancel is rejected on a terminal PI, returning `succeeded`; reconciler then finalizes as paid instead of failing. StripePaymentProvider.cs:235-261; PendingPurchaseReconciler.cs:93-101. |
| CP14 | PASS | `CreatePaymentIntentAsync` throws `InvalidOperationException` when `Stripe:SecretKey` is empty; `BuyEventTicket` catches it and returns a 400 with the config message. Rows stay `pending` (never look paid; reconciler skips when status read returns null). StripePaymentProvider.cs:31-35; PurchaseController.cs:807-810. |
| CP15 | NEEDS-LIVE | Card minted up front `status=active`, `delivery=pending`, unique `GIFT-` code; PI = amount + service charge. On `succeeded` immediate cards email inline, future-scheduled left for the worker. PurchaseController.cs:1494-1547; StripePurchaseFinalizer.cs:121-128; GiftCardDeliveryService.cs:37-67. NOTE (out-of-plan gap): a `payment_failed` on a gift-card PURCHASE is not handled in the finalizer, so the minted active card with full balance is never voided. No test case covers it; flag for triage. |
| CP16 | PASS | Below-min and above-max both rejected with "Gift card amount must be between $X and $Y."; no card created (band check precedes Create). PurchaseController.cs:1470-1474. |
| CP17 | PASS | `BuyGiftCard` is `[Authorize]`; unauthenticated callers are rejected before the body runs. PurchaseController.cs:1458-1464. |
| CP18 | NEEDS-LIVE | Gift card applied after discounts; `stripeChargeCents = total - applied`; per-ticket `gift_card_redemption` rows; balance decremented by `AmountToApplyCents`; response includes `GiftCardAppliedCents`. PurchaseController.cs:619-672, 834. |
| CP19 | PASS | When `combinedStripeChargeCents == 0`, rows flip straight to `paid` with no PI and zero-value ledger rows; gift-card redemption rows are recorded before the free-cart branch. PurchaseController.cs:643-672, 740-764. |
| CP20 | PASS | Validator returns the specific messages: refunded -> "has been refunded"; depleted / zero balance -> "no balance remaining"; future-scheduled undelivered -> "hasn't been delivered yet." GiftCardValidator.cs:29-40. |
| CP21 | PASS | `GiftCardRepository.GetByCode` filters `lower(code) = lower(@code)`. GiftCardRepository.cs:50-54. |
| CP22 | NEEDS-LIVE | Coupon discount split pro-rata by sticker price; last remaining unit absorbs the rounding remainder; one `coupon_redemption` row per discounted ticket. PurchaseController.cs:546-557, 597-611. |
| CP23 | PASS | Validator looks up via `GetByCode`, which lowers both sides. CouponValidator.cs:28; CouponRepository.cs:44. |
| CP24 | PASS | Expired -> "That coupon has expired." CouponValidator.cs:35-36. |
| CP25 | PASS | Inactive -> "That coupon is no longer active." CouponValidator.cs:30. |
| CP26 | PASS | `MaxTotalUses` -> "That coupon has been fully redeemed." With the restore fix a failed cart no longer burns it (see CP45). CouponValidator.cs:46-50. |
| CP27 | PASS | Per-user cap only checked when `userId.HasValue` ("...maximum number of times."); guests are not per-user capped (matches documented intent). CouponValidator.cs:53-58. |
| CP28 | PASS | Scope mismatch -> "That coupon doesn't apply to event tickets." CouponValidator.cs:39-40, 71-77. |
| CP29 | PASS | `ApplicableEventId` != cart event -> "That coupon doesn't apply to this event." CouponValidator.cs:43-44. |
| CP30 | PASS | Both `couponCode` and `rewardRedemptionId` -> "You can use either a reward voucher or a coupon, not both." PurchaseController.cs:475-477. |
| CP31 | PASS | Validator caps a percent discount at the subtotal; unit price -> 0, service charge computed on the reduced price -> 0, combined 0 -> free-cart fast path, no PI. CouponValidator.cs:62-65; PurchaseController.cs:546-560, 740-764. |
| CP32 | NEEDS-LIVE | Single-unit percent voucher; only `q == 0` row carries `AppliedRewardRedemptionId`; finalizer calls `MarkRedemptionUsed` on success. PurchaseController.cs:541-543, 569; StripePurchaseFinalizer.cs:599-602. |
| CP33 | PASS | `totalUnits != 1` with a voucher -> "Reward vouchers can only be applied to a single ticket...". PurchaseController.cs:459-461. |
| CP34 | PASS | Voucher with no signed-in user -> "Please sign in to use a reward voucher." PurchaseController.cs:463-465. |
| CP35 | PASS | `ValidateVoucher` ties `redemption.UserId` to caller -> "That voucher isn't yours." PurchaseController.cs:1021-1023. |
| CP36 | PASS | `RedeemedAt` set -> "That voucher has already been used." PurchaseController.cs:1025-1027. |
| CP37 | PASS | Inactive program -> "That voucher's program is no longer active."; wrong kind -> "That voucher only applies to passes." PurchaseController.cs:1029-1037. |
| CP38 | PASS | 100% voucher -> combined 0 -> free-cart path flips paid, writes zero ledger, and `MarkRedemptionUsed` is called inline (no webhook). PurchaseController.cs:740-754. |
| CP39 | PASS | Empty / all-zero-qty cart -> "Cart is empty." PurchaseController.cs:207-214. |
| CP40 | PASS | Tiers from two events -> "All admissions in a single purchase must be for the same event." PurchaseController.cs:229-232. |
| CP41 | PASS | `CouponRepository.GetByCode` is scoped by `tenant_id`, so a Track B code does not resolve for Track A (and vice versa). CouponRepository.cs:44. |
| CP42 | PASS | `GiftCardRepository.GetByCode` is tenant-scoped; cross-tenant code -> "That gift card code isn't valid here." (no balance touched). GiftCardRepository.cs:52; GiftCardValidator.cs:29. |
| CP43 | NEEDS-LIVE | Ledger rows, emails, and rewards are all stamped from the purchase rows' own `TenantId`; nothing is written cross-tenant. StripePurchaseFinalizer.cs:575-590, 630-646. Confirm under live multi-tenant run. |
| CP44 | PASS (FIXED) | The flagged gap is fixed. `payment_intent.payment_failed` for event tickets calls `RestoreDiscountsFor("event_ticket", ...)`, which `DeleteRedemptionsBySource` (RETURNING) then `RestoreBalance` per card for the summed amount; the reconciler abandon path routes through the same failed event. StripePurchaseFinalizer.cs:232-237, 251-263; GiftCardRepository.cs:80-101; PendingPurchaseReconciler.cs:102-108. Restore amount equals the debited `AmountToApplyCents`; RETURNING makes it safe under a racing/duplicate finalizer. |
| CP45 | PASS (FIXED) | Same restore path also calls `_coupons.DeleteRedemptionsBySource`, freeing the `coupon_redemption` rows that drive `MaxTotalUses`/`MaxUsesPerUser`, so a declined/abandoned cart no longer burns a single-use coupon. StripePurchaseFinalizer.cs:262; CouponRepository.cs:137-144. |
| CP46 | PASS | `currency: "usd"` hard-coded on PI create; service-charge math uses `long` intermediates (no overflow); rounding remainders land on the last unit for coupon, gift card, and Stripe-fee splits so per-row sums equal the cart total. PurchaseController.cs:550, 649, 802, 1011-1013; StripePurchaseFinalizer.cs:564. |
| CP47 | NEEDS-LIVE | `ComputeWithServiceCharge` adds the rider-paid portion (tenant `ServiceChargeBps` x tier `RiderPaidServiceChargeBps`) into `AmountCents`; `RiderServiceChargeCents` returned in the response; ledger fee math reconciles in `OnPaymentSucceeded`. PurchaseController.cs:559-560, 833, 1008-1016. |

## Summary
No FAILs. The pre-payment burn risks (CP44 gift card, CP45 coupon) are confirmed FIXED by `RestoreDiscountsFor`.

Out-of-plan note worth triaging: a `payment_intent.payment_failed` on a gift-card PURCHASE (`BuyGiftCard`) is not handled by the finalizer (only `succeeded` is, StripePurchaseFinalizer.cs:121-128). The card is minted `status=active` with full balance up front, so a failed/abandoned gift-card purchase leaves a spendable card that was never paid for. No CP case covers this.
