-- Explicit per-event audience flags. Previously "who can attend" was inferred from
-- configuration (riders = has race classes or eligible passes; spectators = has a
-- gate-fee add-on), which is circular for brand-new events (race classes only exist
-- after the event is saved). These flags make the audience a first-class, editable
-- choice that drives the admin event dialog's sections and waiver options.
--
-- Backfill preserves current behavior:
--   allows_riders     -> true for every existing event (the column default), so rider
--                        entry stays available everywhere it was.
--   allows_spectators -> true only where the event already has a gate-fee add-on enabled
--                        (the current spectator-entry mechanism); false otherwise.
ALTER TABLE event ADD COLUMN IF NOT EXISTS allows_riders     boolean NOT NULL DEFAULT true;
ALTER TABLE event ADD COLUMN IF NOT EXISTS allows_spectators boolean NOT NULL DEFAULT false;

UPDATE event e
SET allows_spectators = true
WHERE EXISTS (
    SELECT 1
    FROM event_extra_eligibility ee
    JOIN event_extra_product p ON p.id = ee.product_id
    WHERE ee.event_id = e.id
      AND p.kind = 'gate_fee'
);
