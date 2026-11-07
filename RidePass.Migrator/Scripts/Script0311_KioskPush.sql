-- Gate: staff push a waiver and / or photo step for a specific rider to a specific front-desk kiosk.
--
-- A cashier at the gate (or Gate Sale, Event Riders, the kiosk list) picks an active kiosk and the steps
-- the rider still needs; the kiosk, while idle, picks the request up and shows only those steps for that
-- rider. Completion goes through the same paths as a signing link (targeted signing with the guardian
-- rules and the gate's linking) and the gate's own photo store, so the gate's verdict is identical.
--
-- kiosk_push: one row per request. Target: a ticket, a season pass (+ the event it is judged for), or a
-- kiosk sign-up's profile. At most one active (pending / in_progress) request per kiosk. The id is a
-- random uuid and only the kiosk it was sent to can read or complete it, once, before it expires.
-- kiosk_device.last_poll_at: when the kiosk last asked for work (it asks only while idle), so staff can
-- see "Kiosk busy" when someone is mid sign-up on it.
--
-- Additive; rerunnable.

CREATE TABLE IF NOT EXISTS kiosk_push (
    id                      uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id               uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    kiosk_device_id         uuid        NOT NULL REFERENCES kiosk_device(id) ON DELETE CASCADE,
    created_by_user_id      uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    expires_at              timestamptz NOT NULL,
    want_waiver             boolean     NOT NULL DEFAULT false,
    want_photo              boolean     NOT NULL DEFAULT false,
    target_kind             text        NOT NULL CHECK (target_kind IN ('ticket', 'season_pass', 'kiosk_signup')),
    event_ticket_purchase_id uuid       NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE,
    season_pass_purchase_id uuid        NULL REFERENCES season_pass_purchase(id) ON DELETE CASCADE,
    event_id                uuid        NULL REFERENCES event(id) ON DELETE SET NULL,
    kiosk_signup_id         uuid        NULL REFERENCES kiosk_signup(id) ON DELETE CASCADE,
    sign_mode               text        NULL CHECK (sign_mode IN ('guardian', 'adult')),
    waiver_id               uuid        NULL REFERENCES tenant_waiver(id) ON DELETE SET NULL,
    rider_first_name        text        NOT NULL,
    status                  text        NOT NULL DEFAULT 'pending'
                                        CHECK (status IN ('pending', 'in_progress', 'completing', 'done', 'cancelled', 'expired')),
    started_at              timestamptz NULL,
    completed_at            timestamptz NULL,
    cancelled_at            timestamptz NULL,
    cancelled_by_user_id    uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    result_signature_id     uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    result_photo_id         uuid        NULL REFERENCES rider_photo(id) ON DELETE SET NULL,
    CONSTRAINT chk_kiosk_push_steps CHECK (want_waiver OR want_photo)
);

CREATE INDEX IF NOT EXISTS idx_kiosk_push_tenant ON kiosk_push (tenant_id, created_at DESC);
-- One active request per kiosk.
CREATE UNIQUE INDEX IF NOT EXISTS uq_kiosk_push_active
    ON kiosk_push (kiosk_device_id) WHERE status IN ('pending', 'in_progress', 'completing');

ALTER TABLE kiosk_device ADD COLUMN IF NOT EXISTS last_poll_at timestamptz NULL;
