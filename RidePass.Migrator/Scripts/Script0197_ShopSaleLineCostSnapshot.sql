-- Margin reporting: snapshot the unit COST onto each sale line at ring-up (pool parts use the
-- variant's cost, serialized units their acquired cost), so COGS/margin reports reflect what
-- the goods actually cost WHEN they sold, not whatever the cost is today. Historic lines stay
-- NULL and the reports fall back to the variant's current cost (documented approximation).
--
-- Additive + idempotent.

ALTER TABLE shop_sale_line ADD COLUMN IF NOT EXISTS unit_cost_cents_frozen int NULL;
