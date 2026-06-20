# QA Results: Rentals

Traced against current code on 2026-06-20. Verdicts are static-analysis based (no live browser). Counts: 30 PASS, 0 FAIL, 0 NEEDS-LIVE, 0 N/A.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| RN1 | PASS | `RentalController.cs:83-99` creates with `tracking_kind='pool'`, `InventoryPool` persisted when pool (92); rate/deposit edits in Update (124-125); `TenantId` set (85). |
| RN2 | PASS | `RentalController.cs:231-233` ValidateProduct -> "Pooled rentals need a positive inventory." |
| RN3 | PASS | `RentalController.cs:235-237` bps outside 0..10000 -> "Service-charge share must be 0-100%." |
| RN4 | PASS | `RentalController.cs:115-119` blocks tracking_kind change -> "Tracking kind can't be changed after creation."; same-kind field edits still save. |
| RN5 | PASS | `RentalController.cs:182-185` CreateItem on a pool product -> "uses pooled inventory, not per-item tracking"; units created with tenant+product (186-196); unbooked delete OK. |
| RN6 | PASS | `RentalController.cs:220-224` catches 23503 -> "has bookings on file. Set status to retired instead." `rental_purchase_item.item_id` FK RESTRICT at `Script0048_Rentals.sql:126`. |
| RN7 | PASS | `RentalController.cs:157-161` catches 23503 -> "has bookings on file ... Set inactive instead." `rental_purchase.product_id` FK RESTRICT at `Script0048_Rentals.sql:68`. |
| RN8 | PASS | `RentalController.cs:697-698,717-718` end-before-start rejected; valid saves with `tenant_id` (701); maintenance excluded from availability via `PickAvailablePerItemUnits` (`RentalRepository.cs:213-218`). |
| RN9 | PASS | `RentalRepository.cs:116-131` tenant-scoped UPDATE; foreign ids ignored. |
| RN10 | PASS | `RentalController.cs:262-263` PerItemTotal = all units, PerItemAvailable = `status='available'` count (status-only, not date-aware) - matches stated intent. |
| RN11 | PASS | `RentalController.cs:355-393` days = inclusive span (309), amount = rate*days*qty + rider service charge, deposit added (369-370), single PI, token issued (391-393). |
| RN12 | PASS | `RentalController.cs:316-327` "Only N unit(s) available - you asked for N" via `SumOverlappingPoolReserved`; non-overlapping range books (overlap predicate `RentalRepository.cs:230-231`). |
| RN13 | PASS | `RentalController.cs:410-415` PickAvailablePerItemUnits + AssignItems; counter list shows units (`CounterList` 577-586). |
| RN14 | PASS | `RentalController.cs:330-335` "Only N unit(s) available across those dates."; retired/maintenance never offered (`RentalRepository.cs:201-218`). |
| RN15 | PASS | `RentalRepository.cs:213-218` PickAvailablePerItemUnits excludes overlapping maintenance windows. |
| RN16 | PASS | `RentalController.cs:306-310` end<start / start>1-day-past / >30-day span each return the expected messages. |
| RN17 | PASS | `RentalController.cs:340-351` unsigned -> "Please sign the current waiver ..."; signed sets `waiver_signature_id`; no active waiver proceeds. |
| RN18 | PASS | `RentalController.cs:295-299` -> "Please add an emergency contact on your profile before booking a rental." |
| RN19 | PASS | `RentalController.cs:357-364` validates scope "rental"; `RecordRedemption` SourceKind="rental" (397-405); invalid -> validator message. |
| RN20 | PASS | `RentalController.cs:436` partial gift card charges remainder; full coverage (439-453) sets status paid, ClientSecret empty, AmountCents 0. |
| RN21 | PASS | `RentalController.cs:58` GET Products empty when RentalsEnabled false; `289` POST Buy -> "This tenant doesn't offer rentals." |
| RN22 | PASS | `RentalController.cs:516-528` MyRentalResponse carries name/dates/qty/amount/deposit/status; `RentalRepository.cs:381` orders by start_date desc. |
| RN23 | PASS | `RentalRepository.cs:385-396` ListForCounter overlap + `tenant_id` scoped; `RentalController.cs:577-586` per-item rows carry labels + photo fields. |
| RN24 | PASS | `RentalController.cs:592-617` MarkOut paid->out, checked_out_at set (`RentalRepository.cs:304-311`), photos saved per unit; non-paid -> 400 (599). |
| RN25 | PASS | `RentalController.cs:669-678` IsValidPhotoOrNull -> "Photo must be a JPEG/PNG data-url under ~2MB."; null allowed. |
| RN26 | PASS | `RentalController.cs:629-650` captured=0 refunds full deposit; status returned; idempotency key includes id + amount (640). |
| RN27 | PASS | `RentalController.cs:629-665` captured clamped to [0,deposit]; status damaged; only remainder refunded; deposit_captured_cents + notes/photos recorded. |
| RN28 | PASS | `RentalController.cs:642-649` refund failure does NOT flip status (returns before MarkReturned) -> booking stays 'out'; 400 "Could not issue deposit refund via Stripe. Mark the rental returned again ..."; idempotency key (640) makes retry safe. Matches the plan's VERIFY expectation. |
| RN29 | PASS | `RentalController.cs:596-598,623-625` GetPurchase then `TenantId` re-check -> 404 "Rental not found." |
| RN30 | PASS | `RentalController.cs:626-627` only out/paid allowed -> "Can't mark returned a rental with status '...'." |

Recent-fix verification: on PI-creation failure `RentalController.Buy` now calls `RollbackRentalHolds` + `UpdateStatus(...,'failed')` (`RentalController.cs:479-487,748-758`), and the finalizer restores gift-card/coupon holds on rental payment_failed (`StripePurchaseFinalizer.cs:200-204,251-263`). Confirmed present; backs RN19/RN20 robustness.
