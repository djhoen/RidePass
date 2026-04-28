-- Riders become global accounts: one login works across every tenant subdomain.
-- Tenant admins and staff stay tenant-scoped.

ALTER TABLE users DROP CONSTRAINT chk_user_tenant_scope;

-- Detach existing riders from their home tenant.
UPDATE users SET tenant_id = NULL WHERE role = 'rider';

ALTER TABLE users ADD CONSTRAINT chk_user_tenant_scope CHECK (
    (role IN ('super_admin', 'rider') AND tenant_id IS NULL)
    OR
    (role IN ('tenant_admin', 'tenant_staff') AND tenant_id IS NOT NULL)
);
