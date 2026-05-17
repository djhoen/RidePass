-- Race-entry tiers can include a bundle of coupon codes that are auto-minted for
-- the purchaser when their race-entry payment lands. Riders typically get a stack
-- of spectator-pass discount codes to give to friends and family.
--
-- All four bundled_coupon_* fields are NULL for tiers that don't issue codes.
-- bundled_coupon_count is the gating field — when null/0 nothing is generated;
-- when > 0 the other three describe the coupons that get minted.

ALTER TABLE event_ticket_tier
    ADD COLUMN bundled_coupon_count            int  NULL CHECK (bundled_coupon_count IS NULL OR bundled_coupon_count > 0),
    ADD COLUMN bundled_coupon_discount_kind    text NULL CHECK (bundled_coupon_discount_kind IS NULL OR bundled_coupon_discount_kind IN ('percent','amount')),
    ADD COLUMN bundled_coupon_discount_value   int  NULL CHECK (bundled_coupon_discount_value IS NULL OR bundled_coupon_discount_value > 0),
    ADD COLUMN bundled_coupon_scope            text NULL CHECK (bundled_coupon_scope IS NULL OR bundled_coupon_scope IN ('all','day_pass','event_ticket','season_pass')),
    ADD COLUMN bundled_coupon_expires_in_days  int  NULL CHECK (bundled_coupon_expires_in_days IS NULL OR bundled_coupon_expires_in_days > 0);

-- Coupons issued to a specific rider as part of a race-entry purchase. Public
-- tenant coupons leave both columns NULL; rider-issued coupons set both so we can
-- render them under "My Passes" filtered by the source purchase.
ALTER TABLE coupon
    ADD COLUMN issued_to_user_id     uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN issued_from_purchase_id uuid NULL;

CREATE INDEX idx_coupon_issued_to_user
    ON coupon (issued_to_user_id, issued_from_purchase_id)
    WHERE issued_to_user_id IS NOT NULL;
