-- The bike shop counter gets its own cashier role (tenant_shop_cashier) so gate/F&B
-- cashiers and bike shop cashiers are distinct hires: sales.counter no longer opens the
-- bike shop register and shop.counter doesn't open the gate/F&B counter. A staffer who
-- works both counters holds both roles (permissions union). The users scope CHECK
-- enumerates the tenant-staff family, so re-state it with the new role included.
-- Rerunnable (DROP IF EXISTS + ADD) and backwards-compatible (strictly widens the set).
-- Both role CHECKs enumerate the family: users_role_check (all roles) and
-- chk_user_tenant_scope (role -> tenant scope), so re-state both.
ALTER TABLE users DROP CONSTRAINT IF EXISTS users_role_check;
ALTER TABLE users ADD CONSTRAINT users_role_check CHECK (role IN (
    'super_admin', 'tenant_admin', 'tenant_manager', 'tenant_cashier', 'tenant_shop_cashier',
    'tenant_scanner', 'tenant_accountant', 'tenant_staff', 'rider'
));

ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_user_tenant_scope;
ALTER TABLE users ADD CONSTRAINT chk_user_tenant_scope CHECK (
    (role IN ('super_admin', 'rider') AND tenant_id IS NULL)
    OR
    (role IN ('tenant_admin', 'tenant_manager', 'tenant_cashier', 'tenant_shop_cashier',
              'tenant_scanner', 'tenant_accountant', 'tenant_staff')
     AND tenant_id IS NOT NULL)
);
