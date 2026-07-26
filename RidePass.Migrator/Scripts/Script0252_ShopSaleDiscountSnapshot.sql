-- Lets the bike shop register take a staff-applied discount (Script0251's tenant-wide list) and
-- records which one, so a discounted sale explains itself.
--
-- shop_sale already had discount_cents, but only an amount: the existing discounts are a coupon
-- the customer supplies and an automatic season-pass benefit, both of which can be reconstructed
-- from other rows. A staff-applied discount cannot. "Why is this $18 instead of $20" has no answer
-- without the label, and "who allowed it" has none without the authorizing manager. concession_sale
-- already snapshots exactly this for the F&B counter (Script0160); this brings retail in line.
--
-- All three are nullable and default NULL, which is what every existing sale means: no
-- staff-applied discount was involved.

ALTER TABLE shop_sale
    ADD COLUMN IF NOT EXISTS discount_preset_id uuid NULL REFERENCES discount_preset(id) ON DELETE SET NULL;

-- The name as it read at the time. Kept alongside the id on purpose: a track that renames
-- "Military 10%" to "Military 15%" must not silently rewrite what last season's receipts say, and
-- ON DELETE SET NULL above would otherwise erase the reason entirely.
ALTER TABLE shop_sale
    ADD COLUMN IF NOT EXISTS discount_label text NULL;

-- Who authorised it, when the discount was one the tenant marked as needing a manager PIN.
ALTER TABLE shop_sale
    ADD COLUMN IF NOT EXISTS discount_authorized_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

-- Finding every sale that used a given discount, for a "what did we give away" review.
CREATE INDEX IF NOT EXISTS idx_shop_sale_discount_preset
    ON shop_sale (tenant_id, discount_preset_id)
    WHERE discount_preset_id IS NOT NULL;
