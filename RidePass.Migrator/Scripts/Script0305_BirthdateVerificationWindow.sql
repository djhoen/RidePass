-- Gate check-in: verify a rider's birthdate against an ID once per year, not every visit.
--
-- 1. Tenant policy. birthdate_verify_mode:
--      'off'      nothing changes (default, so every existing tenant keeps today's behaviour);
--      'prompt'   the gate shows "Check ID: birthdate not verified since ..." with a Verify button,
--                 but admits;
--      'required' the gate refuses a rider-audience ticket or pass whose birthdate has not been
--                 verified within birthdate_verify_valid_days of the tenant-local check-in date.
--    birthdate_verify_valid_days defaults to 365 (once a year).
--
-- 2. rider_id_verification: a tenant's record that it checked a RIDER ACCOUNT's ID. Rider accounts
--    are platform-wide (users.tenant_id IS NULL), so the existing users.id_verified_* columns
--    (Script0238) can only ever be written for a tenant's own staff accounts; a rider verified at
--    the gate was recorded on the ticket / pass alone and had to be re-checked on the next one.
--    One row per (tenant, account, rider) makes a verification carry to every later ticket and
--    pass at that tenant, without one track's check ever counting at another.
--
--    rider_key is the RIDER the check was for, as the normalized (lowercased, single-spaced)
--    rider / holder name on the credential; the account owner's own check uses their own name.
--    So a parent who buys for themselves and a child holds two rows under one account, the
--    child's later tickets find the child's row, and the child's DOB never answers for the
--    parent (or the other way round).
--
-- Additive: new columns carry defaults, the new table is unused by older code.
-- Rerunnable: ADD COLUMN IF NOT EXISTS, guarded constraints, CREATE TABLE / INDEX IF NOT EXISTS.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS birthdate_verify_mode text NOT NULL DEFAULT 'off',
    ADD COLUMN IF NOT EXISTS birthdate_verify_valid_days integer NOT NULL DEFAULT 365;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_birthdate_verify_mode') THEN
        ALTER TABLE tenant ADD CONSTRAINT chk_tenant_birthdate_verify_mode
            CHECK (birthdate_verify_mode IN ('off', 'prompt', 'required'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_birthdate_verify_valid_days') THEN
        ALTER TABLE tenant ADD CONSTRAINT chk_tenant_birthdate_verify_valid_days
            CHECK (birthdate_verify_valid_days BETWEEN 1 AND 3650);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS rider_id_verification (
    id                  uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id             uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    rider_key           text NOT NULL,
    verified_at         timestamptz NOT NULL DEFAULT now(),
    verified_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    verified_dob        date NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);

-- A database that ran an earlier draft of this script (unpushed) has the table keyed on
-- (tenant, account) only: add the rider key and re-key it. The draft never shipped, so any such
-- table is a local dev one; its rows (if any) are cleared rather than guessed at.
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'rider_id_verification' AND column_name = 'rider_key') THEN
        DELETE FROM rider_id_verification;
        ALTER TABLE rider_id_verification ADD COLUMN rider_key text NOT NULL;
    END IF;
END $$;
DROP INDEX IF EXISTS uk_rider_id_verification_tenant_user;

CREATE UNIQUE INDEX IF NOT EXISTS uk_rider_id_verification_tenant_user_rider
    ON rider_id_verification (tenant_id, user_id, rider_key);
