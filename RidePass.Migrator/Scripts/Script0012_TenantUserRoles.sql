-- Phase 10: granular tenant user roles. Adds tenant_manager, tenant_cashier,
-- tenant_scanner, tenant_accountant alongside the existing tenant_admin. Keeps
-- tenant_staff for now so any legacy rows don't break; the app treats it as
-- equivalent to tenant_scanner going forward.

ALTER TABLE users DROP CONSTRAINT IF EXISTS users_role_check;
ALTER TABLE users ADD CONSTRAINT users_role_check CHECK (role IN (
    'super_admin',
    'tenant_admin',
    'tenant_manager',
    'tenant_cashier',
    'tenant_scanner',
    'tenant_accountant',
    'tenant_staff',
    'rider'
));
