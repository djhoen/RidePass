# QA Results: Refunds, Cancellations & Disputes

Verified by static code tracing against current source on 2026-06-20. Paths are repo-relative to C:\Users\djhoe\source\repos\RidePass.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| RF1 | PASS | MeController.cs:365-419. bps=10000 -> riderPortion = full serviceCharge, refund = amount - fullServiceCharge (RefundCalculator.cs:19). Refund fires BEFORE Cancel (recent fix), then MarkRefunded with note `stripe_refund={id} amount_cents={cents}` (L411), audit `rider.self_cancel` (L413), response `status="cancelled"` + refundCents/refundId (L419). |
| RF2 | PASS | RefundCalculator.cs:19-21. bps=5000 -> riderPortion = serviceCharge*0.5, refund = amount - serviceCharge*0.5. Rider-paid fraction honored. |
| RF3 | PASS | MeController.cs:358-363. AllowSelfCancel off -> EmitCancelRequest then return `status="request_submitted"`; no DB mutation. Notification `cancel_request` -> /Admin/Purchases and audit `rider.cancel_request` (L428-433). |
| RF4 | PASS | MeController.cs:393 guard `refundCents > 0 && StripePaymentIntentId` skips Stripe; Cancel runs (L409), refundId stays null so no MarkRefunded (L410), returns refundCents=0 (L419). |
| RF5 | PASS | MeController.cs:415-418. PromoteNext(tier.EventId, TierId, LadderGroup) fired on self-cancel (recent fix). Cross-ref waitlist.md W5-W8. |
| RF6 | PASS | MeController.cs:352-353. Non-paid rejected: "Cannot cancel a ticket with status '...'." |
| RF7 | PASS | MeController.cs:349-351. GetById tenant-scoped (tenantId arg) + PurchaserUserId != userId -> NotFound. No cross-tenant/cross-rider mutation. |
| RF8 | FAIL | MeController.cs:401-406. Test Expected (swallow exception, leave ticket `cancelled`, return success) is STALE. Recent fix now returns 400 and leaves the ticket `paid` (refund-first, blocks cancel on failure). Fix confirmed present; test plan Expected and the "Known risks" item need updating. |
| RF9 | PASS | PurchaseController.cs:1393-1418. Admin CancelTicket: Cancel only (no ledger), PromoteNext (L1416), `status="cancelled"`. |
| RF10 | PASS | PurchaseController.cs:1242 default `max(0, amount-serviceCharge)`; Stripe key `refund-event_ticket-{id}-{cents}` (L1270); Cancel+MarkRefunded (L1284-1285); refund ledger row Gross=Net=-cents (L1316-1329). Also now promotes waitlist (L1286-1290). |
| RF11 | PASS | PurchaseController.cs:1242-1244. Explicit AmountCents used; clamped to [0, amount]. |
| RF12 | PASS | PurchaseController.cs:1242-1244,1323-1326. AmountCents=amount -> full refund, ledger negative = full amount. |
| RF13 | PASS | PurchaseController.cs:1292-1301. season_pass Cancel; every non-cancelled reservation -> cancelled (tenant-scoped UpdateReservationStatus); MarkRefunded; ledger row. |
| RF14 | PASS | PurchaseController.cs:1247-1262. loampass RefundAsync by redemption.IdempotencyKey; MarkRefunded only if returned (respects un-redeem result, recent fix); refundCents forced 0 (L1262); purchase refunded; ledger at 0. LoamPassMxService.cs:90-94 returns Success flag. |
| RF15 | PASS | PurchaseController.cs:1278 (cash/voucher: nothing to move, no Stripe), Cancel+MarkRefunded per kind, negative refund ledger row written (L1316). |
| RF16 | PASS | PurchaseController.cs:1302-1309 membership/event_extra Cancel+MarkRefunded; event_extra ledgerSourceKind="extras" while sale kind is event_extra (L1231; Script0099). |
| RF17 | PASS | PurchaseController.cs:1273-1276. Stripe throw -> 400 "Refund failed at the payment processor: ..." returned BEFORE any Cancel/MarkRefunded/ledger (those run after, L1281+). No DB change, no ledger row. |
| RF18 | PASS | PurchaseController.cs:1238-1239. Non-paid -> "Only a paid purchase can be refunded." |
| RF19 | PASS | PurchaseController.cs:1185 [Authorize SalesRefund], :1391 [Authorize SalesCancel]. Cashier lacking the policies -> 403. |
| RF20 | PASS | PurchaseController.cs:1203 (event_ticket GetById tenant-scoped), :1213/1221/1229 (others guard `p.TenantId != tenantId`) -> NotFound. No T2 row touched, no T1 ledger. |
| RF21 | FAIL | PurchaseController.cs:1314-1334 + Script0017/0018. Partial unique indexes exist only for `dispute_loss`/`dispute_fee`, NOT for `entry_kind='refund'`. The `catch (23505)` (L1331) therefore never trips for refunds: a replay (even same amount; MarkRefunded has no status guard) inserts a SECOND negative refund ledger row -> double-debit on the ledger. Stripe key dedups the money move only. Real dedup gap. |
| RF22 | PASS | PaymentController.cs:147-198. Upsert linked to tenant+event_ticket (tenant derived from purchase behind PI); newlyActionRequired on needs_response (L162-164); EmitToSuperAdmins + EmitToTenantAdmins `dispute_opened` with evidence-due (L181-198). Listed via Admin/Disputes (L1421-1444). |
| RF23 | PASS | PaymentController.cs:161-164. newlyActionRequired requires `existing.Status != info.Status` AND status in needs_response/warning_needs_response. Same-status re-fire -> no duplicate notification; only the transition notifies. |
| RF24 | PASS | PaymentController.cs:203-236. status=="lost" -> WriteDisputeLossEntry per ticket mirroring sale (-Gross/-StripeFee/-Cut/-Net, L270-284) + single dispute_fee on first ticket (L228-234, default 1500 / Stripe:DisputeFeeCents L49); dispute_lost notifications to super + tenant admins. |
| RF25 | PASS | PaymentController.cs:258-261,286-290 + Script0017/0018 partial unique indexes on (tenant_id, source_kind, source_id). 23505 swallowed -> dispute_loss and dispute_fee each written once. |
| RF26 | PASS | PaymentController.cs:205-208 (loss for every matched ticket via foreach) and :230-233 (fee only on firstTicket). |
| RF27 | PASS | PaymentController.cs:166-179 upsert updates status to `won`; lost block (L203) skipped -> no loss/fee, no debit. |
| RF28 | PASS | PaymentController.cs:140-144 (empty PI -> log+return) and :152-157 (PI matches no purchase -> tenantId null, log+return). No dispute row, no ledger. |
| RF29 | PASS | PurchaseController.cs:1421 [Authorize DisputesView] -> 403 without it; :1425 ListByTenant(_tenantContext.TenantId) scopes to T1 only. |
| RF30 | PASS | PaymentController.cs:266-267. WriteDisputeLossEntry returns when sale is null (no loss row). NOTE/gap: the dispute_fee block (L228-234) runs independently, so the $15 fee IS still written even with no sale entry. Confirm with product. |
| RF31 | PASS | PurchaseController.cs:1316 (refund row) and PaymentController.cs:203-236 (dispute_loss) post independently; nothing blocks both. Double-debit exposure confirmed as documented; ledger does not guard against refund+loss on the same source. |
| RF32 | FAIL | PurchaseController.cs:1286-1290. Test Expected (KNOWN GAP: Refund does NOT promote waitlist) is STALE. Recent fix added PromoteNext to the Refund endpoint (L1290), so admin Refund now DOES promote. Fix confirmed present; test plan Expected and the "Known risks" item need updating. |
| RF33 | PASS | EventTicketPurchaseRepository.cs:331 `WHERE ... AND status='paid'` makes the second cancel a no-op; identical Stripe idempotency key prevents a second money move. Logic sound; true concurrency timing not exercisable statically. |
| RF34 | PASS | MeController.cs:358 reads tenant.AllowSelfCancel live each call -> branch follows the current setting. Range [5,240] enforced by `[Range(5,240)]` on UpdateCancellationPolicyRequest.cs:10 via [ApiController] model validation. |
| RF35 | FAIL | Self-cancel half STALE: MeController.cs:401-406 now returns 400 and leaves the ticket `paid` (no longer swallows/leaves `cancelled`, per RF8 fix). Admin half holds: PurchaseController.cs:1273-1276 surfaces the Stripe error as 400. Update the self-cancel expectation. |
| RF36 | PASS | PaymentController.cs:147. HandleDispute only queries `_ticketPurchases.ListByStripePaymentIntentId` (event_ticket). A day_pass-only PI matches nothing -> tenantId null -> no dispute row recorded, despite the `day_pass_purchase_id` column (Script0062). Documented gap confirmed. |

## Summary

- 36 cases: 32 PASS, 4 FAIL, 0 NEEDS-LIVE, 0 N/A.
- 3 of the 4 FAILs (RF8, RF32, RF35) are the recent fixes landing correctly: the test plan's Expected text still describes the OLD known-gap behavior, so the plan and its "Known risks" section are stale and should be updated. The fixes themselves are verified present.
- 1 FAIL (RF21) is a genuine, still-open gap: the refund ledger has no `entry_kind='refund'` partial unique index, so the defensive 23505 catch never fires and a refund replay can write a second negative ledger row.
