-- Phase: allow a tenant user to hold multiple staff roles at once (permissions = union).
-- We keep users.role as the canonical PRIMARY role (drives global/tenant scope, login,
-- JWT identity, impersonation, display) and add users.roles as the full set. The primary
-- is always a member of the set. Multi-role only applies within the tenant-staff family;
-- 'rider' and 'super_admin' remain singletons (enforced by chk_user_tenant_scope on the
-- primary role).

-- Full role set. Backfill from the existing single role so every row is consistent.
ALTER TABLE users ADD COLUMN IF NOT EXISTS roles text[] NOT NULL DEFAULT '{}';
UPDATE users SET roles = ARRAY[role]
WHERE roles = '{}' OR roles IS NULL OR NOT (role = ANY(roles));

-- Repair the scope constraint: it predated the granular tenant roles (manager/cashier/
-- scanner/accountant) and only listed tenant_admin/tenant_staff, so those granular roles
-- would actually violate it. Re-state it with the full tenant-staff family.
ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_user_tenant_scope;
ALTER TABLE users ADD CONSTRAINT chk_user_tenant_scope CHECK (
    (role IN ('super_admin', 'rider') AND tenant_id IS NULL)
    OR
    (role IN ('tenant_admin', 'tenant_manager', 'tenant_cashier',
              'tenant_scanner', 'tenant_accountant', 'tenant_staff')
     AND tenant_id IS NOT NULL)
);
