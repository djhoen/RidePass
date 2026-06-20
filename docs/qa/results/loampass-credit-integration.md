# QA Results: LoamPass Credit Integration

Verifier: static trace of Expected results against current code (no live browser, no live LoamMx env). Paths are repo-relative to `C:\Users\djhoe\source\repos\RidePass`. Date: 2026-06-20.

Legend: PASS = code implements the Expected. FAIL = code contradicts/omits the Expected (open gap). NEEDS-LIVE = correctness hinges on LoamMx-side state that cannot be observed from RidePass code. N/A = not applicable.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| LP1 [NN] | PASS | Super-admin write: `SuperAdminController.cs:760` -> `TenantRepository.cs:270` (`loampass_mx_destination_id`). Status reports `trackParticipates` from the flag: `RiderLoampassController.cs:49-50,67`. |
| LP2 [NN] | PASS | NULL flag -> `trackParticipates=false` (`RiderLoampassController.cs:50`); redeem rejects "This track doesn't accept Loam Pass credits." `PurchaseController.cs:1052-1054`. |
| LP3 [NN] | PASS | Toggle flips `allow_loampass_redemption`: `EventTypeController.cs:102-104`. Redeem honors it (non-practice): `PurchaseController.cs:1070-1073`. |
| LP4 [NN] | PASS | Practice coerced on: `EventTypeController.cs:102` (`allow = existing.Code == "practice" || request.Allow`); response shows accepted (`:104-105`). Backfill of existing practice rows: `Script0110_LoampassMx.sql:13`. |
| LP5 [R] | PASS | `[Authorize(Policy = CatalogManage)]` on the toggle: `EventTypeController.cs:92-94`. |
| LP6 | PASS | Only the super-admin path writes the destination (`SuperAdminController.cs:729 [SuperAdmin policy] -> :760`). `TenantController.cs:441` only READS it as a bool (`LoampassMxEnabled`); no tenant-admin write exists (grep of `LoampassMxDestinationId` shows only super-admin write). |
| LP10 [NN] | PASS | Link row created tenant-scoped: `RiderLoampassController.cs:98-104` -> `RiderLoampassLinkRepository.cs:32-42`. Status shows linked/accounts/credits: `RiderLoampassController.cs:51-71`. (`creditsAvailable` sum is fetched live from LoamMx; the RidePass aggregation logic is correct.) |
| LP11 [NN] | PASS | `VerifyConfirmAsync` null -> "That code is invalid or expired."; no link written: `RiderLoampassController.cs:95-96`. |
| LP12 [NN] | PASS | Neutral response always `{ sent: true }`: `RiderLoampassController.cs:82-84`. |
| LP13 [NN] | PASS | `ListByUserId` returns all links (`RiderLoampassLinkRepository.cs:21-29`); Status lists all and sums credits across them: `RiderLoampassController.cs:56-62,69`. |
| LP14 [NN] | PASS | `ON CONFLICT (user_id, loampass_account_id) DO UPDATE` refreshes email + `linked_at_utc`: `RiderLoampassLinkRepository.cs:35-41`; unique index `Script0111_RiderLoampassLink.sql:16`. |
| LP15 [NN] | PASS | `DeleteByAccount` scoped to user + tenant + account: `RiderLoampassController.cs:115` -> `RiderLoampassLinkRepository.cs:54-60`. |
| LP16 [NN] | PASS | `!IsConfigured` -> "Loam Pass linking isn't available right now.": `RiderLoampassController.cs:79`. |
| LP17 [R] | PASS | Class-level `[Authorize]`: `RiderLoampassController.cs:20`. Unauthenticated -> 401 from auth middleware (the "Invalid token." string is the token-present-but-no-claim case at `:47` etc.). |
| LP18 | PASS | Link reads are tenant-scoped: `ListByUserId` (`RiderLoampassLinkRepository.cs:24-27`) and `GetUserIdByAccount` (`:44-51`) both filter `tenant_id`. |
| LP20 [NN] | PASS | Creates $0 `loampass_credits` pending (`PurchaseController.cs:1102-1114`), records `loampass_redemption` status=redeemed (`:1136-1144`), marks paid (`:1147`), $0 ledger all-cents-zero (`:1150-1163`). Returns `CreatePurchaseResponse` amountCents=0 (`:1170-1177`). Network redeem requires a reachable LoamMx but the RidePass write path matches Expected. |
| LP21 [NN] | NEEDS-LIVE | "Balance drops by exactly 1" is LoamMx-side state. RidePass redeems once: it loops links and `break`s on the first `Redeemed` (`PurchaseController.cs:1122-1127`), so at most one decrement. The balance assertion needs a live LoamMx. |
| LP22 [NN] | PASS | Draw loops linked accounts, skips ones that return not-redeemed, charges the first success and records that account id: `PurchaseController.cs:1120-1144` (`loampass_redemption.loampass_account_id = chargedAccountId`). Exercising the actual draw needs live LoamMx but the selection logic is correct. |
| LP23 [NN] | PASS | No account redeemed -> pending row set `cancelled`, no redemption row, error surfaced: `PurchaseController.cs:1128-1133` ("No Loam Pass credits available." or LoamMx error). No $0 paid entry. |
| LP24 [NN] | NEEDS-LIVE | LoamMx `alreadyProcessed` semantics need a live env. RidePass-side dedupe is verifiable: `loampass_redemption.event_ticket_purchase_id` is UNIQUE (`Script0112_LoampassRedemption.sql:9`) and the ledger insert swallows `23505` (`PurchaseController.cs:1165-1168`). Note: each RedeemLoampass call mints a fresh purchaseId, so a true same-key retry only occurs within one call. |
| LP25 [NN] | PASS | Advisory lock `event-capacity:{eventId}` wraps the dedupe + pending insert (`PurchaseController.cs:1091-1115`); second submit sees the first pending row via `HasActiveRaceEntry` (status IN pending/paid/redeemed: `EventTicketPurchaseRepository.cs:351-361`) -> "You're already entered in this class." (`PurchaseController.cs:1099-1100`). |
| LP26 [NN] | PASS | After a paid/redeemed entry, `HasActiveRaceEntry` returns true -> rejected, no extra credit: `PurchaseController.cs:1099-1100`. |
| LP27 [NN] | PASS | Under the lock, `sold + 1 > tier.Inventory` -> "'{tier}' is sold out.": `PurchaseController.cs:1093-1097`. |
| LP28 [NN] | PASS | Non-`race_entry` tier -> "Loam Pass credits cover rider entry only.": `PurchaseController.cs:1063-1064`. |
| LP29 [NN] | PASS | Type not accepting (non-practice toggle off) -> "Loam Pass credits aren't accepted for this event."; practice always allowed: `PurchaseController.cs:1070-1073`. |
| LP30 [NN] | PASS | `ev.Status != "scheduled"` -> "Event not found.": `PurchaseController.cs:1066-1068`. |
| LP31 [NN] | PASS | Active unsigned waiver -> "You must sign the current waiver before redeeming a credit for this entry."; sign then retry succeeds: `PurchaseController.cs:1076-1081`. |
| LP32 [NN] | PASS | No link -> "Connect your Loam Pass on your profile first.": `PurchaseController.cs:1056-1058`. |
| LP33 [NN] | PASS | NULL destination -> "This track doesn't accept Loam Pass credits.": `PurchaseController.cs:1052-1054`. |
| LP34 [NN] | PASS | Unreachable -> `PostAsync` returns null -> `RedeemAsync` returns Redeemed=false / "Could not reach LoamPassMx." (`LoamPassMxService.cs:76-77`); controller cancels the pending row and surfaces the error, no redemption row, no orphaned $0 paid entry: `PurchaseController.cs:1128-1133`. |
| LP35 [NN] | PASS | `v_recent_sales` event_ticket branch selects every `event_ticket_purchase` regardless of payment_method, with tier name + purchaser name (`Script0080_RecentSalesView.sql:42-56`); Admin list reads it (`PurchaseController.cs:1376`). $0 ledger row is all-zero cents (`:1150-1163`) so revenue is not inflated. Visual report check still advised live. |
| LP36 [R] | PASS | GateCheckIn flips an EXISTING paid race_entry to redeemed, never redeems a credit; second scan -> "already checked in.": `RiderLoampassController.cs:139-158`. |
| LP40 [NN] | PASS | Refund calls `RefundAsync(idempotency_key)` and only `MarkRefunded` when it returns true (recent fix): `PurchaseController.cs:1250-1262`; ticket cancelled + waitlist promote (`:1283-1290`), refund ledger (`:1316-1329`), `refundCents` forced 0 (`:1262`). LoamMx credit restore needs live but RidePass path matches Expected. |
| LP41 [NN] | PASS | Second refund: `redemption.Status != "refunded"` guard is false -> Unredeem + MarkRefunded skipped: `PurchaseController.cs:1251`. |
| LP42 [NN] | NEEDS-LIVE | LoamMx balance restoration is LoamMx-side state; not observable from RidePass code. |
| LP43 [NN] | PASS | `status != "paid"` -> "Only a paid purchase can be refunded.": `PurchaseController.cs:1238-1239`. |
| LP44 | PASS (drift resolved) | Recent fix closes the documented drift. `RefundAsync` returns false when LoamMx is unreachable (`LoamPassMxService.cs:90-93`, PostAsync null on `:129-147`); Refund now bails with "Couldn't return the Loam Pass credit..." and does NOT `MarkRefunded` or cancel: `PurchaseController.cs:1256-1260`. The test-plan Expected describes the pre-fix behavior and is now stale. |
| LP45 [!!] | PASS (gap resolved) | Recent fix closes the self-cancel gap. `MeController.CancelMyTicket` now has a `loampass_credits` branch that un-redeems via `RefundAsync` and bails (keeping the ticket) if the credit can't be returned: `MeController.cs:373-392`. Only on a successful un-redeem does it cancel. The test-plan Expected (credit lost) is now stale. |
| LP46 [R] | PASS | `[Authorize(Policy = SalesRefund)]` on Refund: `PurchaseController.cs:1185-1186`. |
| LP50 [NN] | FAIL | Confirmed open gap. Redeem dedupe is per-tier only: `HasActiveRaceEntry(tenantId, tier.Id, userId, null)` at `PurchaseController.cs:1099`. It does NOT use the ladder-group-spanning `FindRaceClassConflict` (`EventTicketPurchaseRepository.cs:378`) that registration uses. A rider can redeem step-1 then step-2 of the same `ladder_group` and double-spend a credit (two entries in one class). |
| LP51 [NN] | FAIL | Confirmed open gap. Redeem checks only `tier.Inventory.HasValue` (`PurchaseController.cs:1093-1098`); it never consults `event.capacity` / `GroupSoldCount` the way the card buy flow does (`PurchaseController.cs:319-327`). A `race_entry` tier with NULL inventory on an at-capacity event can be oversold via credit redeem. |
| LP52 [NN] | PASS | `GetCreditsAsync` swallows the error and returns 0 (`LoamPassMxService.cs:50-67`), so `creditsAvailable` shows 0 not an error (`RiderLoampassController.cs:56-62`). Backend confirmed; the "UI does not imply no-credits misleadingly" part is a visual judgment to spot-check live. |
| LP53 [NN] | PASS | Same path as LP23/LP34: a failed draw cancels the pending row and surfaces a message, no $0 paid entry: `PurchaseController.cs:1128-1133`. |
| LP54 [NN] | PASS | Redeem with `!IsConfigured` -> RedeemAsync "LoamPassMx integration is not configured." (`LoamPassMxService.cs:72-73`) -> pending cancelled (`PurchaseController.cs:1128-1133`). Linking -> LinkStart unavailable message (`RiderLoampassController.cs:79`). |
| LP55 [R] | PASS | `ParsePassId` accepts full `{issuer}/QR/{passId}` URL and a bare id (`RiderLoampassController.cs:162-167`); unknown pass -> "That Loam Pass wasn't recognized." (`:132`); recognized-but-not-linked -> connect-in-profile message (`:135-137`). |
| LP56 | PASS | Tenant-scoped lookups: Refund `GetById(PurchaseId, tenantId)` (`PurchaseController.cs:1203`), `GetByPurchaseId(.., tenantId)` (`LoampassRedemptionRepository.cs:30-38`), `MarkRedeemed ... WHERE tenant_id` (`EventTicketPurchaseRepository.cs:145-149`). A foreign-tenant purchaseId resolves to not-found; no cross-tenant mutation. |

## Summary of headline-fix verification
- Admin Refund respects the `RefundAsync` bool: confirmed at `PurchaseController.cs:1256-1262` (bails, does not mark refunded, when Unredeem fails) -> LP40/LP44.
- Rider self-cancel now un-redeems the credit: confirmed at `MeController.cs:373-392` (un-redeems, keeps ticket if it can't) -> LP45.
- Gate check-in is waiver-gated: confirmed at `RiderLoampassController.cs:149-155` -> LP36.
- Still-open: per-tier (not class-wide) redeem dedupe -> LP50; `tier.Inventory`-only capacity check (no `event.capacity`) -> LP51.
