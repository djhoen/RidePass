-- Local-dev convenience: promote seed users so Phase 2 admin UI is testable.
-- Idempotent: only promotes if still a rider.

UPDATE users SET role = 'tenant_admin'
WHERE email IN ('admin@acme.test', 'admin@foothills.test')
  AND role = 'rider';
