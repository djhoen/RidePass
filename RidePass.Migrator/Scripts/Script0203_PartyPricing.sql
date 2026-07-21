-- Lessons step 5: party pricing ("up to 3 riders, one price").
--
-- See docs/lessons.md section 3.1. Three shapes exist in the market:
--   Trestle:        one flat price covering up to 3 riders, no fee for the 2nd and 3rd
--   Mountain Creek: a base price plus a fixed add-a-friend fee per extra rider
--   Deer Valley:    plain per-person pricing
--
-- IMPLEMENTATION NOTE (revised from the doc's first sketch): this does NOT make one ticket cover
-- several riders. That would break the one-ticket-one-rider invariant that event capacity counting,
-- the check-in roster, post-payment registration, and per-rider waivers all depend on. Instead each
-- rider still gets their own ticket row and we vary the PRICE by position within the purchase:
--   unit 0                      -> price_cents (the base)
--   units 1 .. included-1       -> free (covered by the base)
--   units included and beyond   -> party_price_cents
-- Totals come out identical to all three market shapes while every existing invariant holds.
--
-- Defaults reproduce today's behavior exactly: included = 1 and party_price_cents NULL means
-- every unit costs price_cents, which is what every existing tier does.

ALTER TABLE event_ticket_tier
    -- How many riders the single base price covers. 1 = ordinary per-person pricing.
    ADD COLUMN IF NOT EXISTS party_size_included int NOT NULL DEFAULT 1
        CHECK (party_size_included >= 1),
    -- Price for each rider beyond party_size_included. NULL = charge the base price for them
    -- (i.e. ordinary per-person pricing continues past the included count).
    ADD COLUMN IF NOT EXISTS party_price_cents   int NULL
        CHECK (party_price_cents IS NULL OR party_price_cents >= 0),
    -- Hard cap on riders in one purchase of this tier. NULL = no party cap (existing behavior;
    -- tier inventory and event capacity still apply).
    ADD COLUMN IF NOT EXISTS party_size_max      int NULL
        CHECK (party_size_max IS NULL OR party_size_max >= 1);

-- A max below the included count would be self-contradictory ("covers 3, but you may buy 2").
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_event_ticket_tier_party_sizes') THEN
        ALTER TABLE event_ticket_tier ADD CONSTRAINT chk_event_ticket_tier_party_sizes
            CHECK (party_size_max IS NULL OR party_size_max >= party_size_included);
    END IF;
END $$;
