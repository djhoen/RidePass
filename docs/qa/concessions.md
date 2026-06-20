# QA Test Plan: Concessions / Store

> Scope: concession product + variant CRUD, variant inventory caps, the cashier (SalesCounter) item list, the anonymous card-present sale flow with server-authoritative totals and Stripe Terminal, and tenant isolation. Last updated: 2026-06-20.

## Surface map
- **Admin (CatalogManage):** `ConcessionController` product CRUD (`Products/Admin`, `POST/PUT/DELETE Products`, `Products/Reorder`), variant CRUD (`Products/{productId}/Variants` POST, `PUT/DELETE .../Variants/{variantId}`), image upload (`POST Image`).
- **Cashier (SalesCounter):** `Items` (active products + variants to ring up, gated on `tenant.ConcessionsEnabled`), `Sale` (`POST Sale`, anonymous-buyer card-present sale).
- **Repo:** `ConcessionRepository` / `IConcessionRepository` - products, variants, `SumSoldVariant(s)`, sales (`CreateSale`, `CreateSaleLines`, `SetSalePaymentIntentId`, `MarkSalePaid`, `MarkSaleFailed`, `GetSaleByPaymentIntentId`).
- **Entities:** `Data/ConcessionData/Concession.cs` - `ConcessionProduct`, `ConcessionVariant`, `ConcessionSale` (status pending|paid|failed|refunded), `ConcessionSaleLine`.
- **Payment:** `EnsureTerminalLocation` (lazily creates the tenant Stripe Terminal location from the tenant address; mirrors `CounterController`), `CreateCardPresentPaymentIntentAsync`; the existing payment webhook flips the sale to paid.

## Concepts under test
- A **product** (`concession_product`) has a `name`, a `category` (`food` | `drink` | `swag` | `other`, lower-cased on save), a base `price_cents`, image, `is_active`, `sort_order`. There is no event attachment, no waiver, no service-charge field, and no per-product inventory.
- A product with at least one **active variant** (`concession_variant`: size / color, optional price-inherit, optional `inventory`) becomes variant-required at sale. Variant `inventory` is tenant-wide; null = unlimited.
- The **sale** is server-authoritative: the cashier sends a cart of (productId, variantId, qty); the server recomputes the total from the catalog (never trusts a client amount), enforces variant inventory, writes a `pending` `concession_sale` + lines, and opens a card-present PaymentIntent the reader confirms. Pricing is all-in: no tax and no added service charge. Minimum total is 50 cents.
- **Sold count** (`SumSoldVariant(s)`) counts sale lines on sales with status IN ('pending','paid') - so a pending (in-flight) sale already holds inventory.
- Sales are **anonymous** (no buyer account, no email, no redemption token / QR); `sold_by_user_id` records the cashier when resolvable.

## Preconditions / test data
- A tenant with `ConcessionsEnabled = true` and a complete address (AddressLine, City, Country, PostalCode) so a Terminal location can be created; a second tenant for isolation; a third with `ConcessionsEnabled = false`; a fourth with `ConcessionsEnabled = true` but an incomplete address.
- Products: (a) a no-variant drink at 300 cents; (b) a swag T-shirt with two active size variants (one capped at `inventory = 2`, one unlimited) and one inactive variant; (c) a 25-cent item to probe the minimum-total floor.
- A SalesCounter cashier user; a CatalogManage admin user.

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CN1 [NN] | Product CRUD | Create, edit, reopen a product (name, category, price, image, active, sort) | Saves with current `tenant_id`; `category` trimmed + lower-cased; reopens intact. |
| CN2 [NN] | Category normalization | Save category as " Drink " / "SWAG" | Stored as `drink` / `swag`; UI filters by the normalized value. |
| CN3 [NN] | Variant CRUD | Add two size variants under product (b); edit one price/inventory; delete an unsold one | Variant created under the product; null price = inherit; inventory persists; unsold delete succeeds. |
| CN4 [NN] | Duplicate variant attrs blocked | Create a variant with the same (size, color) as an existing one | 400 "A variant with the same size / color already exists." |
| CN5 [NN] | Delete variant with sales | Sell a variant, then delete it | 400 "has sales on file ... Set it inactive instead." (FK RESTRICT). |
| CN6 [NN] | Delete product with sales | Delete a product that has sale lines | 400 "has sales on file ... Set it inactive instead." |
| CN7 [NN] | Inventory remaining display | Set variant `inventory = 2`; open `Products/Admin` | Variant `Sold` = sum of pending+paid lines, `Remaining` = inventory - sold; unlimited variant reports Remaining = -1. |
| CN8 [R] | Reorder products | Drag-reorder and POST `Products/Reorder` | sort_order updated in one round-trip scoped by tenant; foreign ids ignored. |
| CN9 [R] | Image upload limits | Upload >5 MB, then a .gif, then a valid .webp | >5 MB and unsupported content-type rejected 400; webp returns an imageUrl under the tenant's "concession" path. |
| CN10 [R] | Foreign product on variant endpoints | Call the variant create/update/delete with a productId from another tenant | 404 "Item not found." (`GetProduct(id, tenantId)` guard). |

## Cashier (ring up + sell)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| CN11 [NN] | Item list | As cashier on the enabled tenant, call `Items` | Returns active products with active variants hydrated; inactive products/variants hidden; sold counts attached. |
| CN12 [NN] | Feature flag off | Call `Items` and `Sale` on the `ConcessionsEnabled = false` tenant | `Items` returns empty list; `Sale` returns 400 "Concessions aren't enabled for this track." |
| CN13 [NN] | Simple sale | Ring up 2x product (a); confirm on reader | Server computes total = 600; `concession_sale` pending + lines written; card-present PaymentIntent created; `SaleId`, `ClientSecret`, `TotalCents` returned; webhook flips to `paid` with `paid_at`. |
| CN14 [NN] | Variant required | Ring up product (b) with no `VariantId` | 400 "Choose an option for ...". Foreign/inactive variant id -> "That option isn't available for ...". |
| CN15 [NN] | Variant inventory cap | Sell 3 of the variant capped at `inventory = 2` | 400 "Only 2 of \"... (size/color)\" left" (or "sold out" at 0); a qty-2 sale succeeds and decrements remaining. |
| CN16 [NN] | Server-authoritative total | Submit a cart and confirm the charged amount ignores any client-sent amount | Total recomputed from catalog (variant price ?? product price); line snapshots (`name_snapshot`, `variant_label`, `unit_price_cents`) frozen on the sale lines. |
| CN17 [NN] | Cart dedupe | Submit the same (product, variant) twice plus a qty-0 line | Quantities summed into one line; zero-qty lines dropped; empty cart -> 400 "Cart is empty." |
| CN18 [NN] | Minimum total | Ring up a single 25-cent item (product c) | 400 "Sale total must be at least 50 cents." |
| CN19 [NN] | Terminal location required | On the enabled tenant with an incomplete address, attempt a sale | 400 "Cannot take card-present payments until the track's address is filled in (Settings -> General)." Sale not left dangling pending without a PI (location resolved before sale creation). |
| CN20 [NN] | Terminal location lazily created + cached | First sale on an enabled tenant with a complete address but no `StripeTerminalLocationId` | Location created from the tenant address and saved (`SetStripeTerminalLocationId`); subsequent sales reuse it. |
| CN21 [R] | PI creation failure cleanup | Force `CreateCardPresentPaymentIntentAsync` to throw | Sale marked `failed` (`MarkSaleFailed`) and 400 surfaced; the failed sale's lines no longer hold inventory (status not in pending/paid). |
| CN22 [R] | Cashier recorded | Complete a sale as a known cashier | `sold_by_user_id` set from the token when parseable; null when absent (anonymous still allowed). |

---

## Known risks / watch-items
- **Pending sales hold inventory indefinitely.** `SumSoldVariant(s)` counts status IN ('pending','paid'), and an abandoned card-present sale (reader cancelled, customer walked away, PI never confirmed or failed without a webhook) stays `pending` forever, permanently reserving capped-variant stock. There is no timeout/sweeper that flips stale pendings to failed. This is the single biggest correctness risk for capped concession variants - verify a cleanup path exists or recommend one.
- **Inventory check is racy and not transactional.** Two cashiers ringing the last unit of a capped variant can both pass the `SumSoldVariant` check before either sale row exists, oversell, and both confirm. No lock on the variant row.
- **No refund path in this controller.** `ConcessionSale` defines a `refunded` status but the controller exposes no void/refund endpoint; once paid, a mistaken sale can't be reversed here. Confirm where (if anywhere) a refund is issued and whether it restores variant inventory.
- **`SumSoldVariant(s)` and `GetSaleByPaymentIntentId` are not tenant-scoped** (filter by variant id / PI id only). Safe today because those are tenant-unique values and `CreateSale` scopes the product fetch by tenant, but the webhook finalizer that looks up a sale by PI must confirm the tenant before writing the ledger.
- **All-in pricing, no tax:** total = subtotal with no tax or service charge. Confirm this matches the tenant's tax obligations and that the 50-cent Stripe minimum is the only floor.
- **Anonymous, no QR:** there is no buyer record, receipt email, or redemption token - concession sales never appear in a rider's "Mine" list and cannot be redeemed/looked-up by token. Confirm this is intended (storefront / impulse model) and that the sale still lands in the unified sales/ledger read model for reporting.
- Cross-tenant isolation: product reads/writes go through `GetProduct(id, tenantId)`; variant endpoints re-verify the parent product belongs to the tenant; reorder ignores foreign ids. Verify the payment webhook path does the same tenant re-check before marking paid.
