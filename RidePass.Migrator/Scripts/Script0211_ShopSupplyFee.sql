-- Shop supply fee on a repair bill: the solvent, rag, lube, zip-tie, and disposal cost that a
-- shop absorbs on every job and almost never itemises.
--
-- From the Lightspeed DMS comparison in docs/bike-shop.md, where "shop supplies, hazardous waste,
-- freight, shipping, and storage fees" are "calculated automatically based on labor percentages
-- and customizable caps". Same model here, deliberately narrowed to one fee: a track shop wants
-- "5% of labor, capped at $15", not a fee schedule.
--
-- Computed from LABOR only, not parts. Charging a percentage of a $900 fork means the fee tracks
-- how expensive the part was rather than how much shop consumable the job burned, which is both
-- wrong and the thing customers notice.
--
-- Defaults to 0 bps = off, so no existing tenant silently starts adding a line to bills.

ALTER TABLE tenant
    -- Basis points of the labor subtotal. 500 = 5%. 0 turns the fee off entirely.
    ADD COLUMN IF NOT EXISTS shop_supply_fee_bps       int  NOT NULL DEFAULT 0
        CHECK (shop_supply_fee_bps >= 0 AND shop_supply_fee_bps <= 5000),
    -- Ceiling in cents. NULL = uncapped. A cap is what stops a big engine job carrying an
    -- absurd consumables charge.
    ADD COLUMN IF NOT EXISTS shop_supply_fee_cap_cents int  NULL
        CHECK (shop_supply_fee_cap_cents IS NULL OR shop_supply_fee_cap_cents >= 0),
    -- What the customer reads on the bill. Tenants call this different things.
    ADD COLUMN IF NOT EXISTS shop_supply_fee_label     text NOT NULL DEFAULT 'Shop supplies';
