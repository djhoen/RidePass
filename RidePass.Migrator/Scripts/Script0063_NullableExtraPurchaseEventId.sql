-- Counter sales need to sell add-ons as merchandise (no event attachment).
-- The existing flow (rider purchase via event detail) still sets event_id.
ALTER TABLE event_extra_purchase
    ALTER COLUMN event_id DROP NOT NULL;
