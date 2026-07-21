-- Food & Beverage gets its own cashier role, completing the counter split.
--
-- Until now sales.counter opened BOTH the gate counter and the F&B POS, so a gate cashier and a
-- food-window cashier were the same hire. Script0191 split the bike shop out; this splits F&B, so
-- the three counters are three distinct roles and a staffer who works two of them holds two roles
-- (permissions are the union). The new permission is concessions.counter, held by
-- tenant_fnb_cashier, manager, and admin.
--
-- Rerunnable (DROP IF EXISTS + ADD) and backwards-compatible (strictly widens the role set).
-- Both role CHECKs enumerate the tenant-staff family, so re-state both.

ALTER TABLE users DROP CONSTRAINT IF EXISTS users_role_check;
ALTER TABLE users ADD CONSTRAINT users_role_check CHECK (role IN (
    'super_admin', 'tenant_admin', 'tenant_manager', 'tenant_cashier', 'tenant_fnb_cashier',
    'tenant_shop_cashier', 'tenant_scanner', 'tenant_accountant', 'tenant_staff', 'rider'
));

ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_user_tenant_scope;
ALTER TABLE users ADD CONSTRAINT chk_user_tenant_scope CHECK (
    (role IN ('super_admin', 'rider') AND tenant_id IS NULL)
    OR
    (role IN ('tenant_admin', 'tenant_manager', 'tenant_cashier', 'tenant_fnb_cashier',
              'tenant_shop_cashier', 'tenant_scanner', 'tenant_accountant', 'tenant_staff')
     AND tenant_id IS NOT NULL)
);

-- Existing cashiers keep the F&B access they have today. Before this release sales.counter
-- covered the food window, so anyone holding tenant_cashier at a track with concessions turned on
-- would silently lose the POS. Grant them the new role alongside their existing one instead.
-- Idempotent: only adds where the role isn't already present.
UPDATE users u
   SET roles = array_append(u.roles, 'tenant_fnb_cashier')
  FROM tenant t
 WHERE t.id = u.tenant_id
   AND t.concessions_enabled
   AND 'tenant_cashier' = ANY(u.roles)
   AND NOT ('tenant_fnb_cashier' = ANY(u.roles));
