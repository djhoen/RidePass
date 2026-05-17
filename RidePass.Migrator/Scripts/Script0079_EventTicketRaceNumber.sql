-- Per-event race number stamped on the rider's ticket. Different from the
-- rider's profile-level user.race_number (which is their preferred number) —
-- the per-purchase value is what timing uses for THIS event. Track admins can
-- override at check-in (e.g., a rider's preferred 21 conflicts with another
-- entry, so they're given 121 just for this event).
--
-- Nullable: when null, the UI/exports fall back to user.race_number.

ALTER TABLE event_ticket_purchase
    ADD COLUMN race_number text NULL;

-- Trackside imports key by race number, so a quick lookup index helps when
-- staff need to find "who is 21 in 250A" while looking at the timing screen.
CREATE INDEX idx_event_ticket_purchase_race_number
    ON event_ticket_purchase (tier_id, race_number)
    WHERE race_number IS NOT NULL;
