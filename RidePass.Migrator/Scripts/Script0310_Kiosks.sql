-- Gate: account-creation kiosks.
--
-- A tablet at the front desk, paired by a manager, where a walk-up creates their own profile (name,
-- email, birthdate), signs the waiver (a parent or guardian for a minor) and takes their photo. The
-- sign-up shows on the gate screens so the cashier picks it and sells the day pass.
--
-- kiosk_device: one row per paired tablet. The device's secret is stored only as a SHA-256 hash; the
-- one-time pairing code likewise (with its expiry). Revoking a device is immediate. Later items address
-- pushed steps (a waiver or photo for a profile) to a specific device by its id.
--
-- kiosk_signup: one row per completed kiosk sign-up, with the profile it made or used (rider account,
-- dependent, guardian), the signature and the photo, and whether a sale has used it.
--
-- Additive; rerunnable (CREATE ... IF NOT EXISTS, DROP TRIGGER IF EXISTS before CREATE TRIGGER).

CREATE TABLE IF NOT EXISTS kiosk_device (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name                text        NOT NULL,
    credential_hash     text        NULL,
    pairing_code_hash   text        NULL,
    pairing_expires_at  timestamptz NULL,
    paired_at           timestamptz NULL,
    last_seen_at        timestamptz NULL,
    revoked_at          timestamptz NULL,
    revoked_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_kiosk_device_tenant ON kiosk_device (tenant_id, created_at);
CREATE UNIQUE INDEX IF NOT EXISTS uq_kiosk_device_credential
    ON kiosk_device (credential_hash) WHERE credential_hash IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_kiosk_device_pairing_code
    ON kiosk_device (tenant_id, pairing_code_hash) WHERE pairing_code_hash IS NOT NULL;

DROP TRIGGER IF EXISTS trg_kiosk_device_updated_at ON kiosk_device;
CREATE TRIGGER trg_kiosk_device_updated_at
    BEFORE UPDATE ON kiosk_device
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS kiosk_signup (
    id                   uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id            uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    kiosk_device_id      uuid        NULL REFERENCES kiosk_device(id) ON DELETE SET NULL,
    user_id              uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    dependent_id         uuid        NULL REFERENCES rider_dependent(id) ON DELETE SET NULL,
    guardian_user_id     uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    first_name           text        NOT NULL,
    last_name            text        NOT NULL,
    birthdate            date        NULL,
    is_minor             boolean     NOT NULL DEFAULT false,
    signature_id         uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    photo_id             uuid        NULL REFERENCES rider_photo(id) ON DELETE SET NULL,
    created_at           timestamptz NOT NULL DEFAULT now(),
    handled_at           timestamptz NULL,
    handled_by_user_id   uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    sale_ref             text        NULL,
    dismissed_at         timestamptz NULL,
    dismissed_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_kiosk_signup_tenant_created ON kiosk_signup (tenant_id, created_at DESC);

-- A sign-up whose rider or guardian email matches an existing account is held PENDING: nothing is
-- written to that account until a cashier checks ID and confirms. Until then the row keeps what was
-- typed, the signature image and the photo (filed under "kiosk:{id}", not under any account).
ALTER TABLE kiosk_signup
    ADD COLUMN IF NOT EXISTS match_pending            boolean     NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS matched_user_id          uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS matched_guardian_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS email                    text        NULL,
    ADD COLUMN IF NOT EXISTS phone                    text        NULL,
    ADD COLUMN IF NOT EXISTS guardian_first_name      text        NULL,
    ADD COLUMN IF NOT EXISTS guardian_last_name       text        NULL,
    ADD COLUMN IF NOT EXISTS guardian_email           text        NULL,
    ADD COLUMN IF NOT EXISTS guardian_phone           text        NULL,
    ADD COLUMN IF NOT EXISTS guardian_birthdate       date        NULL,
    ADD COLUMN IF NOT EXISTS emergency_contact_name   text        NULL,
    ADD COLUMN IF NOT EXISTS emergency_contact_phone  text        NULL,
    ADD COLUMN IF NOT EXISTS held_signature_data_url  text        NULL,
    ADD COLUMN IF NOT EXISTS held_waiver_id           uuid        NULL REFERENCES tenant_waiver(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS held_signed_at           timestamptz NULL,
    ADD COLUMN IF NOT EXISTS confirmed_at             timestamptz NULL,
    ADD COLUMN IF NOT EXISTS confirmed_by_user_id     uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    -- The ticket of the sale that used this sign-up: it counts as handled once that ticket is paid.
    ADD COLUMN IF NOT EXISTS sale_ticket_id           uuid        NULL REFERENCES event_ticket_purchase(id) ON DELETE SET NULL;

-- A held kiosk photo belongs to no account yet: it is filed under the sign-up ("kiosk:{id}"). Widen the
-- owner check (Script0307) to allow that key; widening is backwards-compatible. Rerunnable: only replaced
-- while it doesn't allow it yet.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'chk_rider_photo_owner' AND conrelid = 'rider_photo'::regclass
                      AND pg_get_constraintdef(oid) LIKE '%kiosk:%') THEN
        ALTER TABLE rider_photo DROP CONSTRAINT IF EXISTS chk_rider_photo_owner;
        ALTER TABLE rider_photo ADD CONSTRAINT chk_rider_photo_owner CHECK (user_id IS NOT NULL OR owner_email IS NOT NULL
            OR owner_key LIKE 'ticket:%' OR owner_key LIKE 'pass:%' OR owner_key LIKE 'kiosk:%');
    END IF;
END $$;
