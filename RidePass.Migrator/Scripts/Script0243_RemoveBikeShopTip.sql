-- Drops tipping from the bike shop. A tip belongs on a food order, not on a repair bill or a
-- parts sale: nobody tips for a chain replacement, and offering the field on the register invites
-- an awkward exchange at the counter every single time. Concessions keep tipping; this is a
-- bike-shop-only removal and concession_sale.tip_cents is deliberately untouched.
--
-- Safe to drop rather than deprecate, confirmed before writing this:
--   * Production has never run a single bike shop migration, so shop_sale does not exist there
--     and no tip has ever been taken.
--   * Local dev has 48 shop sales, none with a tip.
--   * The accounting view (Script0175) reads tip_cents from concession_sale ONLY; there is no
--     shop_sale join, so no bike shop tip ever reached the ledger, QuickBooks, or a tip
--     liability account. Nothing downstream loses history by this going away.
--
-- The table guard is not ceremony. The stage database journals Script0182_BikeShopSale as having
-- run while shop_sale, shop_sale_line and shop_tax_category do not exist there in any schema, so
-- a bare ALTER TABLE would fail on stage: DROP COLUMN IF EXISTS covers a missing column, not a
-- missing table. Worth looking into separately, since a journal that disagrees with the schema
-- will bite something else eventually.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'shop_sale'
    ) THEN
        ALTER TABLE shop_sale DROP COLUMN IF EXISTS tip_cents;
    END IF;
END $$;
