# QA Results: In-Person POS (Counter) & Stripe Terminal

Source plan: `docs/qa/pos-counter-terminal.md`. Method: static trace of Expected against current code (no live browser/Stripe). Primary file: `webapi/Controllers/CounterController.cs`.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| POS1 | PASS | `FindRider` calls `GetGlobalByEmail` first, then tenant fallback; returns id/name/waiver/minor/contact fields. CounterController.cs:90-133 |
| POS2 | PASS | Tenant-scoped fallback `GetByEmail(_tenantContext.TenantId, ...)`. CounterController.cs:91 |
| POS3 | PASS | Null rider returns 404 "No customer with that email." CounterController.cs:92-95 |
| POS4 | PASS | Waiver state derived from `GetActive` + `GetSignature`; no active waiver leaves `signedCurrent=true`. CounterController.cs:97-116 |
| POS5 | PASS | Minor + parent fields populated; `IsMinor` from `WaiverPolicy.IsMinor`. CounterController.cs:127-130 |
| POS6 | PASS | Creates rider with `TenantId=null`, role "rider", random 32-byte password. CounterController.cs:169-190 |
| POS7 | PASS | Dedup probe (global then tenant) returns 400 on existing. CounterController.cs:147-152 (note: product message contains an em-dash; functional behavior correct) |
| POS8 | PASS | `UserController.IsValidBirthdate` gate. CounterController.cs:153-156; UserController.cs:722-726 |
| POS9 | PASS | Blank name or <7 digit phone rejected via `DigitsOnly`. CounterController.cs:157-162; UserController.cs:728 |
| POS10 | PASS | `request.Email.Trim()` in both Find and Create. CounterController.cs:90-91,144 |
| POS11 | PASS | Every endpoint checks `_tenantContext.IsResolved` -> "No tenant resolved." CounterController.cs:82,139,196,849,880 |
| POS12 | PASS | `RequireEmergencyContact` + blank phone -> 400, before any writes. CounterController.cs:216-219 |
| POS13 | PASS | `totalCents <= 0` -> "Cart total must be positive." CounterController.cs:403-406 |
| POS14 | PASS | Unsupported kind (e.g. "pass") -> "Unsupported cart item kind: <kind>". CounterController.cs:398-401 |
| POS15 | PASS | Ladder resolves via `PriceStepResolver.Resolve`, replaces `tier` with active step; line prices use active `tier.PriceCents`. CounterController.cs:251-263,592-594; PriceStepResolver.cs:36-64 |
| POS16 | PASS | `unitAmount` computed once for active step, `totalCents += unitAmount * Quantity`. CounterController.cs:312-315 |
| POS17 | PASS | Standalone uses `SoldCount`; "Tier '<name>' has only N left." CounterController.cs:276-283; EventTicketTierRepository.cs:105-111 |
| POS18 | PASS | Inactive tier -> "not available"; bad status / `EndsAt<now` -> "already ended." CounterController.cs:237-245 |
| POS19 | PASS | $100/500bps/10000 -> charged 10500, cut 500; 0bps -> charged 10000, cut still 500. CounterController.cs:830-838 |
| POS20 | PASS | One PI for summed total; per-row ticket/extras(QR, frozen attrs)/membership; same intent id stamped. CounterController.cs:562-688,799-813 |
| POS21 | PASS | `!ExtrasEnabled` -> "Add-ons are not enabled at this track." CounterController.cs:321-323 |
| POS22 | PASS | Missing variant -> "Pick a variant"; wrong/inactive -> "That option isn't available". CounterController.cs:352-362 |
| POS23 | PASS | Variant over-qty / product sold out / expired messages. CounterController.cs:333-346,363-371 |
| POS24 | PASS | qty!=1, dup, disabled/price<=0 messages; `validTo`=now+365d only when "yearly". CounterController.cs:380-396,660 |
| POS25 | PASS | race `Quantity>1` rejected. CounterController.cs:290-294 |
| POS26 | PASS | Duplicate race line in cart rejected. CounterController.cs:295-299 |
| POS27 | PASS | `HasActiveRaceEntry` (build + recheck) matches user id or LOWER(email), pending/paid/redeemed. CounterController.cs:300-310,552-559; EventTicketPurchaseRepository.cs:344-369 |
| POS28 | PASS | `classStepIds` from `ladderSteps` checks every step id in the group, not just active. CounterController.cs:300-310 |
| POS29 | PASS | Unsigned + `SignWaiver=false` -> "Rider has not signed the active waiver." CounterController.cs:472-475 |
| POS30 | PASS | `SignWaiver` + valid PNG -> `_waivers.Sign` with remote IP. CounterController.cs:476-494 (live Find-after recheck not executed) |
| POS31 | PASS | `IsValidPngDataUrl` rejects non-PNG / out-of-bounds. CounterController.cs:476-479,823-828 |
| POS32 | PASS | Minor requires parent name + >=7-digit phone, else 400; valid -> signedByParent. CounterController.cs:480-494 |
| POS33 | PASS | Existing signature reused (`waiverSignatureId = existing.Id`), no re-sign. CounterController.cs:496-499 |
| POS34 | PASS | `waiverRequiredByCart` set only by `ev.RequiresRiderWaiver`/`product.RequiresWaiver`; activeWaiver loaded only if required. CounterController.cs:317,378,466 |
| POS35 | PASS | Voucher discounts one unit, placed at index 0 qty 1; total adjusted. CounterController.cs:442-461,569 |
| POS36 | PASS | No ticket line -> "No qualifying line for this voucher...". CounterController.cs:432-440 |
| POS37 | PASS | Ownership / RedeemedAt / inactive-program checks. CounterController.cs:416-428 |
| POS38 | PASS | Cash marks rows paid; ledger per line Gross=charged, RidepassCut=sc, Net=-sc, method "cash"; empty ClientSecret, no Stripe. CounterController.cs:693-731 |
| POS39 | FAIL | Plan expects extras to settle with NO ledger row, but the recent fix now writes one. `extras` is added to `ledgerLines` (CounterController.cs:652) and the cash loop inserts a ledger row for every line including extras with no skip (CounterController.cs:696-718); `extras` is permitted by the constraint (Script0099_ExtrasLedgerSourceKind.sql:6-9). Behavior is the intended fix; the test plan Expected (and the line-90 / risk-note comment) is stale. |
| POS40 | PASS | Cash + voucher: discounted gross in paid ticket; `MarkRedemptionUsed`; ledger uses discounted unit amount. CounterController.cs:447-460,696-724 |
| POS41 | PASS | Method not stripe/cash -> 400; empty defaults to stripe. CounterController.cs:201-205 |
| POS42 | PASS | `SoldByUserId = cashierId` on ticket/extras/membership rows. CounterController.cs:209,582,631,674 |
| POS43 | PASS | Single PI for `totalCents`, usd, receiptEmail, metadata sale_kind=counter/rider_id/tenant_id/item_count; intent stamped; rows stay pending. CounterController.cs:775-813 (actual Stripe confirmation is runtime) |
| POS44 | PASS | `totalCents==0` (non-cash): no PI, rows paid, all-zero ledger method "voucher", redemption used, ClientSecret empty, Total=0. CounterController.cs:734-773 |
| POS45 | PASS | `CreatePaymentIntentAsync` `InvalidOperationException` -> 400 with provider message; rows already created stay pending (stamping not reached). CounterController.cs:783-796 |
| POS46 | PASS | `EnsureTerminalLocation` provisions from tenant address, persists via `SetStripeTerminalLocationId`, returns Secret+LocationId. CounterController.cs:846-870,926-960 (Stripe creation itself is runtime) |
| POS47 | PASS | `EnsureTerminalLocation` returns stored `StripeTerminalLocationId` when present (idempotent). CounterController.cs:929-932 |
| POS48 | PASS | Missing line/city/country/postal -> null -> 400 fill-address message, no token. CounterController.cs:851-855,934-940 |
| POS49 | PASS | Card-present PI scoped to Location; metadata sale_kind=card_present_test/tenant_id/sold_by_user_id (when UserId claim); returns id/secret/amount. CounterController.cs:883-920 (live confirm is runtime) |
| POS50 | PASS | `AmountCents < 50` -> "Amount must be at least 50 cents." CounterController.cs:881 |
| POS51 | PASS | `CreateTerminalConnectionTokenAsync` throw -> 400 provider message. CounterController.cs:857-864 |
| POS52 | NEEDS-LIVE | Mobile reader discovery/tap/confirm requires a live Stripe Terminal reader; stub PI writes no sale rows (matches risk note). |
| POS53 | PASS | Per-event advisory lock + authoritative recheck serialize inserts; spots-left message on overflow. CounterController.cs:514-560 (true near-simultaneous race not executed; mechanism present) |
| POS54 | PASS | Recheck under lock reloads `GroupSoldCount` and rejects with spots-left. CounterController.cs:526-542 |
| POS55 | PASS | Locks acquired in sorted event-id order, released in finally before Stripe. CounterController.cs:514-519,599-603 |
| POS56 | PASS | Build loop sums `cartGroupUnits`+groupSold vs capacity; recheck sums `cartUnits`. CounterController.cs:264-274,537-541 |
| POS57 | PASS | `_tiers.GetById(itemId, TenantId)` returns null cross-tenant -> "not available". CounterController.cs:236-239; EventTicketTierRepository.cs:53-58 |
| POS58 | PASS | GroupSoldCount, HasActiveRaceEntry, ledger Insert all scoped to `_tenantContext.TenantId`. CounterController.cs:256,303-304,537,554,705; EventTicketTierRepository.cs:116-127 |
| POS59 | PASS | Class-level `[Authorize(Policy = SalesCounter)]`. CounterController.cs:26 |
| POS60 | PASS | Location built from this tenant's address fields and stored per tenant via `SetStripeTerminalLocationId`. CounterController.cs:928-959 |

## Summary
- PASS: 58
- FAIL: 1 (POS39)
- NEEDS-LIVE: 1 (POS52)
- N/A: 0

## Notes
- POS39 is the only mismatch and it reflects an intended recent fix (cash extras now write a ledger row). The test plan's Expected and the line-90 / "Known risks" wording should be updated to say extras DO produce a `source_kind='extras'` ledger row on cash sales (Script0099 widened the CHECK constraint).
- Recent fixes confirmed present: active-step price-ladder resolution + `event.capacity` enforcement (POS15/POS16/POS53/POS54/POS56), race-entry uniqueness spanning ladder steps (POS28), and POS cash extras ledger row (POS38/POS39).
- POS7 product copy uses an em-dash in the duplicate-email message; behavior is correct, flagged only as a copy nit.
