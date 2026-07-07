# Section 13: Food & Beverage reporting & analytics

Covers the F&B reporting surfaces added after Sections 1-11: the profitability report, the sales-by-employee
(staff) report, and the void/comp report. The underlying sale flow + schema are Section 12. Event/ticket
Reports are Section 9.

## Inline fixes applied during this review

1. **High - reported net sales ignored discounts, so the dashboards over-stated revenue and did not
   reconcile (FIXED).** `concession_sale.subtotal_cents` is stored as the **gross, pre-discount** subtotal,
   but `GetSalesAggregate`, `GetHourlyProfitability`, and `GetEmployeeSales` computed net sales as
   `subtotal_cents - (inclusive ? tax_cents : 0)` and never subtracted `discount_cents`. A fully-comped $10
   order reported $10 of net sales; meanwhile the per-item/category tables (which use post-discount
   `line_total_cents`) reported the discounted figure, so the two views disagreed. Fixed by subtracting
   `discount_cents` in all three net-sales expressions
   (`subtotal_cents - discount_cents - (inclusive ? tax_cents : 0)`).
2. **Medium - item/category revenue was tax-inclusive for inclusive-pricing tenants (FIXED).**
   `GetItemProfitability` and `GetCategoryProfitability` summed `line_total_cents` with no tax adjustment,
   so for `prices_include_tax = true` tenants the per-item/category revenue, profit, and margin were
   inflated by the embedded tax and would not equal headline net sales. Fixed by backing out the line's
   tax when inclusive: `SUM(line_total_cents - CASE WHEN s.prices_include_tax THEN l.tax_cents ELSE 0 END)`.
   (These columns are already post-discount, so only the tax adjustment was needed.) After both fixes the
   sum of item revenue reconciles with headline net sales for both pricing modes.

## Scope

Read end-to-end:

- `webapi/Controllers/ReportsController.cs` - `GetConcessionProfitability`, `GetConcessionEmployees`,
  the `Margin()` helper and DTO mapping.
- `webapi/Controllers/ConcessionController.cs` - `CompReport` (void/comp) + `MemberLookup`.
- `Services/Repositories/ConcessionRepository.cs` "Profitability reporting" region:
  `GetSalesAggregate`, `GetCogsTotal`, `GetRefundAggregate`, `GetPaymentBreakdown`,
  `GetItemProfitability`, `GetCategoryProfitability`, `GetHourlyProfitability`, `GetEmployeeSales`,
  `SearchComps`.
- DTOs: `ConcessionProfitabilityReport.cs`, `ConcessionEmployeeReport.cs`, and the comp-report DTO;
  read-models in `ConcessionData/ConcessionReport.cs`.
- Frontend: `vueapp/src/views/Admin/Reports/ConcessionProfitability.vue`, `ConcessionStaff.vue`,
  `ConcessionComps.vue`; the report types + methods in `ReportsService.ts`.

## Architecture summary

Each report is a small set of independent, tenant-scoped aggregate queries the controller composes (the
profitability report runs ~7 one-shot queries; no N+1). Revenue and COGS are deliberately computed in
**separate** queries/CTEs and merged afterward, because the recipe join fans rows out - so COGS is never
multiplied into revenue. Revenue/COGS/item/category/hourly filter `status = 'paid'`; refunds are a separate
`status = 'refunded'` aggregate; the comp report intentionally includes both (a comp stands even if later
refunded). All money is integer cents cast `::bigint`; the only `double` is the display `MarginPct`, which
is guarded against divide-by-zero. Hour bucketing uses `created_at AT TIME ZONE @timezone` (a `timestamptz`)
for correct tenant-local dayparts. The reports are backed by `idx_concession_sale_tenant (tenant_id,
created_at DESC)` and `idx_concession_sale_line_sale (sale_id)`.

## Findings

### High

| # | Location | Issue | Status / fix |
|---|----------|-------|--------------|
| H1 | `GetSalesAggregate` / `GetHourlyProfitability` / `GetEmployeeSales` | Net sales ignored `discount_cents`; revenue over-stated and dashboards didn't reconcile. | **Fixed inline** (#1 above). |

### Medium

| # | Location | Issue | Status / fix |
|---|----------|-------|--------------|
| M1 | `GetItemProfitability` / `GetCategoryProfitability` | Revenue tax-inclusive for inclusive-pricing tenants; margins inflated. | **Fixed inline** (#2 above). |

### Low / informational

| # | Location | Issue |
|---|----------|-------|
| L1 | `GetItemProfitability` | Combo component child lines (`line_total_cents = 0`, real recipe COGS) produce a per-item row with ~0 revenue, negative profit, and inflated `QtySold`, while the entree row shows full combo revenue against only its own COGS. Headline totals are correct (revenue + COGS aggregated separately). Documented in code + the UI note; consider folding child COGS into the parent or flagging combo-child rows. |
| L2 | `GetCogsTotal` vs item/category COGS | Headline COGS casts the full `SUM(...)::bigint` once; the per-item/category CTEs cast per group. With fractional `recipe_item.quantity` (`numeric(12,3)`) the summed rows can differ from the headline by a cent or two. Cosmetic; defer the cast to after summation if exact reconciliation matters. |
| L3 | `ConcessionController.CompReport` | Resolves cashier names via `_users.GetById(sid)`, an id-only (not tenant-scoped) lookup. The `sid` values come from tenant-scoped comp sales' `sold_by_user_id` (set to the authenticated staff at checkout), so no realistic cross-tenant leak and only first/last name is read; still, prefer passing the tenant id or joining inside `SearchComps`. Also loops `GetById` per distinct seller (small, `LIMIT 500`-capped). |

## What's verified clean (no action)

Tenant isolation on every reporting query (all filter `tenant_id` via `concession_sale`; the
`GetEmployeeSales` users-join only surfaces names for the tenant's own sales; recipe/inventory joins key on
globally-unique UUIDs sourced from tenant-scoped lines). COGS join cardinality (revenue and COGS merged
post-aggregation). Status semantics (paid vs refunded, no double counting; pending/failed ignored).
Divide-by-zero guards on `Margin()` and both average-order-value computations. Timezone bucketing. Money
typed as integer cents end to end; complete tender split (`cash` vs `stripe`/`stripe_direct`). The report
Vue components have informative load-error handling and display-only money. No critical or high issue
remains after the two inline fixes.
