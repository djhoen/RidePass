# RidePass project rules

## Tenant isolation is non-negotiable

This is a multi-tenant SaaS. Every per-tenant query must be scoped by `tenant_id`. A query that omits the predicate is a cross-tenant data leak — the worst class of bug for this codebase.

**Before reporting any backend task complete that touched files under `webapi/Controllers/`, `Services/Repositories/`, or `RidePass.Migrator/Scripts/`, invoke the `/tenant-audit` skill and address any findings.** Do not skip this even if the change feels obvious — the cost of pausing for the audit is seconds; the cost of a leak is the company.

If the audit reports findings, fix them, then re-run until it reports clean. Only after that, report the task as done.

## Standard backend patterns

- Migrations live in `RidePass.Migrator/Scripts/` named `Script####_Description.sql`. Pick the next number by globbing the existing scripts.
- Repository pattern: entity in `Services/Repositories/Data/<Area>/`, interface in `Services/Repositories/Interfaces/`, implementation in `Services/Repositories/`, DI registration in `webapi/Program.cs`.
- DTOs: one class per file under `Controllers/API/Data/<Area>/`.
- Tenant scope is resolved by `TenantResolutionMiddleware` from the request subdomain into `ITenantContext` — controllers read `_tenantContext.TenantId` and verify `_tenantContext.IsResolved` first.
- Permission policies are declared in `webapi/AuthPolicies/TenantPermissions.cs`. Common ones: `CatalogManage`, `SettingsManage`, `CampaignsManage`, `SalesCounter`, `SalesRedeem`, `SalesView`, `ReportsView`.

## v_recent_sales radar

`v_recent_sales` (defined in `RidePass.Migrator/Scripts/Script0080_RecentSalesView.sql`) is the unified read model the admin dashboard and the Admin → Purchases list both read from. **Whenever you add a new purchase-shaped table, change a column the view selects, or wire up a new sale repository, silently apply the rules in `.claude/skills/recent-sales-view/SKILL.md` and surface a one-paragraph update offer at end-of-turn.** Skipping it means the new sale kind silently disappears from those screens.
