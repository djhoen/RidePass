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

-- Review round (amended in place, unpushed):
-- claimed_at / claim_id: when (and by which attempt) the kiosk took the request for completion. A 'completing'
--   request whose claim is older than the lease (2 minutes) is stale: it no longer holds the kiosk and can be
--   taken again (a new claim_id) or expire. Only the current claim records results or finishes it.
-- end_reason: why a request ended without being done: declined (at the kiosk), waiver_changed,
--   mode_changed (e.g. the rider turned 18), no_longer_needed, or NULL (timed out / cancelled by staff).
-- photo_ticket_purchase_id: the ticket the gate's photo check uses, when it differs from the ticket the
--   waiver is fixed on (a registrant's rider ticket vs their waiver ticket).
ALTER TABLE kiosk_push
    ADD COLUMN IF NOT EXISTS claimed_at               timestamptz NULL,
    ADD COLUMN IF NOT EXISTS claim_id                 uuid        NULL,
    ADD COLUMN IF NOT EXISTS end_reason               text        NULL,
    ADD COLUMN IF NOT EXISTS photo_ticket_purchase_id uuid        NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_kiosk_push_end_reason' AND conrelid = 'kiosk_push'::regclass) THEN
        ALTER TABLE kiosk_push ADD CONSTRAINT chk_kiosk_push_end_reason
            CHECK (end_reason IS NULL OR end_reason IN ('declined', 'waiver_changed', 'mode_changed', 'no_longer_needed'));
    END IF;
END $$;

-- The open requests of a tenant in one read (staff's kiosk list); a kiosk's own is uq_kiosk_push_active.
CREATE INDEX IF NOT EXISTS idx_kiosk_push_open_tenant ON kiosk_push (tenant_id)
    WHERE status IN ('pending', 'in_progress', 'completing');
