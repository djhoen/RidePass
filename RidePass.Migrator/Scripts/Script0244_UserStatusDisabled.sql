-- Fix: disabling a tenant user has never worked. It throws a check-constraint violation.
--
-- Admin > Users offers "Disable user". The UI sends 'disabled'
-- (vueapp/src/views/Admin/Users.vue line 72), UserService passes it through, and
-- UserController.UpdateTenantUserStatus explicitly validates `status is "active" or "disabled"`
-- before UserRepository.UpdateStatus writes it verbatim. But users_status_check has only ever
-- allowed ('active', 'suspended', 'pending'), so the UPDATE fails with SQLSTATE 23514 and the
-- staff member stays active.
--
-- Verified against stage AND production: both carry the three-value constraint, and production
-- has zero non-active users, which is what a permanently failing off-switch looks like.
--
-- Why add 'disabled' rather than remap the endpoint to 'suspended':
--   * 'suspended' is the SUPER-ADMIN action (SuperAdminController line 496): the platform
--     suspending an account across every tenant. 'disabled' is a tenant admin turning off one
--     of their own staff. Same shape, different actor and different blast radius, and the audit
--     trail reads better when they are not the same word.
--   * Remapping would mean changing the controller, the DTO, the service, the UI's status
--     filter, and every comparison against 'disabled' — a lot of churn to make a working UI
--     speak a different word, with more chances to miss one.
--   * Nothing reads 'disabled' today (it could never be written), so adding the value cannot
--     change existing behavior. It only makes the button do what it always claimed to.
--
-- This is load-bearing for employee passes (Script0242): their validity is derived from
-- users.status, so an off-switch that cannot be flipped means an employee pass could never be
-- revoked by deactivating the employee.
--
-- Additive and rerunnable: the constraint is dropped and recreated by name only if the new
-- value is missing, so a re-run is a no-op.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'users_status_check'
          AND conrelid = 'users'::regclass
          AND pg_get_constraintdef(oid) LIKE '%disabled%'
    ) THEN
        ALTER TABLE users DROP CONSTRAINT IF EXISTS users_status_check;
        ALTER TABLE users
            ADD CONSTRAINT users_status_check
            CHECK (status = ANY (ARRAY['active'::text, 'suspended'::text, 'pending'::text, 'disabled'::text]));
    END IF;
END $$;
