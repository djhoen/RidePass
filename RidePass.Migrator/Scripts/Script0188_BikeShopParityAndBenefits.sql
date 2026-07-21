-- Bike shop parity pass: low-stock alerts, shop coupons, and season-pass benefits at the shop.
--
-- Three small pieces from the Lightspeed gap list (docs/bike-shop.md) plus the benefits hook:
--   1. Low-stock thresholds on shop variants (the concession low-stock pattern, applied to the
--      shop's real decremented inventory).
--   2. Coupons at the shop register: 'shop' joins the coupon scopes and 'shop_sale' the
--      redemption source kinds.
--   3. season_pass_benefit learns 'retail', so a pass can grant "15% off the bike shop" —
--      applied at the shop register (and 'rental' at rental booking), each surface's own
--      checkout, never mixing catalogs.
--
-- All additive + idempotent.

-- ── 1. Low-stock alerting ────────────────────────────────────────────────────
-- threshold NULL = no alerting for this variant. notified_at is the de-dupe stamp: set when an
-- alert fires, cleared when stock rises back above threshold, so each low episode alerts once.
ALTER TABLE shop_variant
    ADD COLUMN IF NOT EXISTS low_stock_threshold  int         NULL CHECK (low_stock_threshold IS NULL OR low_stock_threshold >= 0),
    ADD COLUMN IF NOT EXISTS low_stock_notified_at timestamptz NULL;

-- ── 2. Coupons at the shop register ──────────────────────────────────────────
ALTER TABLE coupon DROP CONSTRAINT IF EXISTS coupon_applicable_scope_check;
ALTER TABLE coupon ADD CONSTRAINT coupon_applicable_scope_check
    CHECK (applicable_scope IN ('all','pass','event_ticket','season_pass','rental','shop'));

ALTER TABLE coupon_redemption DROP CONSTRAINT IF EXISTS coupon_redemption_source_kind_check;
ALTER TABLE coupon_redemption ADD CONSTRAINT coupon_redemption_source_kind_check
    CHECK (source_kind IN ('pass','event_ticket','season_pass','rental','shop_sale'));

-- ── 3. Retail benefits ───────────────────────────────────────────────────────
ALTER TABLE season_pass_benefit DROP CONSTRAINT IF EXISTS season_pass_benefit_benefit_type_check;
ALTER TABLE season_pass_benefit ADD CONSTRAINT season_pass_benefit_benefit_type_check
    CHECK (benefit_type IN ('event','concession','rental','buddy_pass','retail'));
