-- Per-audience waiver-required flags on the event row. The single `requires_waiver`
-- bool gated everyone the same; with separate spectator and rider waivers
-- (Script0069), the event admin needs independent control over whether each
-- audience must sign on purchase.

ALTER TABLE event
    ADD COLUMN requires_rider_waiver     boolean NOT NULL DEFAULT false,
    ADD COLUMN requires_spectator_waiver boolean NOT NULL DEFAULT false;

-- Existing events: carry the legacy bit forward to both audiences.
UPDATE event SET
    requires_rider_waiver     = requires_waiver,
    requires_spectator_waiver = requires_waiver;

ALTER TABLE event DROP COLUMN requires_waiver;
