-- Daily wristbands: one band per RIDER per tenant business day (item 9; item 10 attaches tickets).
--
-- event_wristband (Script0189 / Script0237) ties a band to one credential admission: a ticket, or one
-- season pass reservation. A rider who is admitted to two events on the same day would need two bands,
-- and nothing connects a band to the rider. A track wants the opposite: every rider wears one band per
-- day, and every admission they have that day is tied to it.
--
-- rider_day_band is that band. It belongs to a rider, keyed by the identity the gate already uses for
-- ID checks and photos (RiderPhotoRules: owner_key + rider_key, e.g. the buyer account and the holder's
-- name, or a dependent under their guardian), on the tenant-LOCAL business date. While it is active:
--   * a rider wears one band that day (uk_rider_day_band_rider), and
--   * a band code means one rider that day (uk_rider_day_band_code). Cheap number packs repeat ranges,
--     so a code only needs to be unique within its day.
-- A lost or broken band is voided with a reason (kept for audit) and its admissions move to the new one.
--
-- rider_day_band_admission lists the admissions a band covers that day: a season pass reservation now,
-- an event ticket from item 10 (the column is created here so the model does not change again).
--
-- event_wristband is left in place: ticket bands keep using it until item 10, and existing rows keep
-- resolving. New pass bands are written here only.
--
-- Additive and rerunnable.

-- Off by default: banding at check-in stays optional unless a track turns this on.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS wristband_required_at_checkin boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS rider_day_band (
    id                      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- The tenant-local date the band is good for (TenantClock), never a UTC date.
    business_date           date        NOT NULL,
    -- The scanned QR payload or typed number, trimmed; matched case-insensitively.
    code                    text        NOT NULL,
    -- Who wears it (RiderPhotoRules key): the same rider on a ticket and a pass maps to the same key.
    owner_key               text        NOT NULL,
    rider_key               text        NOT NULL,
    rider_user_id           uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    rider_name              text        NOT NULL,
    -- The credential it was issued through (a pass holder's pass; item 10 adds tickets).
    season_pass_purchase_id uuid        NULL REFERENCES season_pass_purchase(id) ON DELETE CASCADE,
    issued_by_user_id       uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    issued_at               timestamptz NOT NULL DEFAULT now(),
    voided_at               timestamptz NULL,
    voided_by_user_id       uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    void_reason             text        NULL,
    replaced_by_band_id     uuid        NULL REFERENCES rider_day_band(id) ON DELETE SET NULL,
    CONSTRAINT chk_rider_day_band_void CHECK ((voided_at IS NULL) = (void_reason IS NULL)),
    CONSTRAINT chk_rider_day_band_code CHECK (length(btrim(code)) > 0)
);

-- One active band per rider per day.
CREATE UNIQUE INDEX IF NOT EXISTS uk_rider_day_band_rider
    ON rider_day_band (tenant_id, business_date, owner_key, rider_key) WHERE voided_at IS NULL;
-- One active rider per band code per day.
CREATE UNIQUE INDEX IF NOT EXISTS uk_rider_day_band_code
    ON rider_day_band (tenant_id, business_date, lower(code)) WHERE voided_at IS NULL;
-- A scanned band, any day (history, and "that band was for another day").
CREATE INDEX IF NOT EXISTS idx_rider_day_band_lookup
    ON rider_day_band (tenant_id, lower(code), business_date DESC);
CREATE INDEX IF NOT EXISTS idx_rider_day_band_pass
    ON rider_day_band (season_pass_purchase_id) WHERE season_pass_purchase_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS rider_day_band_admission (
    id                         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                  uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    band_id                    uuid        NOT NULL REFERENCES rider_day_band(id) ON DELETE CASCADE,
    season_pass_reservation_id uuid        NULL REFERENCES season_pass_reservation(id) ON DELETE CASCADE,
    event_ticket_purchase_id   uuid        NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE,
    linked_by_user_id          uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    linked_at                  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_rider_day_band_admission_anchor
        CHECK (((season_pass_reservation_id IS NOT NULL)::int + (event_ticket_purchase_id IS NOT NULL)::int) = 1)
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_rider_day_band_admission_reservation
    ON rider_day_band_admission (band_id, season_pass_reservation_id) WHERE season_pass_reservation_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_rider_day_band_admission_ticket
    ON rider_day_band_admission (band_id, event_ticket_purchase_id) WHERE event_ticket_purchase_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_rider_day_band_admission_reservation
    ON rider_day_band_admission (season_pass_reservation_id) WHERE season_pass_reservation_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_rider_day_band_admission_ticket
    ON rider_day_band_admission (event_ticket_purchase_id) WHERE event_ticket_purchase_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_rider_day_band_admission_tenant
    ON rider_day_band_admission (tenant_id, band_id);

-- Review round (amended in place, unpushed): an admission that stops being checked in (un-checked at the
-- gate, cancelled, refunded) no longer counts toward a band. Done here, once, for every path that changes a
-- reservation's status, current and future; the band itself stays (the rider still wears it).
CREATE OR REPLACE FUNCTION rider_day_band_unlink_reservation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    DELETE FROM rider_day_band_admission WHERE season_pass_reservation_id = NEW.id;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_rider_day_band_unlink_reservation ON season_pass_reservation;
CREATE TRIGGER trg_rider_day_band_unlink_reservation
    AFTER UPDATE OF status ON season_pass_reservation
    FOR EACH ROW WHEN (OLD.status = 'checked_in' AND NEW.status IS DISTINCT FROM 'checked_in')
    EXECUTE FUNCTION rider_day_band_unlink_reservation();
