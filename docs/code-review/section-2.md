# Section 2: Payments, ledger & financial integrity

## Inline fixes applied during this review

Only one of the four Criticals was patched inline; the other three need real implementation work
(plumbing through repos / interfaces / state machines) and are documented for follow-up.

1. **Critical — confirmation emails never fire on first webhook delivery (FIXED)**
   `OnPaymentSucceeded` in `PaymentController.cs` calls `_passPurchases.UpdateStatus(id, "paid")` which
   writes to the DB but doesn't mutate the in-memory POCO. The subsequent
   `passes.Where(p => p.Status == "paid")` filter therefore matches zero rows and **no receipt /
   QR-code emails are sent on the first webhook delivery for any purchase.** Fixed by extending the
   `MarkPaid` closure to also mutate `p.Status = "paid"` after the DB write, so the email-loop filter
   finds the rows we just flipped. Every existing guest purchase that "succeeded" but produced no
   email was hit by this. Backfilling those emails is a separate task.

The remaining three Criticals (rental sales never ledgered, gift-card balance race, Stripe Transfer
idempotency) are listed in the Findings table — they require real implementation work and should be
prioritized for the next sprint.

## Scope

Files read end-to-end:
- `webapi/Payments/StripePaymentProvider.cs`
- `webapi/Controllers/PaymentController.cs` (Stripe webhook + 7-table fan-out)
- `Services/Payments/IPaymentProvider.cs`, `Services/Payments/FeeCalculator.cs`
- `Services/Repositories/TenantLedgerRepository.cs`, `Services/Repositories/Interfaces/ITenantLedgerRepository.cs`
- `Services/Repositories/DisputeRepository.cs`
- `Services/Repositories/TenantPayoutRepository.cs`
- `webapi/Controllers/SuperAdminController.cs` (refund + payout actions)
- `Services/Coupons/CouponValidator.cs`, `Services/Coupons/BundledCouponMinter.cs`
- `Services/GiftCards/GiftCardValidator.cs`, `Services/GiftCards/GiftCardDeliveryService.cs`
- `Services/Helpers/RefundCalculator.cs`

Spot-checks (relevant methods only) in the seven sale repositories:
- `PassPurchaseRepository` (UpdateStatus, MarkRefunded, Cancel, ListByStripePaymentIntentId)
- `EventTicketPurchaseRepository` (UpdateStatus, MarkRefunded, Cancel)
- `SeasonPassRepository` (UpdatePurchaseStatus, DecrementCredits)
- `MembershipRepository` (UpdateStatus, GetByPaymentIntentId)
- `GiftCardRepository` (ApplyToBalance, RecordRedemption, GetByCode, MarkDelivered)
- `RentalRepository` (UpdateStatus, MarkOut, MarkReturned, GetPurchaseByRentalPaymentIntentId)
- `EventExtraRepository` (UpdateStatus, ListByPaymentIntentId)

Migrations consulted for ledger / dispute / source-kind constraints:
- `Script0016_TenantPayouts.sql` (ledger schema, `uk_tenant_ledger_entry_sale_per_source`)
- `Script0017_DisputeLossLedger.sql`, `Script0018_DisputeFeeLedger.sql` (idempotency indexes)
- `Script0048_Rentals.sql`, `Script0056..0058` (source_kind CHECK expansions)

## Architecture summary

**Stripe wrapper.** `StripePaymentProvider` is registered AddSingleton. It reads `Stripe:SecretKey` and
`Stripe:WebhookSecret` once at construction; both empty → operations either throw (`CreatePaymentIntentAsync`,
`CreateTransferAsync`, `RefundAsync`) or, for the webhook entry point, log an error and return null (which
the controller surfaces as a 400). A non-empty secret feeds `StripeConfiguration.ApiKey` — a process-global
static. Webhook verification goes through `EventUtility.ConstructEvent(rawBody, signatureHeader, _webhookSecret)`;
on `StripeException` the parse returns null. The provider decodes four event-object shapes
(`PaymentIntent`, `Dispute`, `Account`, `Transfer`) into a single `PaymentWebhookEvent` record.

**Webhook fan-out.** `PaymentController.StripeWebhook` is the single ingress. It reads the raw body once
(`StreamReader.ReadToEndAsync`), verifies the signature, then dispatches:
1. Disputes → `HandleDispute` (upsert `dispute` row, write `dispute_loss` and `dispute_fee` ledger entries on `lost`).
2. `account.updated` → `_tenants.UpdateStripeConnectStatus`.
3. Transfer events → `HandleTransferEvent` (only reacts to `transfer.reversed` / `transfer.updated`+reversed).
4. PaymentIntent events → bulk-load every table that might point at this PI (`pass_purchase`,
   `event_ticket_purchase`, `season_pass_purchase`, `gift_card`, `rental_purchase`, `event_waitlist_entry`,
   `event_extra_purchase`, `membership_purchase`) and fan out status flips.

Gift cards, waitlist pre-pay, membership, extras, rental, and pass/ticket/season-pass each have their own
branch with its own status semantics. A counter cart that mixes pass + ticket + extras + membership all on
one PI flows through several branches in sequence (not early-returning) so each row gets flipped.

**Ledger schema.** `tenant_ledger_entry` is append-only; one row per chargeable event keyed by `entry_kind`
(`sale | refund | dispute_loss | dispute_fee | adjustment`) and `(source_kind, source_id)`. Three partial
unique indexes provide idempotency:
- `uk_tenant_ledger_entry_sale_per_source` — at most one `sale` per (tenant, source_kind, source_id).
- `uk_tenant_ledger_entry_dispute_loss_per_source` — at most one `dispute_loss` per source.
- `uk_tenant_ledger_entry_dispute_fee_per_source` — at most one `dispute_fee` per source.

There is **no** unique index for `entry_kind = 'refund'`. The current `source_kind` CHECK accepts
`pass | event_ticket | season_pass | rental | membership` — note `extras` (event extras) is **not** in the
list; the controllers explicitly skip ledger inserts for that kind (see Finding 2 below).

**Fee math.** `FeeCalculator.Calculate(tenant, gross, stripeFee, serviceCharge, occurredAt)` honors the
tenant's optional monthly service-charge cap by looking at `GetMonthlyRidepassCutCents`. `serviceChargeCents`
is the "RidePass cut"; net to tenant = `gross - stripeFee - ridepassCut`. The Stripe fee is fetched per
PI via `latest_charge.balance_transaction.fee` (PaymentController only — service charge is frozen on the
purchase row at create-time via `ComputeWithServiceCharge`).

**Refund flow.** Two paths: pass refund (`SuperAdminController.ProcessPassRefund`) and ticket refund
(`SuperAdminController.ProcessTicketRefund`). Both compute `RefundCalculator.RefundableCents` (= amount minus
rider's portion of service charge — the "service charge is never refunded" rule), call
`_payments.RefundAsync(StripePaymentIntentId, refundCents)`, then `MarkRefunded`, then
`WriteRefundLedgerEntry` which mirrors the original sale with negated amounts. No refund UI exists for
season passes, memberships, rentals, gift cards, or event extras.

**Payouts.** `TenantPayoutRepository` creates payouts in `pending`, attaches unpaid ledger entries via
`AttachUnpaidEntries`, recomputes totals via `RefreshTotals`. Super-admin can send via Stripe Transfer
(`SendPayoutViaStripe` → `IPaymentProvider.CreateTransferAsync`) which marks `paid` synchronously and uses
`transfer.reversed` as a backstop. Or super-admin can hand-mark `paid` with an arbitrary external reference.

## Findings

| Severity | Location | Description | Suggested fix |
|---|---|---|---|
| **Critical** | `webapi/Controllers/PaymentController.cs:226-238` and `RentalController.cs:441` | **Rental sales never write a ledger entry.** The webhook handler flips rental_purchase → `paid` but no `tenant_ledger_entry` insert follows. Same for the free-cart fast-path. Result: rental revenue is invisible to `GetSummary` / `GetMonthlyGrossVolumeCents` / payouts / reconciliation. Tenants who run rentals get under-paid. The `source_kind` CHECK *does* include `'rental'`, the schema was prepared for it — only the controller code is missing the insert. | In `PaymentController.OnPaymentSucceeded`-equivalent for the rental branch, call `_feeCalculator.Calculate` and `_ledger.Insert(new TenantLedgerEntry { EntryKind = "sale", SourceKind = "rental", ... })`. Catch `23505` for idempotency. Mirror in the free-cart path with an `InsertZeroLedger` equivalent. |
| **Critical** | `Services/Repositories/GiftCardRepository.cs:68-78` + `Services/GiftCards/GiftCardValidator.cs:24-48` | **Gift-card balance can go negative under concurrent redemption.** `GiftCardValidator.ResolveAsync` reads `BalanceCents` and returns `min(balance, owed)`. The caller later runs `ApplyToBalance` which is `UPDATE … SET balance_cents = balance_cents - @amountCents` — no `WHERE balance_cents >= @amountCents` guard, no `SELECT FOR UPDATE`. Two concurrent checkouts on the same card (e.g. shared-with-spouse scenario, or a malicious double-tab) both pass validation with the full balance, then both deduct → final balance is negative and both purchases proceed with the gift card. | Make `ApplyToBalance` atomic + guarded: `UPDATE gift_card SET balance_cents = balance_cents - @amountCents, status = CASE WHEN balance_cents - @amountCents <= 0 THEN 'depleted' ELSE status END WHERE id = @id AND balance_cents >= @amountCents RETURNING balance_cents` and have it return whether the update fired. Callers must check the result and unwind the redemption row + roll back the purchase if it returned no rows. |
| **Critical** | `webapi/Controllers/SuperAdminController.cs:636-665` (`SendPayoutViaStripe`) | **Stripe Transfer.create has no idempotency key.** A network blip on the response, a client timeout that triggers a retry, or two super-admins clicking the button in parallel will call `_payments.CreateTransferAsync(...)` twice. Stripe is happy to create two transfers for the same `(destination, amount)` without an idempotency key — the tenant double-receives the payout out of the platform balance, and only one of the two transfers ends up linked to the `tenant_payout` row. Same risk on `RefundAsync` (used by `ProcessPassRefund` / `ProcessTicketRefund` / `RentalController.MarkReturned`). | Pass an idempotency key into the Stripe SDK call: `new RequestOptions { IdempotencyKey = $"payout:{payoutId}" }` on transfers and `$"refund:{purchaseRowId}"` on refunds. Wire `RequestOptions` through `IPaymentProvider.CreateTransferAsync` / `RefundAsync` so callers can pass a stable key. Also add a `WHERE status = 'pending'` row-level guard or `SELECT … FOR UPDATE` around the payout's Stripe-call window. |
| **Critical** | `webapi/Controllers/PaymentController.cs:506-515` (`OnPaymentSucceeded`) | **Per-purchase confirmation emails never fire on a fresh webhook.** `lines` is built from `passes.Where(p => p.Status != "paid" && p.Status != "redeemed")`, each line's `MarkPaid()` does `_passPurchases.UpdateStatus(p.Id, "paid")` which only updates the DB row — it does **not** mutate `p.Status` on the in-memory POCO. The follow-up `foreach (var dp in passes.Where(p => p.Status == "paid"))` therefore matches *zero* rows for the first webhook delivery. Guests buying tickets get no QR-bearing email. (A *redelivered* webhook would re-fetch with `Status = "paid"` and would then send — but Stripe usually doesn't redeliver after a 200.) | Either mutate the in-memory `p.Status = "paid"` after `MarkPaid()`, or just iterate `lines` (which is the set we actually just paid) for the email loop instead of re-filtering `passes` / `tickets`. The reward-engine block below (line 518-537) iterates over the whole `passes`/`tickets` lists and is similarly fragile — recommend it also drive off the freshly-paid `lines`. |
| **High** | `webapi/Controllers/PaymentController.cs:208-224` + `Script0058_Memberships.sql:89` | **Event extras are paid but ledgered nowhere.** The webhook flips `event_extra_purchase` to `paid`, but the `tenant_ledger_entry` `source_kind` CHECK only allows `pass / event_ticket / season_pass / rental / membership` — `extras` was never added. `CounterController.cs:573-576` comments confirm this is intentional ("source kind 'extras' isn't in the tenant_ledger CHECK constraint yet, so we skip ledger inserts for now"). Tenants who sell pit-vehicle passes, camping spots, etc. — those revenues never appear on their dashboard or payouts. | Add `'extras'` to the `source_kind` CHECK in a new migration; backfill ledger entries for paid extras from the past; have `OnPaymentSucceeded` / counter cash sale / the extras branch in the webhook each emit a ledger insert. The free-cart and gift-card-fully-cover paths in `PurchaseController` need the same treatment. |
| **High** | `webapi/Controllers/PaymentController.cs:163-170` (gift-card delivery) + `Services/GiftCards/GiftCardDeliveryService.cs:37-66` | **Gift-card delivery email is not idempotent against duplicate webhook deliveries.** The webhook fires `_giftCardDelivery.SendDeliveryEmail(giftCard)` whenever the PI succeeds and the card is immediate-delivery — no check of `card.DeliveryStatus`. The service sends the email *then* calls `MarkDelivered`. Two webhook deliveries arrive in parallel → both see `delivery_status = 'pending'`, both send, recipient gets two emails (and `MarkDelivered` runs twice — idempotent at the row level but the email has already gone). | Inside `SendDeliveryEmail`, re-fetch the card and short-circuit if `delivery_status != 'pending'`, or move `MarkDelivered` to a conditional `UPDATE … SET delivery_status='delivered' WHERE id=@id AND delivery_status='pending'` and only send the email if the update affected one row. |
| **High** | `webapi/Controllers/RentalController.cs:614-660` (`MarkReturned`) | **Rental deposit refund has no idempotency guard, no idempotency key, and the catch-all rejects all errors as 400 even when the Stripe refund succeeded.** Two staff clicks → two `RefundAsync` calls → two partial refunds (Stripe does not de-dup without an idempotency key). The `catch { return ... "Could not issue deposit refund via Stripe. Mark the rental returned again after fixing." }` swallows network-blip errors after the refund posted, so the operator retries and Stripe refunds the deposit twice. There's also no `WHERE status IN ('out','paid')` predicate on `MarkReturned`. | Pass `IdempotencyKey = $"rental-deposit-refund:{rental.Id}"`. Differentiate Stripe `StripeException` (refund-known-failed) from transport errors (refund-unknown); on transport errors, don't allow retry until reconciliation has confirmed Stripe state. Add the missing `WHERE` predicate. |
| **High** | `webapi/Controllers/PaymentController.cs:714-741` (refund ledger math) + `Services/Helpers/RefundCalculator.cs` | **Refund ledger row reverses RidePass's service charge cut even though the cut is never refunded.** `RefundCalculator.RefundableCents` keeps the rider-paid service-charge portion at RidePass (refund = amount − riderPortion). But `WriteRefundLedgerEntry` writes `RidepassCutCents = -sale.RidepassCutCents` — fully reversing the cut. Net effect: tenant balance correctly sums to zero across (sale + refund), but **lifetime RidePass cut totals under-count** (the dollars are still in the platform pocket; the ledger says otherwise). Reconciliation gap not flagged because both sides of the equality drop in lock-step. | Set `RidepassCutCents = 0` on the refund row (or set it to `-sale.RidepassCutCents + (sale.RidepassCutCents - whateverWasActuallyKept)` if you want to express which portion really did get refunded). Adjust `NetToTenantCents` so the tenant balance still sums to zero — likely `NetToTenantCents = -(sale.NetToTenantCents + sale.RidepassCutCents)`. Add a unit test that re-derives the platform's lifetime cut from the ledger and asserts it matches the dollars actually retained. |
| **High** | `Services/Repositories/GiftCardRepository.cs` (no refund path) + `webapi/Controllers/SuperAdminController.cs:404-420` (`ProcessPassRefund`) | **Refunding a gift-card-paid purchase does not re-credit the gift card balance.** No code path adds back to `gift_card.balance_cents`. A rider buys a $50 pass with their $50 gift card, the tenant cancels, refund processes: Stripe refunds $0 (PI was for $0 of Stripe charge), the pass row flips to `refunded`, the gift card stays depleted. The rider has nothing. | Add `IGiftCardRepository.CreditBalance(id, amountCents)` that re-credits via `UPDATE gift_card SET balance_cents = balance_cents + @amountCents, status = CASE WHEN status='depleted' THEN 'active' ELSE status END`. In the refund flow, look up `gift_card_redemption` rows for the source purchase and reverse them. |
| **High** | `Services/Repositories/PassPurchaseRepository.cs:125-129` (and twins on `EventTicketPurchaseRepository`, `SeasonPassRepository`, `MembershipRepository`, `RentalRepository`, `EventExtraRepository`) | **`UpdateStatus(id, status)` has no `WHERE status = 'pending'` guard.** Idempotency for sale-flips relies entirely on `uk_tenant_ledger_entry_sale_per_source` catching the duplicate ledger insert with a 23505 swallow. That works today, but every flip also fires side effects *before* the ledger insert: `_extras.UpdateStatus` (no ledger), `_rentals.UpdateStatus` (no ledger), `MarkPrepaid` on waitlist, the reward-engine call, the bundled-coupon minter, the confirmation emails, the large-sale super-admin notification. None of those benefit from the ledger-level dedupe — re-deliveries can re-fire them. | Add `WHERE id = @id AND status = 'pending'` to each `UpdateStatus` and return the affected row count; PaymentController should branch on that instead of pre-reading `Status`. (Section 1 already noted some of these methods take no `tenantId` — the audit fix there can land at the same time as this guard.) |
| **High** | `webapi/Controllers/PaymentController.cs:577-687` (`HandleDispute`) | **Dispute handler only matches passes and tickets** — a chargeback on a season pass, membership, rental, gift-card, or event extra never gets a `dispute` row written, no super-admin/tenant notification fires, and no `dispute_loss` / `dispute_fee` ledger entries appear. Stripe will still claw back the funds out of the platform balance, but the system has no record. | Expand `HandleDispute` to look up all six remaining tables by PI (mirroring the lookup `OnPaymentSucceeded` already does). Generalize `dispute.pass_purchase_id` / `dispute.event_ticket_purchase_id` columns into a `source_kind`/`source_id` pair — same shape as the ledger. |
| **High** | `Services/Coupons/BundledCouponMinter.cs:24-72` | **Bundled coupon minting has a TOCTOU race.** `MintForPurchase` calls `ListIssuedFromPurchase(purchaseId)` and returns early if anything exists; otherwise mints N. Two concurrent webhook deliveries (or any pair of parallel calls) both see empty and both mint — the buyer receives 2N codes. The `coupon` table has no unique constraint that would catch this. | Wrap the lookup-and-insert in a single transaction with `SELECT … FOR UPDATE` on the parent `event_ticket_purchase` row, or move the idempotency check after insert (catch a uniqueness violation on, e.g., a new unique index on `(issued_from_purchase_id, sort_order)` if you add one). Alternatively, gate the entire success branch on `UpdateStatus`-returning-1 (Finding 10) so only one webhook delivery enters the mint code. |
| **High** | `Services/Coupons/CouponValidator.cs:46-58` | **Coupon `MaxTotalUses` and `MaxUsesPerUser` are not enforced atomically.** Two concurrent checkouts on the last redemption of a single-use coupon both call `CountRedemptions(coupon.Id)`, both see 0, both pass, both call `RecordRedemption` later. The `coupon_redemption` table has no unique constraint on `coupon_id` that would catch the duplicate. | Add a partial unique index on `coupon_redemption(coupon_id)` when the coupon is single-use, or move the count + insert into a transaction with `SELECT max_total_uses, (SELECT COUNT(*) FROM coupon_redemption WHERE coupon_id=…) FOR UPDATE` against the parent `coupon` row. |
| **Medium** | `webapi/Controllers/SuperAdminController.cs:376-466` (refund queue) | **Double-click on "Process refund" is only race-protected by Stripe rejecting a second full refund** — and only for *full* refunds. Partial refunds (`refundCents < amountCents`) bypass Stripe's natural de-dup. The `MarkRefunded` SQL takes no `WHERE status = 'cancelled'` predicate and no row lock, and the queue list is loaded again on every click. | Combine with the Critical-level idempotency-key fix in Finding 3, and add `WHERE status = 'cancelled'` to `MarkRefunded`. |
| **Medium** | `webapi/Controllers/PurchaseController.cs:382-384` (pass coupon distribution) | **Pass-purchase coupon discount loses up to (quantity − 1) cents.** `var perUnit = dpCoupon.DiscountCents / quantity; effectiveUnitPrice -= perUnit;` — integer division truncates and no "last unit absorbs rounding" pass exists. The ticket equivalent at line 870-877 does this correctly. For a 7-cent coupon split across 3 passes, the rider loses 1 cent and the `coupon_redemption.discount_cents` recorded later overstates what actually came off the price. | Mirror the ticket pattern: compute `unitCouponDiscount` per row with `(i == quantity-1) ? remaining : prorated` and decrement a running `remaining`. |
| **Medium** | `webapi/Controllers/SeasonPassController.cs:288-301` | **Season-pass gift-card-fully-covered fast-path flips status but writes no ledger entry.** `PurchaseController` uses `InsertZeroLedger` for the equivalent pass / ticket free path; this season-pass branch was missed. The pass appears in admin lists with no corresponding ledger row, breaking reconciliation. | Call an `InsertZeroLedger`-equivalent (`EntryKind="sale"`, gross=0, paymentMethod="voucher") for the season pass. |
| **Medium** | `webapi/Controllers/PurchaseController.cs:937` + `Services/Repositories/TenantLedgerRepository.cs:82-97` | **Gift cards paid against a purchase create a systematic Stripe-vs-ledger gross gap.** Ledger row records `GrossCents = unitAmount` (full pre-gift-card value). Stripe only sees `stripeChargeCents = total − giftCardApplied`. The reconciliation view (`SumForPeriod` filters `payment_method = 'stripe'`) is computed in `stripe`'s units, while `ledger.GrossCents` reflects the pre-instrument-application value. So `grossGap = stripe.GrossCents − ledger.GrossCents` will always be negative by the sum of gift-card applications in the period — that's actual real revenue, not a discrepancy, but the UI surfaces it as one. | Either subtract gift-card-redemption-totals from the ledger gross in `SumForPeriod`, or surface "gift card applications" as a separate column in the reconciliation panel so users see the gap is explained, not anomalous. |
| **Medium** | `webapi/Controllers/PaymentController.cs:407-468` (Stripe fee distribution) | **Pro-rata Stripe fee split happens before the ledger insert** — but the fee is fetched per webhook delivery from Stripe (`GetActualStripeFeeCentsAsync`), which means a duplicate webhook delivery that races past the unique-index dedupe (e.g. if Stripe returns a different fee value between calls — unlikely but possible during retries on uncaptured PIs) could record a slightly different fee on each line. The current code already handles the duplicate-ledger case via the 23505 catch, so this is bounded — but if `GetActualStripeFeeCentsAsync` returns null (PI not yet settled at first webhook), `stripeFee` falls back to 0 and the ledger entry permanently locks in fee=0 with no reconciliation pass to correct it later. | Add a reconciliation worker that walks recent `tenant_ledger_entry` rows where `stripe_fee_cents = 0` and `payment_method = 'stripe'`, re-fetches the actual fee, and updates in-place. Or fail-open: if the fee fetch returned null, schedule a deferred re-write rather than writing fee=0. |
| **Medium** | `webapi/Controllers/SuperAdminController.cs:663-665` (`SendPayoutViaStripe` status update) | **Status update on a successful Stripe transfer doesn't condition on the prior `pending` status** — `_payouts.UpdateStatus(id, tenantId, "paid", …)` always runs. If two clicks slip past the `payout.Status != "pending"` pre-check (lines 616-619), both Stripe transfers fire (Finding 3) and both DB writes succeed; the second one quietly overwrites `external_reference` and `payout_date_utc`. | Add `AND status = 'pending'` to `TenantPayoutRepository.UpdateStatus`'s WHERE when transitioning to `paid`, or refactor that path to use a dedicated `MarkPaidIfPending` returning the affected row count, and have `SendPayoutViaStripe` short-circuit if it returns 0 (and reverse the just-created Stripe transfer, since it was a duplicate). |
| **Medium** | `Services/Repositories/TenantLedgerRepository.cs:30-41` (`Insert`) | **No `entry_kind = 'refund'` unique index.** The three other entry kinds (`sale`, `dispute_loss`, `dispute_fee`) have partial unique indexes on `(tenant_id, source_kind, source_id)`. `refund` does not — so a hypothetical second click of "Process refund" that somehow gets past the queue check and Stripe-de-dup would write a second refund row. Today the queue's status filter is the only safety net. | Add `CREATE UNIQUE INDEX uk_tenant_ledger_entry_refund_per_source ON tenant_ledger_entry (tenant_id, source_kind, source_id) WHERE entry_kind = 'refund' AND source_kind IS NOT NULL AND source_id IS NOT NULL;` in a new migration. |
| **Low** | `webapi/Payments/StripePaymentProvider.cs:9-22` | **`StripeConfiguration.ApiKey` is set to a process-global static at construction; `_webhookSecret` is captured by the singleton.** Secret rotation requires a process restart. This is documented industry practice for the Stripe SDK (the static is unavoidable) and a singleton is the right lifetime — but worth a comment so future readers don't try to make it transient. | Add a comment in `Program.cs` next to `AddSingleton<IPaymentProvider, StripePaymentProvider>()` noting that the lifetime matches the SDK's global config + secret-rotation expectation. |
| **Low** | `webapi/Controllers/PaymentController.cs:589-591` (HandleDispute tenant_id fallback) | **`tenantId = passes.FirstOrDefault()?.TenantId ?? tickets.FirstOrDefault()?.TenantId` is correct for single-tenant PIs (which is always the case in practice), but `passId` / `ticketId` only capture the *first* match.** A counter cart that bundles a pass and a ticket on one PI gets the dispute row linked only to the pass; the ticket purchase has no `dispute` row attached. | When the dispute is `lost`, write one `dispute` row per affected purchase (or move `dispute` to a `source_kind`/`source_id` shape that mirrors the ledger). |
| **Low** | `webapi/Controllers/CounterController.cs:573-576` (extras comment) | **Stale TODO comment** says `source_kind='extras' isn't in the tenant_ledger CHECK constraint yet` — that's still true, and is the same issue as Finding 5. Comment is fine but worth deleting after the migration in Finding 5 lands. | — |
| **Info** | Service charge truncation `(int)((long)unitPriceCents * tenantServiceChargeBps / 10_000L)` | Each per-unit service charge truncates toward zero. The platform consistently takes fractionally *less* than the headline percentage (favorable to the tenant) by up to 1 cent per unit-line. Intentional and conservative; not a finding. | — |

## Patterns worth replicating

- **Partial unique indexes for ledger idempotency.** The three indexes `uk_tenant_ledger_entry_*_per_source`
  give every sale/dispute_loss/dispute_fee insert a "swallow 23505" path that survives webhook re-delivery
  without explicit row locks. Cheap, robust, schema-enforced. Extend to `refund` (Finding 21).
- **Snapshotted service-charge / price on the purchase row** (`ServiceChargeCents`, `UnitPriceCentsFrozen`,
  `DailyRateCentsFrozen`, `PriceCents` on `MembershipPurchase`). This makes historical ledger entries stable
  when tenant settings change — important for both audit and refunds.
- **`GetSaleEntryForSource` re-derives refund/dispute amounts from the original sale**, rather than
  recomputing fee math at refund time. Ensures the refund's negation matches the original sale's snapshot.
- **`Account.updated` + `account.charges_enabled`/`payouts_enabled` → status reconciler.** The status
  computation in both `PaymentController.HandleAccount` and `StripePaymentProvider.GetConnectAccountStatus`
  collapses to the same three values, so a stale status from a missed webhook can be re-derived live.
- **`MonthlyPayoutDrafter`** wasn't part of this scope, but its existence (alongside `RefreshTotals` and
  `AttachUnpaidEntries`) is what lets the ledger be append-only while the payout view stays current — worth
  a follow-up that walks the cron + drafting code if not already covered.

## Open questions

1. **Refund ledger math (Finding 9).** The reversal of `RidepassCutCents` on the refund row is what
   I think is wrong, but the canonical answer depends on whether "platform retains the service charge on a
   refund" is a stated business rule. If yes, this is a real ledger-drift bug; if the business rule is
   "platform refunds the service charge along with the rider's principal," then `RefundableCents` is wrong
   instead. The `RefundCalculator` doc-comment says "service charge is never refunded" — confirming the
   former, and pointing the fix at the ledger row.
2. **Are extras truly out of scope for the ledger?** The CounterController comment ("we skip ledger inserts
   for now — matching how the existing extras flow handles ledger writes") reads like a deferred decision
   rather than a permanent one. Finding 5 assumes we want extras in the ledger; if instead the choice is
   "extras don't generate platform fees, so don't ledger them," then `event_extra_purchase` revenue still
   needs to live somewhere reportable.
3. **`tenant_payout` row-level lock under concurrency.** `SendPayoutViaStripe` and `UpdateTenantPayoutStatus`
   both fetch-then-update. Combined with the missing idempotency key (Finding 3) the worst case is a
   duplicate transfer; with the key in place, the worst case becomes one transfer + two paid-status writes
   (idempotent). Confirm the lock-free approach is acceptable once the idempotency-key fix lands.
4. **No retry/backoff on `GetActualStripeFeeCentsAsync`.** When a PI is `requires_capture` at the time of
   webhook (rare for AutomaticPaymentMethods, but possible), the fee fetch returns null and the ledger row
   gets `stripe_fee_cents = 0` permanently. Is there a reconciliation worker we missed that fills these in?
5. **Refund availability for memberships, season passes, rentals (non-deposit), gift cards, extras.** No
   refund UI exists today. Is the policy "those are non-refundable," or "the queue will be expanded later"?
   If the latter, the missing dispute coverage (Finding 11) plus the missing ledger writes (Findings 1, 5)
   compound — a tenant who needs to refund a season pass today has no path.

## Coverage notes

What I verified explicitly:
- Webhook signature verification rejects on empty secret and on signature mismatch (`StripePaymentProvider.cs:228-289`).
- Webhook body is read once via `StreamReader.ReadToEndAsync`; no double-consumption.
- PI lookups across the seven tables (`ListByStripePaymentIntentId`, `GetByPaymentIntentId`,
  `GetPurchaseByStripePaymentIntentId`, etc.) all use the row's own `tenant_id` for downstream tenant-scoped
  work; the unauthenticated webhook never touches `_tenantContext`.
- `StripePaymentProvider` is registered `AddSingleton` — appropriate for the SDK's process-global static.
- No raw PAN / CVC / exp-date handling anywhere in the codebase (grepped: zero matches).
- No `LogInformation` / `LogDebug` of raw bodies, PI client secrets, or full payloads.
- The `voucher OR coupon, not both` constraint is enforced in both `PurchaseController.BuyDayPass` and
  `BuyEventTicket`. Coupon `GetByCode` is `WHERE tenant_id = @tenantId`, so cross-tenant redemption is
  impossible.
- `tenant_ledger_entry`'s `uk_tenant_ledger_entry_sale_per_source` correctly dedupes the
  `payment_intent.succeeded` re-delivery path for passes, tickets, season passes, and memberships.
- Stripe Connect `account.updated` webhook → `UpdateStripeConnectStatus` is keyed on the Stripe account id,
  not subdomain — correct for unauthenticated delivery.
- Transfer reversal handler (`HandleTransferEvent`) reads the prior payout status and skips if already
  `failed` — idempotent against re-deliveries.

What I did *not* re-verify (deferred to Section 1's coverage or beyond Section 2's scope):
- Auth attributes / `IsResolved` guards in the controllers reviewed — Section 1's domain.
- The Vue side of any of these flows (e.g. whether the "Process refund" button is rate-limited / disabled
  during submission).
- `MonthlyPayoutDrafter` and the hosted-service that runs it.
- Reconciliation queries against actual Stripe data (no live keys consulted).
- Counter cart concurrency between two cashiers ringing up the same gift-card-paid rider at the same
  register on different terminals.
