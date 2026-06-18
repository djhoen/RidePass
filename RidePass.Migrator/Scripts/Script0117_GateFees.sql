-- Gate fees as first-class per-event tiers + per-rider registrant grouping.
--
-- Previously spectator admission was a "Gate Fee" add-on (event_extra_product
-- kind=gate_fee) and/or a legacy spectator_pass tier, while race classes were
-- race_entry tiers. This unifies admission gates into the tier table:
--
--   race_entry  — a race class (rider competes); audience is always 'rider'
--   gate_fee    — a facility gate fee, audience 'rider' or 'spectator', optionally
--                 a REQUIRED purchase. For a race, a required rider gate fee means a
--                 rider must buy a class AND one rider gate fee (one gate per rider).
--
-- Existing spectator_pass tiers convert to spectator gate fees so current spectator
-- admission keeps working. The gate_fee add-on kind is retired in code (we stop
-- surfacing/selling it); existing rows are left in place but no longer offered.
--
-- registrant_id groups a rider's gate fee + their race-class entries within one order
-- so the post-payment registration step can attach each class to a rider (one rider
-- may hold several classes) and charge exactly one gate fee per rider.

-- 1. Tier kind: add gate_fee. Drop the old kind check (auto-named) and re-add.
ALTER TABLE event_ticket_tier DROP CONSTRAINT IF EXISTS event_ticket_tier_kind_check;
ALTER TABLE event_ticket_tier
    ADD CONSTRAINT event_ticket_tier_kind_check
    CHECK (kind IN ('spectator_pass', 'race_entry', 'gate_fee'));

-- 2. Allow $0 tiers (free kids entry / free gate). Original constraint required > 0.
ALTER TABLE event_ticket_tier DROP CONSTRAINT IF EXISTS event_ticket_tier_price_cents_check;
ALTER TABLE event_ticket_tier
    ADD CONSTRAINT event_ticket_tier_price_cents_check CHECK (price_cents >= 0);

-- 3. Audience + required flag. Race classes are always rider-audience; gate fees pick.
ALTER TABLE event_ticket_tier
    ADD COLUMN IF NOT EXISTS audience text NOT NULL DEFAULT 'rider'
        CHECK (audience IN ('rider', 'spectator')),
    ADD COLUMN IF NOT EXISTS required boolean NOT NULL DEFAULT false;

-- 4. Convert legacy spectator_pass tiers into spectator gate fees (not required by
--    default; spectator admission has always been the gate fee itself).
UPDATE event_ticket_tier
   SET kind = 'gate_fee', audience = 'spectator'
 WHERE kind = 'spectator_pass';

-- 5. Per-rider grouping for the post-payment registration step.
ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS registrant_id uuid NULL;

CREATE INDEX IF NOT EXISTS idx_event_ticket_purchase_registrant
    ON event_ticket_purchase (registrant_id)
    WHERE registrant_id IS NOT NULL;
