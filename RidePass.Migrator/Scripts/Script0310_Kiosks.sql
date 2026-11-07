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
