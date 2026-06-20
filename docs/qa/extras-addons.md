# QA Test Plan: Extras / Add-ons

> Scope: add-on product + variant CRUD, tenant-wide and per-event inventory caps, expiry, the gate-fee/spectator default products, three purchase paths (standalone, bundled with a ticket, POS counter), per-unit QR redemption, and tenant isolation. Last updated: 2026-06-20.

## Surface map
- **Admin (CatalogManage):** `ExtraController` product CRUD (`Products/Admin`, `POST/PUT/DELETE Products`, `Products/Reorder`), variant CRUD (`Products/{productId}/Variants` GET/POST, `PUT/DELETE .../Variants/{variantId}`), image upload (`POST Image`). Per-event eligibility + per-event inventory is edited through the event editor (`EventExtraRepository.ReplaceEligibility`, `event_extra_eligibility`).
- **User:** `ExtraController.ListActive` (`GET Products`, gated on `tenant.ExtrasEnabled`), `ExtraController.Buy` (`POST Buy`, standalone, authed, attached to an event), `ExtraController.ListMine` (`GET Mine`).
- **Bundled checkout:** `PurchaseController` buys extras alongside an event ticket (`ResolveExtraVariant`, `request.Extras`, per-event eligibility enforced, one `event_extra_purchase` row per unit).
- **POS:** `CounterController` extras path (`item.Kind == "extras"`): sells add-ons as untethered merchandise, no event, no eligibility check, variant + product inventory still enforced.
- **Redemption:** each purchased unit gets its own `redemption_token`; `EventExtraRepository.MarkRedeemed` (tenant + status='paid' scoped) backs the SalesRedeem gate scan. `Cancel` / `MarkRefunded` handle voids.
- **Inventory logic:** `ExtraController.ResolveVariantOrError` (product expiry, tenant-wide product cap, variant cap, legacy per-event cap), `SumSoldProduct` / `SumSoldVariant` / `SumSold` (all count status IN ('paid','redeemed')).
- **Repo / migrations:** `EventExtraRepository`, `Script0059_ExtraVariants.sql` (variants, unique attr index, frozen attrs), `Script0066_ExtrasExpiryInventoryGateFee.sql` (expires_at, product inventory, tier/description, Gate Fee seed + backfill), `Script0099_ExtrasLedgerSourceKind.sql` (adds 'extras' to the ledger source_kind check).

## Concepts under test
- A **product** (`event_extra_product`) has a kind (`gate_fee`, `camping`, `parking`, `pit_vehicle`, or freeform), a base `price_cents`, an optional `expires_at`, an optional tenant-wide `inventory`, `requires_waiver`, and `rider_paid_service_charge_bps` (the rider's share of the service charge).
- A product with at least one **active variant** (`event_extra_variant`: size / color / gender / sku / tier / description) becomes variant-required at purchase. Variant price (`price_cents`) and image are null-inherit from the product. Variant `inventory` is tenant-wide (one physical stock count across every event and order); null = unlimited.
- **Inventory layering** at purchase: expiry first, then the tenant-wide product cap (`SumSoldProduct`), then either the variant cap (`SumSoldVariant`) when active variants exist, or the legacy per-event eligibility cap (`SumSold`) when they do not. Sold counts include only `paid` and `redeemed` rows; `pending` and `cancelled` do not hold stock.
- **Eligibility:** the standalone and bundled paths require an `event_extra_eligibility` row tying the product to the event; the POS path does not (counter merch is untethered, `EventId` null).
- One `event_extra_purchase` row is written **per unit** so each unit carries its own QR `redemption_token`; variant attributes are frozen (`size_at_purchase` etc.) at sale time.

## Preconditions / test data
- A tenant with `ExtrasEnabled = true`; a second tenant for isolation checks. A third tenant with `ExtrasEnabled = false`.
- A scheduled, future event (`status='scheduled'`, `EndsAt` in the future) with eligibility rows for the products under test.
- Products: (a) a simple no-variant product with a per-event inventory cap of 5; (b) a T-shirt product with three active size variants, one variant capped at `inventory = 2`, one with null (unlimited) inventory, plus an inactive variant; (c) a product with a tenant-wide `inventory = 3`; (d) a product with `expires_at` in the past; (e) the seeded Gate Fee product; (f) a product with `requires_waiver = true`.
- An active waiver for the tenant, a rider with a signed signature and a rider without.
- Two rider accounts plus a counter/staff user with SalesCounter and SalesRedeem.

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| EX1 [NN] | Product CRUD | Create, edit, then re-open a product setting name, kind, base price, rider service-charge share, requires-waiver, sort order | Saves and reopens with every field intact; `kind` stored lower-cased and trimmed; created with `tenant_id` = current tenant. |
| EX2 [NN] | Variant CRUD | Add three size variants under product (b); edit one price and inventory; delete an unsold one | Each variant saves; per-variant price/inventory persist; null price shows "inherit"; delete of an unsold variant succeeds. |
| EX3 [NN] | Duplicate variant attrs blocked | Create a variant with the same (size, color, gender) tuple as an existing one (try NULL color vs '' too) | 400 "A variant with the same size / color / gender already exists." Unique index folds NULL to '' so NULL-color dupes are also caught. |
| EX4 [NN] | Delete variant with sales | Sell one unit of a variant, then attempt to delete that variant | 400 "has purchases on file ... Set inactive instead." (FK RESTRICT). Set inactive instead and confirm it disappears from rider list but stays in admin list. |
| EX5 [NN] | Delete product with sales | Attempt to delete a product that has paid purchases | 400 "has purchases on file ... Set inactive instead." (FK RESTRICT). |
| EX6 [NN] | Tenant-wide inventory cap | Set product (c) `inventory = 3`; admin list shows Sold / Remaining | Admin list `Remaining` reflects `inventory - SumSoldProduct`; product with null inventory reports Remaining = -1 (unlimited sentinel). |
| EX7 [NN] | Expiry flag | Set `expires_at` in the future, then in the past; reload admin list | Product stays listed in admin in both states (re-extendable); rider/POS paths reject once expired (see EX12, EX18). |
| EX8 [R] | Reorder products | Drag-reorder the catalog and POST `Products/Reorder` | Single UPDATE applies new sort_order scoped by tenant; reload preserves order; ids from another tenant in the payload are ignored (tenant predicate). |
| EX9 [R] | Image upload limits | Upload a 6 MB file, then a .gif, then a valid .png | >5 MB and unsupported content-type rejected with 400; png returns an imageUrl stored under the tenant's "extra" path. |
| EX10 [R] | Gate Fee seed present | On a freshly created tenant, open the add-on catalog | Gate Fee (kind `gate_fee`, $10), Camping, Parking, Pit Vehicle exist (seed trigger); existing tenants backfilled with a single Gate Fee row. |

## User (buy + redeem)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| EX11 [NN] | Standalone buy, no variant | As an authed rider, `POST Buy` a no-variant product for the future event | One `event_extra_purchase` row per unit, status `pending`, single PaymentIntent for the cart; on webhook paid, each row flips to `paid` and appears in `GET Mine`. |
| EX12 [NN] | Variant required | Buy product (b) without a `VariantId` | 400 "Pick a size/color/gender for ...". Supplying an inactive or foreign variant id returns "That option isn't available." |
| EX13 [NN] | Variant inventory cap | Order qty 3 of the variant capped at `inventory = 2` | 400 "Only 2 of ... left" (or "sold out" at 0); qualifier shows size/color/gender label. Order of 2 succeeds; a later 3rd unit is rejected. |
| EX14 [NN] | Tenant-wide cap spans events | With product (c) `inventory = 3`, sell 2 at event A then attempt 2 at event B | Second order rejected ("Only 1 ... left"): `SumSoldProduct` sums across every event and variant, not per-event. |
| EX15 [NN] | Per-event cap (legacy, no variants) | Product (a) eligibility inventory = 5; sell 5 at the event, attempt a 6th | 6th rejected "sold out for this event"; selling the same product at a different eligible event has its own count. |
| EX16 [NN] | Variant short-circuits per-event cap | Confirm a product that has active variants ignores the per-event eligibility inventory | Inventory enforced only at the variant/product (tenant-wide) level; per-event cap not applied when active variants exist. |
| EX17 [NN] | Expiry blocks purchase | Buy the expired product (d) | 400 "... is no longer being sold." Enforced before any inventory math. |
| EX18 [NN] | Waiver gate | Buy product (f) as a rider with no signed waiver, then as one who signed | Unsigned: 400 "requires a signed waiver ..."; signed: succeeds and `waiver_signature_id` set on the rows. If no active waiver exists, purchase proceeds without a signature. |
| EX19 [NN] | Per-unit QR redemption | Buy qty 2, then have staff (SalesRedeem) redeem one token at the gate | Each unit has a distinct `redemption_token`; redeeming flips that one row `paid`->`redeemed`; redeeming again or a token from another tenant is a no-op (MarkRedeemed is tenant + status='paid' scoped). |
| EX20 [NN] | Feature flag off | On the `ExtrasEnabled = false` tenant, call `GET Products` and `POST Buy` | `GET Products` returns an empty list; `POST Buy` returns 400 "This tenant doesn't sell add-ons." |
| EX21 [R] | Event not buyable | Buy against a cancelled / ended event | 400 "Event not available." (status must be `scheduled` and `EndsAt` in the future). |
| EX22 [R] | Untethered Mine rows | Buy an add-on via POS (no event), open the rider's `GET Mine` (counter sale assigned to that rider if applicable) | Rows with null EventId stay in the list with null event fields; rows whose event is missing for the tenant are dropped. |

## Bundled checkout + POS

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| EX23 [NN] | Extras bundled with a ticket | Through `PurchaseController`, buy an event ticket plus two extras in one order | Eligibility enforced per extra; `ResolveExtraVariant` mirrors standalone rules; one PaymentIntent covers ticket + extras; one extra row per unit; combined waiver gating runs once. |
| EX24 [NN] | Bundled extra not eligible | Add an extra to a ticket order for an event with no eligibility row | 400 "... isn't offered at this event." |
| EX25 [NN] | POS sells extras as merch | At the counter, ring up an add-on with `Kind="extras"` and no event | Sale succeeds with no eligibility check, `EventId` null; product expiry, tenant-wide cap, and variant cap still enforced; rejects when `ExtrasEnabled` is false. |
| EX26 [NN] | POS variant required + cap | Ring up a variant product at the counter with no variant, then over the cap | "Pick a variant ..." then "Only N of that variant left" - same caps as online, counted tenant-wide. |
| EX27 [R] | Ledger / payout records extras | Complete a paid extras sale (any path) and check the sales ledger + payout | Revenue records a `tenant_ledger_entry` with `source_kind='extras'` (Script0099); confirm it counts toward tenant balance. NOTE: verify the finalizer actually writes the 'extras' ledger row - CounterController/PurchaseController comments say extras currently skip `ledgerLines` and only flip status to paid, so this is a likely gap to confirm. |
| EX28 [R] | Service-charge split | Buy with `rider_paid_service_charge_bps` at 0%, 50%, 100% | Rider-paid portion = serviceCharge * bps / 10000 added on top of frozen unit price; `UnitPriceCentsFrozen` records the pre-charge price; totals match across standalone / bundled / POS. |

---

## Known risks / watch-items
- **Inventory checks are read-then-insert with no lock** on every path (`ResolveVariantOrError`, the POS and bundled equivalents). Two concurrent orders for the last unit of a capped variant or a capped product can both pass and oversell. Match the ticket plan's concurrency note; consider `SELECT ... FOR UPDATE` if contention appears.
- **Ledger source_kind='extras' may not be written.** Script0099 permits the value, but the bundled/POS finalizers explicitly skip `ledgerLines` for extras (only `UpdateStatus(...,'paid')`). Confirm whether extras revenue reaches payouts on any path; EX27 is the gating check.
- **Sold-count queries are not tenant-scoped** (`SumSoldProduct`/`SumSoldVariant`/`SumSold` filter only by product/variant/event id). Safe today because those ids are tenant-unique GUIDs, but a join bug elsewhere could leak counts across tenants - keep an eye when refactoring.
- **`GetPurchase` / `GetPurchaseByRedemptionToken` / `GetPurchaseByPaymentIntentId` are not tenant-scoped.** Redemption (`MarkRedeemed`) and `Cancel` are tenant-scoped, but any caller that reads a purchase by id/token must re-check `TenantId` before acting (verify the gate-scan controller does).
- **Variant unique index folds NULL to ''** - intentional, but means a size-only and a fully-null variant collide; document for admins building sparse attribute sets.
- **Expiry uses `<= now`**; a product expiring at an exact timestamp becomes unsellable at that instant across all paths. Confirm the admin list still surfaces it for re-extension (EX7).
- Cross-tenant isolation: product/variant CRUD all go through `GetProduct(id, tenantId)`; verify a foreign productId on the variant endpoints returns 404, and a foreign id in the reorder payload is silently skipped (tenant predicate in the UPDATE).
