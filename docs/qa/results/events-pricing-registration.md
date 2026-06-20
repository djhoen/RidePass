# QA Results: Events, Pricing Ladders & Registration

Static trace of each case against the current code. Verdicts: PASS / FAIL / NEEDS-LIVE / N/A.
Reviewed 2026-06-20.

## Admin

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| EA1 | PASS | Admin editor persists the quantity ladder: `TicketTiersList.vue:384-385` writes `ladderGroup` + `minSold` (trigger "sold"); repo persists at `EventTicketTierRepository.cs:60-78`; list caption renders group + trigger via `stepTriggerLabel` (`TicketTiersList.vue:454-459`) and the row template at `:34-37`. `openEdit` (`:330-356`) rehydrates the trigger type from `minSold`/`effectiveDaysBefore`/`effectiveAtUtc`, so reopen confirms persistence. On-screen render itself is runtime, but data round-trip is sound. |
| EA2 | PASS | Date ladder: `formToTier` writes `effectiveDaysBefore` for trigger "days" and `effectiveAtUtc` for trigger "date" (`TicketTiersList.vue:386-388`); `openEdit:348-353` restores `triggerType` from those columns. Persistence intact. |
| EA3 | PASS | `EventController.Duplicate` copies every tier incl. ladder steps (`EventController.cs:382-408`); absolute `EffectiveAtUtc` shifted by the +7d clone offset (`:401`, `shift` at `:344`), relative `EffectiveDaysBefore` and `MinSold` copied unchanged (`:399-400`); clone rows are new so sold counts reset to 0. |
| EA4 | PASS | Documented gap reproduced exactly. Public read surfaces cheapest step when no step fires: `EventTicketTierController.cs:63-69`. Checkout rejects: `PriceStepResolver.Resolve` returns null and `PurchaseController.cs:304-307` returns "This ticket isn't available right now." No server-side base-step validation exists, confirming the known gap. |
| EA5 | PASS | Standard tier CRUD + reorder present (`EventTicketTierController.cs:117-230`); delete blocked when sold>0 with "This tier has purchases and cannot be deleted. Set inactive instead." (`:222-226`); reorder at `:199-210`. |
| EA6 | PASS | Bundled-coupon all-or-nothing validation enforced in `ValidateBundledCoupon` (`EventTicketTierController.cs:271-296`): count>0 requires kind+value+scope and race_entry kind. Required rider gate is a separate tier flag (`Required`). |
| EA7 | PASS | Cancel via Update sets `Status` (`EventController.cs:285`); Delete blocked when purchases pin the chain via FK 23503 catch returning "...can't be deleted. Set status to Cancelled instead." (`EventController.cs:322-330`). |
| EA8 | NEEDS-LIVE | Registration data is persisted (rider names + race number via `EventTicketPurchaseRepository.CompleteRegistration` / `PurchaseController.cs:961-967`), but the rider/sales report screen and its query are outside the supplied files; confirm the report renders each rider name + race number on stage. |

## User (buy + register)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| EU1 | PASS | `GetForEvent` collapses each ladder group to its single active step and adds the next-change hint (`EventTicketTierController.cs:56-97`); cheaper sold-out and not-yet-fired steps are not emitted as rows. Vue `stepHint` renders "then $X" / "rises to $X on <date>" (`EventCheckout.vue:441-448`). Visual render is runtime. |
| EU2 | PASS | `GroupSoldCount` counts pending/paid/redeemed (`EventTicketTierRepository.cs:116-127`); `Resolve` picks the highest fired step (`PriceStepResolver.cs:42-49`); `remainingToCapacity = capacity - groupSold` (`EventTicketTierController.cs:73-75`) shrinks as sales rise. |
| EU3 | PASS | No per-step boundary cap: the only hard cap is `groupSold + groupQty > event.Capacity` (`PurchaseController.cs:319-327`); every unit is priced at the active step's `tier.PriceCents` (`:540`), so an order spanning a min_sold boundary is fully honored at the low step and the next buyer re-resolves higher. |
| EU4 | PASS | Cart referencing a non-active ladder step returns 409 `price_changed` with the new price/message (`PurchaseController.cs:309-318`); `EventCheckout.createIntent` catches 409 `price_changed` and surfaces the message (`EventCheckout.vue:540-542`). Refresh/re-submit is a manual reload (component shows the message, does not auto-refresh tiers); the new-price re-submit succeeds because the cart then references the active tier. |
| EU5 | PASS | Sold-out: `remainingToCapacity` clamps to 0 (`EventTicketTierController.cs:73-75`), Vue `soldOut`/`canAdd` block adds (`EventCheckout.vue:430-452`); over-order rejected "Only N spot(s) left for this event." (`PurchaseController.cs:319-326`). |
| EU6 | PASS | Deferred multi-rider checkout: `BuyEventTicket` with `DeferRegistration` creates incomplete rows; `CompleteTicketRegistration` loops registrants assigning gate + class entries with one `registrantId` each (`PurchaseController.cs:944-969`); `GetRegistration` returns still-incomplete entries for resume (`:978-1006`). Full E2E is runtime, logic is complete. |
| EU7 | PASS | Person conflict by name + birthdate across the class returns "person" and the rejection message (`EventTicketPurchaseRepository.cs:386-398`, surfaced at `PurchaseController.cs:919-923`). |
| EU8 | PASS | Race-number conflict returns "number" and the rejection message (`EventTicketPurchaseRepository.cs:400-413`, `PurchaseController.cs:924-928`). |
| EU9 | PASS | Class spans all steps: `classTierIds` is every tier sharing the `LadderGroup` (`PurchaseController.cs:913-915`), so a $50-step entry conflicts with a $65-step registration for the same rider. The in-request dedup also keys on `ladderGroup ?? tierId` (`:898-902`). |
| EU10 | PASS | Documented trade-off reproduced: `rider_birthdate IS NOT DISTINCT FROM @birthdate` (`EventTicketPurchaseRepository.cs:396`) treats two NULL birthdates as equal, so a same-name no-birthdate second rider is rejected as a duplicate. |
| EU11 | PASS | Guest path requires email+name (`PurchaseController.cs:198-204`); authed path resolves the user (`:185-195`); both flow through the same purchase build. Stripe confirmation is runtime. |
| EU12 | PASS | Required rider gate enforced server-side: race entries with a required rider gate and zero gate units rejected "This race requires a rider gate fee..." (`PurchaseController.cs:337-357`); Vue mirrors via `riderGateHint`/`canContinue` (`EventCheckout.vue:416-422`). |
| EU13 | PASS | Waiver required when audience needs it: missing signature rejected (`PurchaseController.cs:939-941`); minor path requires parent/guardian name in the UI (`EventCheckout.vue:655-657, 674-676`) and is stored via `parentGuardianName` (`PurchaseController.cs:966`). |
| EU14 | PASS | Admin Refund cancels + refunds + fires `PromoteNext` on the freed class (`PurchaseController.cs:1284-1290`); admin Cancel (`:1409-1417`) and rider self-cancel (`MeController.cs:415-418`) also promote. (Note: the older "Refund does not promote" gap is now fixed.) |

## Incidental (not a plan case)
- `TicketTiersList.vue:426` uses native `confirm()` for tier delete, which violates the project's no-native-confirm rule. Out of scope for these test cases and left unmodified (QA pass).
