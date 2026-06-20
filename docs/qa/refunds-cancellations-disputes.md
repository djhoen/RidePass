# QA Test Plan: Refunds, Cancellations & Disputes

> Scope: rider self-cancel vs cancel-request, admin cancel + admin refund, refundable-amount math (service-charge handling), Stripe refund idempotency, waitlist promotion on a freed spot, dispute lifecycle + tenant ledger impact, and tenant isolation. Last updated: 2026-06-20.

## Surface map
- **Rider self-cancel:** `MeController.CancelMyTicket` (`POST api/Me/Purchases/Ticket/{id}/Cancel`, ~L336). Two branches on `tenant.AllowSelfCancel`; the disabled branch calls `EmitCancelRequest` (~L388). Event tickets only.
- **Admin cancel (no money):** `PurchaseController.CancelTicket` (`POST api/Purchase/Ticket/{id}/Cancel`, ~L1380; policy `SalesCancel`). Fires `_waitlistPromoter.PromoteNext` on the freed class.
- **Admin refund (money):** `PurchaseController.Refund` (`POST api/Purchase/Refund`, ~L1187; policy `SalesRefund`). Kinds: `event_ticket`, `season_pass`, `membership`, `event_extra`. Does **not** promote the waitlist.
- **Refund math:** `Services/Helpers/RefundCalculator.cs` (`RefundableCents(amount, serviceCharge, riderPaidServiceChargeBps)`).
- **Disputes:** `PurchaseController.ListDisputes` (`GET api/Purchase/Admin/Disputes`, ~L1408; policy `DisputesView`). Webhook ingest: `PaymentController.HandleDispute` (~L138) from `charge.dispute.*`. Ledger writers `WriteDisputeLossEntry` (~L264) and `WriteDisputeFeeEntry` (~L239).
- **Stripe:** `webapi/Payments/StripePaymentProvider.cs` (`RefundAsync(intentId, cents, idempotencyKey, ct)`).
- **Tenant settings:** `TenantController.UpdateCancellationPolicy` (`PUT api/Tenant/CancellationPolicy`, policy `SettingsManage`) sets `AllowSelfCancel` + waitlist fields.
- **Repos:** `EventTicketPurchaseRepository.Cancel` (paid → cancelled, tenant-scoped), `.MarkRefunded` (→ refunded), `DisputeRepository.Upsert/ListByTenant`, `TenantLedgerRepository`.
- **Migrations:** `Script0051_TenantCancelAndWaitlistSettings.sql`, `Script0010_Disputes.sql`, `Script0017_DisputeLossLedger.sql`, `Script0018_DisputeFeeLedger.sql`.
- **Frontend:** `src/views/Admin/Purchases.vue` (admin refund/cancel + dispute list), rider My Passes cancel control.

## Concepts under test
- **Two rider paths.** When `AllowSelfCancel = true`, the rider cancels inline and a partial Stripe refund fires. When `false`, no DB change happens: a `cancel_request` notification + audit row are emitted to tenant admins and the API returns `status = "request_submitted"`.
- **Service-charge withholding differs by path.** Rider self-cancel uses `RefundCalculator`: it withholds only the rider-paid fraction of the service charge (`serviceChargeCents * riderPaidServiceChargeBps / 10000`, default bps `10000` = rider paid 100%). Admin refund's default withholds the **entire** `serviceChargeCents` (`amount - serviceCharge`), but an admin may override `AmountCents` to anything in `[0, amount]`.
- **Status transitions.** `Cancel` flips `paid → cancelled` only `WHERE status='paid'` (so a re-run is a no-op). A refund then `MarkRefunded` flips it to `refunded`. A fully refunded ticket ends at `refunded`; an admin "Cancel" with no refund ends at `cancelled`.
- **Stripe idempotency key embeds the amount:** self `refund-ticket-{id}-{cents}`, admin `refund-{kind}-{purchaseId}-{cents}`. Re-submitting the same amount is a no-op at Stripe; a different amount is a new refund.
- **Disputes are ingested, never initiated.** Stripe `charge.dispute.*` upserts a `dispute` row (unique `stripe_dispute_id`), linked to the tenant via the purchase behind the PaymentIntent. A `lost` dispute writes a negative `dispute_loss` ledger entry (mirror of the sale) plus a flat `dispute_fee` (default $15, `Stripe:DisputeFeeCents`). Both are idempotent via partial unique indexes on `(tenant_id, source_kind, source_id)`.
- **Waitlist coupling.** Only `CancelTicket` (admin) and rider self-cancel call `PromoteNext`; the admin **Refund** endpoint does not (see Known risks). See `waitlist.md` for promotion depth.

## Preconditions / test data
- Two tenants, **T1** and **T2**, for isolation checks. T1 with Stripe (test mode) wired; a rider account **R1** with a saved paid event ticket; a second rider **R2**.
- T1 ticket tiers covering both fee splits: a tier with `RiderPaidServiceChargeBps = 10000` (rider paid the whole fee) and one with `5000` (split 50/50), each with a non-zero `ServiceChargeCents`.
- A paid ticket with `PaymentMethod = stripe`, one with `loampass_credits`, one `cash`, and one `voucher` (zero-dollar) for the no-money-moved branches.
- A full price-ladder class on T1 with at least one waiter queued (per `waitlist.md`) so refund/cancel can free a spot.
- Tenant toggle `AllowSelfCancel` flippable via Admin -> Settings; a manager role (`SalesRefund` + `SalesCancel` + `DisputesView`) and a cashier role (no refund/cancel) for permission checks.
- Stripe CLI (or dashboard) able to fire `charge.dispute.created/updated/closed` against a real test PaymentIntent backing a T1 ticket.

---

## Rider self-cancel

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RF1 [NN] | Self-cancel allowed, full-fee tier | `AllowSelfCancel = on`. R1 cancels a paid stripe ticket on a `bps=10000` tier (fee fully rider-paid) | Ticket → `cancelled` then `refunded`; refund = `amount - fullServiceCharge`; Stripe refund issued; `refund_note` records `stripe_refund=… amount_cents=…`; audit `rider.self_cancel`; response `status="cancelled"` with `refundCents`/`refundId`. |
| RF2 [NN] | Self-cancel allowed, split-fee tier | Same, on a `bps=5000` tier | Refund withholds only **half** the service charge (`amount - serviceCharge*0.5`), confirming `RefundCalculator` honors the rider-paid fraction. |
| RF3 [NN] | Self-cancel disabled -> request only | `AllowSelfCancel = off`. R1 taps cancel | No DB change to the purchase (stays `paid`); response `status="request_submitted"`; tenant admins get a `cancel_request` notification linking `/Admin/Purchases`; audit `rider.cancel_request`. No Stripe refund. |
| RF4 [NN] | Free / zero-refund self-cancel | Self-cancel a ticket whose computed `refundCents = 0` (e.g. amount equals withheld fee) or a voucher/$0 ticket | Ticket is `cancelled`; no Stripe call (guarded by `refundCents > 0 && StripePaymentIntentId`); `refundCents=0` returned; not marked `refunded`. |
| RF5 [NN] | Self-cancel frees a waitlist spot | R1 self-cancels a paid ticket in a full ladder class with a queued waiter | `PromoteNext(eventId, tierId, ladderGroup)` fires; the class waiter is promoted (cross-ref `waitlist.md` W5 to W8). |
| RF6 [R] | Not-paid ticket | Try to self-cancel a `redeemed`, `cancelled`, or `refunded` ticket | Rejected: "Cannot cancel a ticket with status '…'." |
| RF7 [R] | Not my ticket / wrong tenant | R2 calls cancel with R1's ticket id; then R1 calls it on the T1 subdomain for a T2 ticket id | `Not found` both times (`GetById` is tenant-scoped and `PurchaserUserId` is checked). No cross-tenant or cross-rider mutation. |
| RF8 | Self-cancel when Stripe refund throws | Force the Stripe refund to fail (e.g. already-refunded PI) on an allowed self-cancel | KNOWN GAP: exception is swallowed (logged), ticket is left `cancelled` (not `refunded`), `refundId=null`, response still `status="cancelled"`. Confirm rider is not told money is coming when it is not. See Known risks. |

---

## Admin cancel & refund

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RF9 [NN] | Admin cancel (no money) frees spot | Manager hits `Ticket/{id}/Cancel` on a paid ticket in a full ladder class with a waiter | Ticket → `cancelled` (no refund, no ledger row); `PromoteNext` fires and the waiter is promoted; response `status="cancelled"`. |
| RF10 [NN] | Admin refund default (full fee withheld) | Manager refunds an `event_ticket` with `AmountCents` omitted | Refund = `max(0, amount - serviceCharge)` (entire service charge withheld, unlike self-cancel); Stripe refund w/ key `refund-event_ticket-{id}-{cents}`; ticket `cancelled` then `refunded`; one negative `refund` ledger row (`GrossCents=NetToTenant=-cents`). |
| RF11 [NN] | Admin partial refund override | Refund with explicit `AmountCents` between 0 and amount | Exactly that amount refunded at Stripe and on the ledger; values above `amount` clamp to `amount`, below 0 clamp to 0. |
| RF12 [NN] | Admin full refund override | Refund with `AmountCents = amount` (give back the fee too) | Full amount refunded; ledger negative equals full amount. |
| RF13 [NN] | Season-pass refund releases reservations | Refund a `season_pass` purchase that holds event reservations | Pass `cancelled` + `refunded`; every non-cancelled reservation flipped to `cancelled` so it stops holding spots; refund ledger row written. |
| RF14 [NN] | Loampass-credit refund returns the credit | Refund a `paid` purchase with `PaymentMethod=loampass_credits` | `LoamMx RefundAsync` called by the redemption's idempotency key, redemption marked `refunded`; `refundCents` forced to 0 (no money moved); purchase `refunded`; ledger row at 0. |
| RF15 [R] | Cash / voucher refund | Refund a `cash` then a `voucher` purchase | Purchase `cancelled`+`refunded`; no Stripe call; ledger negative row recorded (cash returned at counter out of band). |
| RF16 [R] | Membership / event_extra refund | Refund each remaining kind | Correct repo `Cancel`+`MarkRefunded` path runs; `extras` uses ledger source-kind `extras` while the sale kind is `event_extra` (verify the ledger nets out against the original sale). |
| RF17 [NN] | Stripe refund failure aborts admin refund | Force the Stripe call to throw on an admin refund | Endpoint returns `400 "Refund failed at the payment processor: …"`; purchase is **not** cancelled/refunded and **no** ledger row is written (unlike the self-cancel swallow). |
| RF18 [R] | Refund a non-paid purchase | Refund something already `refunded`/`cancelled`/`pending` | Rejected: "Only a paid purchase can be refunded." |
| RF19 [R] | Permission gates | Cashier (no `SalesRefund`/`SalesCancel`) calls Refund and Cancel | Both `403`. Manager/admin succeed. |
| RF20 [NN] | Cross-tenant refund blocked | T1 manager refunds a T2 `PurchaseId` (each kind) | `Not found` (event_ticket via tenant-scoped `GetById`; others via `p.TenantId != tenantId` guard). No T2 row touched, no T1 ledger row. |
| RF21 | Idempotent refund ledger | Replay the exact same admin refund request twice | Stripe key (same amount) makes the money move once; verify whether a second `refund` ledger row is written (see Known risks: no partial unique index for `entry_kind='refund'`). |

---

## Disputes

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RF22 [NN] | Dispute opened -> recorded + notified | Fire `charge.dispute.created` (status `needs_response`) against a T1 ticket's PaymentIntent | `dispute` row upserted (linked to T1 + the event_ticket purchase); super-admins and T1 tenant-admins each get a `dispute_opened` notification (with evidence-due date if present). Visible in `GET Admin/Disputes`. |
| RF23 [NN] | Status flip notifies once | Re-fire the same dispute at the same status, then flip to a new action-required status | No duplicate notification on a same-status re-fire; a notification only on the transition into `needs_response`/`warning_needs_response`. |
| RF24 [NN] | Dispute lost -> loss ledger + fee | Fire `charge.dispute.closed` with status `lost` | Negative `dispute_loss` ledger entry mirroring the original sale (`-Gross/-StripeFee/-Cut/-Net`) for each linked line item; a single `dispute_fee` entry of `Stripe:DisputeFeeCents` (default $15, `NetToTenant=-fee`) tied to the first ticket; `dispute_lost` notifications to super-admins (+ tenant admins). Tenant balance drops by sale + fee. |
| RF25 [NN] | Lost-dispute idempotency | Re-deliver the `lost` webhook 2 to 3 times | `dispute_loss` and `dispute_fee` each written exactly once (partial unique indexes on `(tenant_id, source_kind, source_id)`); retries hit `23505` and are swallowed. No double debit. |
| RF26 [NN] | Counter-cart multi-item dispute | Dispute a PaymentIntent that backed several tickets in one cart | A `dispute_loss` is written for **every** matched ticket; the `dispute_fee` is written only once (first ticket). |
| RF27 [NN] | Dispute won | Fire `charge.dispute.closed` with status `won` | `dispute` row updated to `won`; no `dispute_loss`/`dispute_fee` written; no debit. |
| RF28 | No-PI / unmatched dispute | Fire a dispute with empty `payment_intent`, then one whose PI matches no purchase | Logged warning, no `dispute` row written, no ledger impact (cannot resolve tenant). Confirm this is acceptable vs. an orphan record. |
| RF29 [R] | Disputes list permission + isolation | T1 accountant (has `DisputesView`) lists; a role without it; then confirm T1 list excludes T2 disputes | `403` without the permission; `Admin/Disputes` is scoped to `_tenantContext.TenantId` so only T1 disputes appear. |
| RF30 | Loss with missing sale entry | Lost dispute where no `sale` ledger entry exists for the source | `WriteDisputeLossEntry` returns without writing (guards on `sale is null`); document whether the fee should still apply. |

---

## Edge

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RF31 | Refund then dispute on same purchase | Admin refunds a ticket, then a `lost` dispute arrives for the same PaymentIntent | Both a negative `refund` and a negative `dispute_loss` post; quantify the double-debit exposure and confirm intended handling (Stripe would not normally dispute a refunded charge, but the ledger does not block it). |
| RF32 | Admin refund does not promote waitlist | Refund the last paid ticket of a full ladder class that has a queued waiter (via the Refund endpoint, not Cancel) | KNOWN GAP: spot is freed but `PromoteNext` is never called, so the waiter is not promoted. Contrast with RF9/RF5. See Known risks. |
| RF33 | Concurrent double-cancel | Two near-simultaneous cancels of the same paid ticket | `Cancel`'s `WHERE status='paid'` makes the second a no-op; only one refund fires (same Stripe key). No double money movement. |
| RF34 | Toggle policy then act | Flip `AllowSelfCancel` off then on and exercise RF1/RF3 back to back | Branch selection follows the live tenant setting each call; `WaitlistConfirmWindowMinutes` range `[5,240]` enforced by `UpdateCancellationPolicy`. |
| RF35 | Already-refunded Stripe PI | Self-cancel/admin-refund a PI Stripe has already fully refunded out of band | Self-cancel swallows + leaves `cancelled` (RF8); admin refund surfaces the Stripe error as a 400 (RF17). Confirm DB does not claim a refund that did not happen. |
| RF36 | Day-pass dispute linkage | Fire a dispute for a `day_pass` PaymentIntent | `HandleDispute` only matches `event_ticket` purchases, so a day-pass-only dispute resolves no tenant and is not recorded even though `dispute` schema has a `day_pass_purchase_id` column. Document the gap. |

---

## Known risks / watch-items
- **Self-cancel swallows Stripe refund failure (RF8).** `CancelMyTicket` catches the refund exception, logs it, leaves the ticket `cancelled` (never `MarkRefunded`), and still returns `status="cancelled"`. The rider sees a successful cancel with no money returned and no retry path. Recommend surfacing failure or queueing a retry, and not flipping status until the refund clears.
- **Admin Refund does not promote the waitlist (RF32).** Only `CancelTicket` and rider self-cancel call `PromoteNext`. Refunding the last paid ticket frees a class spot silently. Either route the refund through the same promotion call or document that admins must use Cancel to free a waitlisted spot.
- **Inconsistent service-charge default (RF2/RF10).** Self-cancel withholds only the rider-paid fraction (`RefundCalculator`), while the admin refund default withholds the entire service charge. Same ticket, different refund depending on who cancels. Confirm this is intended.
- **Refund ledger may not be deduped (RF21).** Partial unique indexes exist for `dispute_loss` and `dispute_fee` but not for `entry_kind='refund'`. The `catch (23505)` in `Refund` is defensive only; two refund submissions at different amounts (or a missing constraint) could write two negative rows. Verify the ledger nets correctly and consider a `refund`-per-source guard.
- **`MarkRefunded` is not tenant-scoped.** `UPDATE … WHERE id=@id` has no `tenant_id` predicate and no status guard; safe today only because the controller pre-validates ownership via a tenant-scoped `GetById`. Adding a `tenant_id` predicate would make it defense-in-depth.
- **Money correctness on refund + dispute overlap (RF31)** and **day-pass disputes never recorded (RF36)** are open ledger/coverage gaps to confirm with product.
- **Multi-tenant isolation:** refund (all kinds), self-cancel, and the disputes list are tenant-scoped; dispute webhook ingest derives the tenant from the purchase rather than trusting request input. Re-verify after any change to `GetById`/`ListByStripePaymentIntentId` scoping.
- Cross-reference **`waitlist.md`** (W5 to W10) for freed-spot promotion and **`events-pricing-registration.md`** (EU14) for the buyer-side cancel/refund entry point.
