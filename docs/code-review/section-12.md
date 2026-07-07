# Section 12: Food & Beverage (concession) QSR subsystem

Covers the quick-service Food & Beverage build-out that landed after Sections 1-11 were written. Section 9
reviewed the *event/ticket* Reports surface; the F&B catalog, inventory, kitchen, cashier/online sale
flow, settings, online-order throttle, manager-PIN, discounts/comps, and the menu/pickup boards had **no
prior coverage**. F&B reporting (profitability / staff / void-comp) is split into Section 13.

## Inline fixes applied during this review

1. **Medium - `GET /Concession/Combo` was anonymous (FIXED).** `webapi/Controllers/ConcessionController.cs`
   had no class-level `[Authorize]` and no `FallbackPolicy` is registered, so the combo-config read action
   (missing its attribute) was reachable unauthenticated, exposing a tenant's combo tiers, slot component
   names, and upcharge pricing. Added `[Authorize]` to match every sibling menu read.
2. **High - printed + emailed/SMS receipts dropped the discount/comp line (FIXED).** After #2 added
   discounts/comps, `BuildReceiptText`/`BuildReceiptHtml` (server) and `ReceiptPrinter.ts` +
   `ConcessionPos.sendReceipt` (client ESC/POS) still printed only Subtotal / Tax / Tip / Total, so a
   discounted or comped receipt did not foot (line items and total disagreed with no explanation). Added a
   discount line (`-{DiscountCents}`, labeled) between Subtotal and Tax in all three receipt renderers and
   threaded `discountCents`/`discountLabel` through the `Receipt` interface and the POS mapping.
3. **Medium - a failed recipe load could silently wipe an item's recipe on Save (FIXED).**
   `Concessions.vue` `openEdit` set `recipeRows = []` then awaited `getRecipe`; on failure it flashed an
   error but left the rows empty, and `saveProduct` unconditionally called `setRecipe(productId, [])`,
   deleting the item's existing recipe. Added a `recipeLoaded` guard so Save skips the recipe write unless
   the recipe actually loaded (new items start `recipeLoaded = true` since an empty recipe is correct).

## Follow-up fixes applied after the review

- **H1 (manager PIN brute-force) FIXED + hardened.** Verification/lockout/uniqueness were centralized into
  a shared `webapi/Security/ManagerPinService` (so the gate can use the identical model): a per-(tenant,
  staff-user) DB lockout (`Script0163_ManagerPinLockout.sql`, 5 failures -> 15-min lockout, manager
  notification on lockout), an ASP.NET `manager-pin` rate-limit policy on `VerifyManagerPin`, and
  PIN-uniqueness enforced on set so the authorizer is unambiguous. The 4-digit floor is kept. A
  `ManagerPin/Status` endpoint + an F&B-admin banner prompt every manager to set their own PIN.
  **Still open:** wiring the gate's `ForceCheckedIn` refund-override (PurchaseController) through the same
  service, and a global (not just F&B) forced-setup prompt.
- **M1 (order-number UTC reset) FIXED.** `NextOrderNumber` derives the business date from the tenant's
  stored timezone in SQL, so the counter resets at local midnight.
- **M2 (86 / availability UTC) FIXED.** A `TenantToday()` helper now drives the 86 store + availability +
  member-window checks (tenant-local date).
- **L1 (refund-ledger 23505) FIXED**; **L3 (clamping) partially FIXED** (`ReceiveStock` now rejects <= 0;
  modifier groups reject min<0 / max<min). Remaining L3 clamps (price/cost/on-hand/counted-qty) and the L2
  name-guards, plus L4/L5/L6, remain documented/open.
- Tax-missing is now surfaced: the Settings tax card shows a warning when no rate is configured.

## Scope

Read end-to-end:

- `webapi/Controllers/ConcessionController.cs` (full): product/variant/category/station/modifier/combo
  CRUD; product->group + default-option assignment; inventory items + `ReceiveStock`; recipes; stock takes;
  low-stock `NotifyLowStock`; 86 (`SetProductSoldOut`); starter-catalog seed; image upload; the kitchen
  endpoints (`Kitchen`, line advance, `Recall`, `Complete`, `Kitchen/Completed`); the cashier sale
  (`CreateSale`) and rider sale (`RiderOrder`); `ResolveCartLines`, `ResolveModifiers`, `ApplyCombo`,
  `ResolveTierSize`, `ComputeLineTax`; discounts/comps (`ApplyDiscounts`/`ComputeDiscountOutcome`); manager
  PIN (`SetManagerPin`, `VerifyManagerPin`, `VerifyManagerPinInternal`, `IsManagerOrAdmin`); menu settings;
  ordering capacity / status / pause; tax categories; discount presets; comp reasons; refunds
  (`RefundSale`); receipts; the pickup `Board`.
- `webapi/Payments/StripePurchaseFinalizer.cs` - `OnConcessionPaid`.
- `Services/Repositories/ConcessionRepository.cs` (full) and `Services/Repositories/UserRepository.cs`
  (`SetPosPinHash`, `ListTenantManagerPins`).
- All DTOs under `webapi/Controllers/API/Data/Concession/*.cs`.
- `RidePass.Migrator/Scripts/Script0140`..`Script0162` (the F&B-era migrations).
- Frontend: `vueapp/src/views/Admin/ConcessionPos.vue`, `ConcessionKitchen.vue`, `Concessions.vue`,
  `ConcessionMenuBoard.vue`, `ConcessionPickupBoard.vue`, `ConcessionOrders.vue`, `ConcessionInventory.vue`,
  `vueapp/src/views/OrderFood.vue`, `vueapp/src/services/ConcessionService.ts`,
  `vueapp/src/helpers/ReceiptPrinter.ts`.

## Architecture summary

**Server-authoritative pricing.** Both `CreateSale` (counter) and `RiderOrder` (online) rebuild every
price from the catalog via `ResolveCartLines` -> `ResolveModifiers` -> `ApplyCombo`, never trusting a
client amount. Each line snapshots a frozen tax rate + tax cents. Tips are clamped server-side; discounts
are recomputed and clamped so a total can never go negative; tax is computed on the post-discount net.
`SubtotalCents` is persisted as the **gross** (pre-discount); `DiscountCents`/`TaxCents`/`TotalCents`
reflect the net. Cash and fully-comped orders finalize inline (order number + ledger now); card orders
persist `pending` and are finalized exactly once by `StripePurchaseFinalizer.OnConcessionPaid` (webhook or
the `Finalize` endpoint).

**Tenant isolation is clean across the whole subsystem.** Every per-tenant read/write/list filters
`tenant_id` directly or via a tenant-scoped parent join (variant via product, recipe via product, sale
line via sale, modifier option via group). Child-row CRUD verifies the parent belongs to the tenant before
the id-only inner write. The id-only system paths (`GetSaleByPaymentIntentId`, `MarkSalePaid`,
`SetOrderNumber`, `FailStalePendingSales`) are webhook/finalizer/maintenance keyed by globally-unique
values. No IDOR or cross-tenant read/write found. Manager-PIN storage is hashed
(`IPasswordHasher<User>`), never returned or logged; PIN-set is gated to managers/admins on the
authenticated self.

**Frontend contract is consistent** - every `ConcessionService.ts` method's verb/route/request/response
shape matches its controller endpoint + DTO (verified field-by-field; no mismatches). No XSS (the receipt
renderer escapes every user string; no `v-html`), no native confirm/alert/prompt, all `setInterval`
timers are cleared in `onUnmounted`.

## Findings

### High

| # | Location | Issue | Status / fix |
|---|----------|-------|--------------|
| H1 | `ConcessionController.cs` `VerifyManagerPin` (+ the sale-path PIN check) | Manager PIN has no rate limit or lockout, a 4-digit floor, and is callable by the `SalesCounter` role it defends against. `VerifyManagerPinInternal` tests a guess against every manager's hash, so expected guesses to first hit ~= 10000 / #managers. A cashier can script it, discover a PIN, then self-authorize comps/discounts. | **Open** (owned by the discounts/comps feature). Add a fixed-window rate-limit policy + per-user failed-attempt lockout (notify on lockout), and/or raise the min PIN length to 6+. Needs a small product decision; see end-of-section. |
| H2 | receipts (server + client) | Discount/comp line missing; receipt didn't foot. | **Fixed inline** (#2 above). |

### Medium

| # | Location | Issue | Status / fix |
|---|----------|-------|--------------|
| M1 | `ConcessionRepository.NextOrderNumber` (called `ConcessionController.cs` sale path) | Per-day order-number counter keys off `DateTime.UtcNow.Date`, not tenant tz. For a non-UTC track the counter resets at UTC midnight mid-service, so order #1, #2... reappear the same evening and collide on the cook screen + pickup board. | Open. Derive the business date in `TenantTz()` (as `HasEventToday`/`ListOrders` already do). |
| M2 | `SetProductSoldOut` + `ApplyProductAvailability` + `ResolveCartLines` | Manual 86 keys off UTC date, so an item 86'd in the evening auto-un-86es at UTC midnight for non-UTC tenants; the "clears tomorrow" comment is wrong. | Open. Compute "today" in tenant tz. |
| M3 | `StripePurchaseFinalizer.OnConcessionPaid` | Inventory depletion is not guarded by the same atomic transition as the order-number assignment, so a concurrent webhook + `Finalize` can deplete recipe inventory twice (and burn one pickup number). | Open. Gate `DepleteInventoryForSale` on `SetOrderNumber` actually setting the number (return rows-affected). |
| M4 | `ResolveCartLines` capped-item check | Oversell race: `SumSold*` is read then lines inserted with no lock/atomic decrement, so two concurrent sales for the last unit both pass. `inventory` is a documented soft cap, hence Medium. | Open. Atomic conditional decrement or `SELECT ... FOR UPDATE` inside a sale transaction. |
| M5 | `ConcessionPos.vue` tender/change preview | When a discount applies, the client `total`/`changeCents`/"Charge card ·" use the client estimate; if the server taxes/rounds differently the displayed amount can disagree with the recorded sale. Receipt itself trusts the server total. | Open. Reconcile displayed total + change against `res.totalCents` before completing the cash tender. |
| M6 | `concession_menu_settings`, `concession_inventory_item`, `concession_ordering_capacity` | Declare `updated_at NOT NULL DEFAULT now()` but no `set_updated_at` trigger exists for any concession table; `inventory_item.on_hand` mutates on every depletion so its `updated_at` goes stale. | Open. Add `DROP TRIGGER IF EXISTS ...; CREATE TRIGGER ... set_updated_at()` per table (additive migration). |
| M7 | Migrations `0140, 0144, 0148, 0152, 0153, 0154, 0155` | Non-idempotent (unguarded `CREATE TABLE/INDEX/ADD COLUMN`; `0154` `DROP COLUMN is_combo` without `IF EXISTS`; `0140`/`0144` backfills would corrupt/duplicate data if reached on re-run). All predate the migration-safety rules; `0156`+ are clean. | Open / by-design (already applied). Documented so a fresh re-run from these scripts is known-unsafe; retrofit with guards if a re-run is ever needed. |

### Low / informational

| # | Location | Issue |
|---|----------|-------|
| L1 | `RefundSale` | Refunds are not manager-PIN gated, while comps always are - inconsistent with the "gated refunds + audit" model. Also `WriteRefundLedger` does not swallow `23505` (a concurrent second refund's loser 500s after the refund already succeeded); wrap it like the other ledger inserts. |
| L2 | `Create*`/`Update*` actions | `req.Name.Trim()` with no null/required guard (NRE -> 500 instead of 400) on most create/update endpoints. |
| L3 | input clamping | `PriceCents`, `CostCents`, `OnHand`, `ReceiveStock` quantity (no `>0`, a negative receive depletes), `CountedQty`, modifier `MinSelect`/`MaxSelect` (no `Min<=Max`; an impossible group blocks every sale of its product) are stored unclamped. |
| L4 | fully-comped card order | `total <= 0` branch always stamps `payment_method = 'cash'` even when card was chosen; mislabels tender on a $0 comp (no money impact). |
| L5 | combo component tax | Combo child lines are taxed at the entree's rate; a component in a different tax category (taxable drink under exempt food) is taxed wrong. Jurisdiction-dependent. |
| L6 | `GET Board` / config GETs | `Board` (`[Authorize]`) returns active customers' names + numbers; `MenuSettings`/`TaxCategories`/`DiscountPresets`/`CompReasons` (`[Authorize]`) expose full config to any authenticated user incl. riders. Not sensitive; consider a public-safe projection / display token. |
| L7 | `AdvanceLine` | Reloads + hydrates the full kitchen line set just to read one bumped line's `SaleId`; per-bump cost on a busy KDS. Return `SaleId` from `AdvanceLinePrep`. |
| L8 | refund vs `on_hand` | A refund releases the cap-based "sold" reservation but does not restore recipe-depleted `on_hand`; the two stock models diverge until the next stock take. Likely by design (food already made). |
| L9 | menu-board first load | `ConcessionMenuBoard.refresh()` swallows errors; on the very first poll (no last-good data) a failure renders an empty board with no message. Acceptable for an unattended display; optional one-time error state. |
| L10 | tax/settings preview loads | POS/rider swallow `menuSettings()`/`taxCategories()` load failures silently; tax/tip preview is then silently wrong (server still authoritative on the charge). Consider a non-blocking toast. |

## Regression found (not a code defect in the new code, but a loss during concurrent work)

**The tax auto-suggest feature was removed.** A "Suggest from address" button + a `Concession/Tax/Suggest`
endpoint (API Ninjas lookup, key in config) were built earlier and have since vanished from the controller,
service, DTOs, appsettings, and the settings UI (grep across the repo finds only docs/skill references).
It was almost certainly dropped when `ConcessionController.cs` / `ConcessionService.ts` / the settings view
were rewritten during the parallel discounts/comps work. Manual tax-rate entry still works; only the
one-click address lookup is gone. Restore is a re-implementation, not a patch.

## What's verified clean (no action)

Tenant isolation across the whole subsystem; manager-PIN hashing + set-gating; combo depletion + COGS
counting child lines; low-stock one-alert-per-episode dedupe; fulfillment recompute + one-shot ready SMS;
capped-variant reservation aggregation; the full `ConcessionService.ts` <-> controller/DTO contract;
receipt XSS escaping; no native dialogs; polling teardown; Vuetify field spacing + modal X-close
conventions across the F&B views.
