---
name: tenant-audit
description: Audit recent backend changes (Controllers / Repositories / migrations) for multi-tenant isolation bugs — SQL queries missing tenant_id, controllers missing IsResolved guards, repository calls without tenant scope, migrations that skip backfill. Run this before reporting any backend task complete that touches tenant-scoped code.
---

# Tenant Isolation Audit

RidePass is a multi-tenant SaaS where every tenant's data must be isolated by `tenant_id`. A query that omits the predicate is a cross-tenant data leak waiting to happen — the worst class of bug for this kind of product. This skill walks the in-flight diff and reports any isolation gaps before they ship.

## When to invoke

Run this audit after edits to:
- `webapi/Controllers/**/*.cs`
- `Services/Repositories/**/*.cs`
- `Services/Repositories/Interfaces/*.cs`
- `RidePass.Migrator/Scripts/*.sql`

If the diff doesn't touch any of those areas, skip the audit — there's nothing to verify.

## What to check

Walk through each of the four checks below against the diff. For each finding, report `file:line`, the risk class, and a concrete fix.

### 1. SQL queries are scoped by tenant_id

Every SQL string in modified .cs files whose target is a per-tenant table must include `tenant_id = @tenantId` in the WHERE clause — either directly, or via a JOIN through a row that's already tenant-scoped (e.g., `event_ticket_purchase` joined to `event_ticket_tier` joined to `event` is fine because each join carries the scope).

**Per-tenant tables (need the predicate):** `event`, `event_type`, `tenant_event_type`, `tenant_waiver`, `pass_product`, `pass_purchase`, `event_ticket_tier`, `event_ticket_purchase`, `event_extra_product`, `event_extra_purchase`, `season_pass_product`, `season_pass_purchase`, `season_pass_reservation`, `membership_purchase`, `gift_card`, `rental_product`, `rental_item`, `newsletter_subscriber`, `newsletter_campaign`, `newsletter_recipient`, `coupon`, `reward_program`, `reward_redemption`, `audit_log`, `track_feedback`, `survey`, `survey_question`, `survey_question_choice`, `survey_invite`, `survey_response`, `survey_answer`, `notification`, `notification_preference`.

**Globally-scoped tables (DO NOT need the predicate):** `tenant`, `users`, `super_admin`, `super_admin_session`, `event_subscription`.

**Watch for these violation shapes:**
- `DELETE FROM <per-tenant-table> WHERE id = @id` — id-only scoping is unsafe
- `SELECT ... FROM <per-tenant-table> WHERE id = @id` — same
- `UPDATE <per-tenant-table> SET ... WHERE id = @id` — same
- `DELETE FROM survey_question WHERE id = @id` — child rows of a per-tenant parent: the parent's tenant should be enforced, either by joining or by passing tenantId from a prior fetch
- `WHERE token = @token` without subsequent tenant verification — token lookups can leak across tenants if the token space isn't globally unique

**Acceptable:** child-row queries that constrain by parent FK and the controller has already verified the parent belongs to the tenant. Note these explicitly — they're easy to miss when the call site changes.

### 2. Controllers verify tenant resolution

Every controller action that touches tenant-scoped data must:
- Begin with `if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");`
- Pass `_tenantContext.TenantId` to repo calls
- Carry an `[Authorize(Policy = TenantPermissions.Policy.X)]` attribute appropriate to the action — except intentionally-public endpoints (e.g., `Survey/Public/{token}`, `Event/Public/{id}`)
- For public endpoints, still verify `IsResolved` before reading tenant data — the subdomain middleware needs to have resolved a tenant for the response to be meaningful

### 3. Repository calls pass the tenant id

For repository methods whose signature includes a `tenantId` parameter, every call site must pass it from `_tenantContext.TenantId` or from a previously-verified tenant id (e.g., the tenant id from a parent row already fetched with the right scope).

**Watch for:**
- `await _repo.GetById(id)` when an overload taking `(id, tenantId)` exists — wrong overload
- `Guid.Empty` or hardcoded GUIDs passed for tenantId — almost always wrong
- A method that takes a tenantId param but never uses it in the SQL — the param is decorative; SQL is unscoped

### 4. Migrations preserve scope

For migrations under `RidePass.Migrator/Scripts/`:
- **New per-tenant tables**: must have `tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE` and at least one index that includes `tenant_id`
- **ALTER TABLE on per-tenant tables**: if the new column has business meaning (not just NULL), is there a backfill `UPDATE` for existing tenants? Is the default sensible cross-tenant?
- **Triggers**: if `seed_default_event_types`, `seed_initial_waiver`, or `seed_default_pass_products` is touched, do they branch correctly on `tenant_type`? Do they all `ON CONFLICT DO NOTHING` so re-running is safe?

## Output format

Report each finding as:

```
- <file>:<line> — <risk class>
  <one-line description of what's wrong>
  Fix: <one-line suggestion>
```

Risk classes: **data leak** (read across tenants), **cross-tenant write** (modify another tenant's data), **unauthorized access** (missing policy), **migration gap** (existing tenants left in bad state).

If no findings: `Tenant-isolation audit clean — no issues found in this diff.`

## What NOT to flag

- Frontend (Vue) files — tenant scope is enforced server-side; client code can't leak data.
- Read-only utility helpers under `webapi/Helpers/` that don't touch DB.
- DTOs and request/response classes — they don't query data.
- Test fixtures or seed data scripts that intentionally cross tenants.
