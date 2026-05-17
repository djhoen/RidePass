-- Events can require different waivers for spectators vs. racers (e.g. a simple
-- liability for spectators and a more involved race-day waiver for entrants).
-- Replace the single event.waiver_id with two nullable FKs. Each is optional —
-- null = use the tenant's default active waiver as a fallback.

DROP INDEX IF EXISTS idx_event_waiver_id;

ALTER TABLE event
    ADD COLUMN spectator_waiver_id uuid NULL REFERENCES tenant_waiver(id) ON DELETE SET NULL,
    ADD COLUMN racer_waiver_id     uuid NULL REFERENCES tenant_waiver(id) ON DELETE SET NULL;

-- Carry any existing waiver_id forward as the racer waiver — race-day waivers
-- are typically the more substantive one, and the only events using waiver_id
-- so far have been race events.
UPDATE event SET racer_waiver_id = waiver_id WHERE waiver_id IS NOT NULL;

ALTER TABLE event DROP COLUMN waiver_id;

CREATE INDEX idx_event_spectator_waiver ON event (spectator_waiver_id) WHERE spectator_waiver_id IS NOT NULL;
CREATE INDEX idx_event_racer_waiver     ON event (racer_waiver_id)     WHERE racer_waiver_id     IS NOT NULL;
