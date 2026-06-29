-- Product-level inventory + a quick per-day "86 / sold out" toggle for concession items.
--
-- (1) inventory: stock count for SIMPLE items that have no size/color variants (a variant item already
--     tracks stock on concession_variant.inventory). NULL = unlimited, which is the existing behavior,
--     so every current row keeps working unchanged.
-- (2) sold_out_date: the business date an item was manually 86'd for. While it equals today's (UTC)
--     business date the item is unavailable; the next day it is stale and the item is automatically
--     available again. NULL = not 86'd. No backfill needed (NULL preserves current behavior).
ALTER TABLE concession_product ADD COLUMN IF NOT EXISTS inventory int NULL;
ALTER TABLE concession_product ADD COLUMN IF NOT EXISTS sold_out_date date NULL;
