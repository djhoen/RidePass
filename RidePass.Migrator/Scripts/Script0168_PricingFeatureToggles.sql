-- Tenant feature toggles for the two advanced pricing features, both super-admin
-- controlled (Feature Toggles tab) and default OFF:
--   dynamic_pricing_enabled: stepped ticket-price ladders (price rises by date or sales volume)
--   bundled_coupons_enabled: race-entry tiers minting single-use share coupons at purchase
-- No backfill needed: as of 2026-07 no tenant on prod or stage has any ladder_group or
-- bundled_coupon_count config on event_ticket_tier (verified), so defaulting off changes nothing.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS dynamic_pricing_enabled boolean NOT NULL DEFAULT false;
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS bundled_coupons_enabled boolean NOT NULL DEFAULT false;
