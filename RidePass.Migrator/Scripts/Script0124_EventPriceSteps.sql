-- Dynamic event pricing: turn event_ticket_tier rows into ordered price "steps".
-- Steps sharing a ladder_group on the same event escalate the price; the live price is
-- the highest-priced step whose trigger has fired (a min_sold threshold OR a date).
-- A step with all triggers NULL is the base (starting) price. ladder_group IS NULL means
-- a standalone tier, exactly today's behavior, so all existing rows are unaffected.
ALTER TABLE event_ticket_tier
    ADD COLUMN IF NOT EXISTS ladder_group          text        NULL,
    ADD COLUMN IF NOT EXISTS min_sold              int         NULL CHECK (min_sold IS NULL OR min_sold >= 0),
    ADD COLUMN IF NOT EXISTS effective_days_before int         NULL CHECK (effective_days_before IS NULL OR effective_days_before >= 0),
    ADD COLUMN IF NOT EXISTS effective_at_utc      timestamptz NULL;

-- Resolve a ladder quickly: all steps for one event's group.
CREATE INDEX IF NOT EXISTS idx_event_ticket_tier_ladder
    ON event_ticket_tier (event_id, ladder_group)
    WHERE ladder_group IS NOT NULL;
