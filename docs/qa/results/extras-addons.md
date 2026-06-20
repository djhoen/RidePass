# QA Results: Extras / Add-ons

Traced against current code on 2026-06-20. Verdicts are static-analysis based (no live browser). Counts: 28 PASS, 0 FAIL, 0 NEEDS-LIVE, 0 N/A.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| EX1 | PASS | `ExtraController.cs:300-341` Create/Update set `Kind = req.Kind.Trim().ToLowerInvariant()` (308, 330), `TenantId = _tenantContext.TenantId` (304); all fields persisted and re-read via `ToResponse`. |
| EX2 | PASS | `ExtraController.cs:110-182` variant create/update persists price + inventory; null price surfaces as inherit in `ToVariantResponse` (571-588); unsold delete returns OK. |
| EX3 | PASS | 23505 caught at `ExtraController.cs:130-133` / `158-161` -> "A variant with the same size / color / gender already exists." Unique index folds NULL to '' at `Script0059_ExtraVariants.sql:37-39`. |
| EX4 | PASS | 23503 caught at `ExtraController.cs:176-180` -> "has purchases on file ... Set inactive instead." FK RESTRICT at `Script0059_ExtraVariants.sql:52`. Inactive variants hidden from rider list via `activeOnly` filter `ExtraController.cs:88`, kept in admin. |
| EX5 | PASS | `ExtraController.cs:362-367` catches 23503 -> "has purchases on file ... Set inactive instead." product_id FK RESTRICT at `Script0054_EventExtras.sql:54`. |
| EX6 | PASS | `ExtraController.cs:567` Remaining = inv.HasValue ? Max(0, inv-sold) : -1; product sold computed across variants/events in `HydrateProductsWithVariants` (80). |
| EX7 | PASS | `ListForAdmin` uses `activeOnly:false` with no expiry filter (`ExtraController.cs:50-53`); rider/POS reject expired (EX17, EX25). |
| EX8 | PASS | `EventExtraRepository.cs:142-147` single UPDATE via unnest with `tenant_id = @tenantId` predicate; foreign ids silently skipped. |
| EX9 | PASS | `ExtraController.cs:194-203` >5MB and non png/jpeg/webp (.gif) rejected 400; png stored under "extra" path (206). |
| EX10 | PASS | `Script0066_ExtrasExpiryInventoryGateFee.sql:22-34` seed trigger inserts Gate Fee ($10/1000c), Camping, Parking, Pit Vehicle for new tenants; backfill (38-44) adds a single Gate Fee for existing tenants. |
| EX11 | PASS | `ExtraController.cs:445-508` one `event_extra_purchase` row per unit, status pending, single PI, `SetPaymentIntentId` per row; finalizer `OnExtrasPaid` flips to paid. |
| EX12 | PASS | `ExtraController.cs:258` "Pick a size/color/gender for ..."; inactive/foreign variant -> "That option isn't available ..." (263). |
| EX13 | PASS | `ExtraController.cs:265-277` "Only N of ... left" / "sold out", label = size/color/gender. |
| EX14 | PASS | `EventExtraRepository.SumSoldProduct` (121-127) has no event predicate -> sums across every event/variant; enforced at `ExtraController.cs:239-249`. |
| EX15 | PASS | `ExtraController.cs:284-294` legacy per-event cap via `SumSold(eventId, productId)`; "sold out for this event". |
| EX16 | PASS | `ExtraController.cs:254-281` returns inside the active-variant branch before the eligibility cap at 284 is reached. |
| EX17 | PASS | `ExtraController.cs:233-235` expiry checked before any inventory math -> "is no longer being sold." |
| EX18 | PASS | `ExtraController.cs:422-433` unsigned -> "requires a signed waiver ..."; signed sets `WaiverSignatureId`; no active waiver proceeds. |
| EX19 | PASS | Distinct token per unit; `EventExtraRepository.MarkRedeemed` (264-273) scoped `tenant_id` + `status='paid'`; `RedemptionController.cs:244` re-checks `x.TenantId != tenantId`; second redeem / foreign tenant is a no-op. |
| EX20 | PASS | `ExtraController.cs:60` GET Products returns empty when ExtrasEnabled false; `377` POST Buy -> "This tenant doesn't sell add-ons." |
| EX21 | PASS | `ExtraController.cs:385` rejects non-scheduled or ended events -> "Event not available." |
| EX22 | PASS | `ExtraController.cs:528-530` keeps null-EventId rows with null event fields; drops rows whose event is missing for the tenant. |
| EX23 | PASS | `PurchaseController.cs:401-426` eligibility per extra, `ResolveExtraVariant` mirrors standalone; combined waiver gate runs once (433); extras totals folded into the single PI. |
| EX24 | PASS | `PurchaseController.cs:408-412` no eligibility row -> "... isn't offered at this event." |
| EX25 | PASS | `CounterController.cs:319-378` Kind=="extras" path: no eligibility check, EventId from item (null), expiry (333) + product cap (337) + variant cap (363) enforced; rejects when ExtrasEnabled false (321). |
| EX26 | PASS | `CounterController.cs:356` "Pick a variant for ..."; `369` "Only N of that variant left", counted tenant-wide via `SumSoldVariant`. |
| EX27 | PASS | Cash path writes ledger row `source_kind='extras'` at `CounterController.cs:652,700-718`; Stripe path via finalizer `OnExtrasPaid` `StripePurchaseFinalizer.cs:354-367`; `Script0099` permits 'extras'. NOTE: the rare 100%-off free-voucher fast path still skips the extras ledger row (`CounterController.cs:741-742`); out of EX27's paid-sale scope but worth tracking. |
| EX28 | PASS | Rider portion = serviceCharge*bps/10000 (`ExtraController.cs:437`), `UnitPriceCentsFrozen` records pre-charge price (462); same math in POS `ComputeWithServiceCharge` and bundled `PurchaseController.cs:420-422`. |
