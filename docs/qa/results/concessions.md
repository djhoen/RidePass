# QA Results: Concessions / Store

Traced against current code on 2026-06-20. Verdicts are static-analysis based (no live browser). Counts: 20 PASS, 2 FAIL, 0 NEEDS-LIVE, 0 N/A.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| CN1 | PASS | `ConcessionController.cs:60-92` Create/Update set `TenantId` (62), `Category = Trim().ToLowerInvariant()` (65,83); reopens intact. |
| CN2 | PASS | `ConcessionController.cs:65,83` " Drink " -> "drink", "SWAG" -> "swag". |
| CN3 | PASS | `ConcessionController.cs:121-167` variant create/update; null price = inherit (`ToVariantResponse` 412-425); inventory persists; unsold delete OK. |
| CN4 | PASS | 23505 caught at `ConcessionController.cs:137-140` / `161-164` -> "A variant with the same size / color already exists." Unique index `Script0104_Concessions.sql:41`. |
| CN5 | FAIL | `ConcessionController.cs:178-184` catches **23503** and expects FK RESTRICT, but `concession_sale_line.variant_id` is declared **ON DELETE SET NULL** (`Script0104_Concessions.sql:64`). Deleting a sold variant therefore succeeds (no 23503 raised), nulls the sale-line ref, and the "has sales on file ... Set it inactive instead." 400 is never returned. Expected behavior does not occur. |
| CN6 | FAIL | `ConcessionController.cs:98-104` catches **23503** and expects FK RESTRICT, but `concession_sale_line.product_id` is **ON DELETE SET NULL** (`Script0104_Concessions.sql:63`) and `concession_variant.product_id` is ON DELETE CASCADE (28). Deleting a product with sale lines succeeds (cascades variants, nulls sale-line product_id); the "has sales on file ..." 400 is never returned. |
| CN7 | PASS | `ConcessionController.cs:422` Remaining = inv.HasValue ? Max(0, inv-sold) : -1; Sold via `SumSoldVariants` counting pending+paid (`ConcessionRepository.cs:136-148`). |
| CN8 | PASS | `ConcessionRepository.cs:75-84` tenant-scoped UPDATE via unnest; foreign ids ignored. |
| CN9 | PASS | `ConcessionController.cs:192-203` >5MB and non png/jpeg/webp (.gif) rejected 400; webp stored under "concession" path (203). |
| CN10 | PASS | `ConcessionController.cs:123-124,148-149,173-174` GetProduct(id, tenantId) guard -> 404 "Item not found." |
| CN11 | PASS | `ConcessionController.cs:209-218` Items returns active products + active variants hydrated (Hydrate activeOnly), sold counts attached; inactive hidden. |
| CN12 | PASS | `ConcessionController.cs:214-215` Items empty when disabled; `229-230` Sale -> "Concessions aren't enabled for this track." |
| CN13 | PASS | `ConcessionController.cs:242-337` total recomputed (2x300=600), pending sale + lines written, card-present PI created, SaleId/ClientSecret/TotalCents returned; finalizer `OnConcessionPaid` -> MarkSalePaid sets paid_at. |
| CN14 | PASS | `ConcessionController.cs:256` "Choose an option for ..."; foreign/inactive -> "That option isn't available for ..." (259). |
| CN15 | PASS | `ConcessionController.cs:260-270` "Only N of ... left" / "sold out"; qty-2 succeeds and decrements remaining. |
| CN16 | PASS | `ConcessionController.cs:272,277` unitPrice = variant.PriceCents ?? product.PriceCents; snapshots name/variant_label/unit_price frozen on lines (280-289). |
| CN17 | PASS | `ConcessionController.cs:233-238` GroupBy (product,variant) sums, qty>0 filter drops zero lines; empty -> "Cart is empty." |
| CN18 | PASS | `ConcessionController.cs:294` total<50 -> "Sale total must be at least 50 cents." |
| CN19 | PASS | `ConcessionController.cs:296-299` location resolved before CreateSale (310) -> incomplete address returns 400 "Cannot take card-present payments until the track's address is filled in (Settings -> General)." with no dangling pending. |
| CN20 | PASS | `ConcessionController.cs:360-383` EnsureTerminalLocation lazily creates + `SetStripeTerminalLocationId` (381); subsequent sales reuse stored id (363-364). |
| CN21 | PASS | `ConcessionController.cs:324-328` PI-create throw -> `MarkSaleFailed` + 400; failed sale lines no longer hold inventory (`SumSoldVariant` counts only pending+paid, `ConcessionRepository.cs:150-157`). |
| CN22 | PASS | `ConcessionController.cs:301,308` soldBy parsed from token, null when absent. |

Recent-fix verification: abandoned pending concession sales are now swept by the reconciler - `concession_sale` is in the stale-PI union (`PendingPurchaseRepository.cs:42-43`), and on canceled/abandoned the reconciler calls the finalizer with payment_failed which runs `MarkSaleFailed` (`StripePurchaseFinalizer.cs:216-219`), freeing capped-variant stock. Confirmed present; mitigates the plan's top "pending holds inventory indefinitely" risk.
