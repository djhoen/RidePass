-- Shop labor rate: a tenant-level $/hour so labor lines derive their price from hours instead of
-- typing a total on every line, and so labor dollars can later be reported per clock-hour.
--
-- All columns nullable and additive:
--   tenant.shop_labor_rate_cents   NULL = no rate set (labor lines still take a typed price, as today)
--   shop_work_order_line.labor_hours / labor_rate_cents
--       Set together when a labor line was entered by hours; the line's unit_price_cents is then
--       hours * rate. Left NULL for a flat-priced labor line (and for every pre-existing line), so
--       the historical typed prices are untouched.
-- Rerunnable via IF NOT EXISTS.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS shop_labor_rate_cents integer;

ALTER TABLE shop_work_order_line
    ADD COLUMN IF NOT EXISTS labor_hours numeric(6,2);

ALTER TABLE shop_work_order_line
    ADD COLUMN IF NOT EXISTS labor_rate_cents integer;
