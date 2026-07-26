-- Whether more than one discount may land on the same sale.
--
-- A bike shop sale can currently attract three at once: a season-pass retail benefit the buyer
-- already paid for, a staff-applied discount the cashier chose (Script0251), and a promo code the
-- customer brought. Compounding all three is rarely what a track means to offer, and it is the
-- kind of thing nobody notices until someone works out that a member card plus a code plus the
-- military rate takes 40% off.
--
-- Default false, which is the conservative reading and a deliberate behaviour change from what
-- shipped: benefit and coupon previously stacked additively with no way to stop them. Off means
-- exactly ONE discount applies and it is the LARGEST of the candidates, so the customer still
-- gets the best deal available to them and no cashier has to work out which to pick. Taking the
-- best rather than refusing the sale keeps the counter moving; an error at the till over a policy
-- question is the worst place to surface it.
--
-- Scope note: this governs the bike shop register and work-order billing, where several distinct
-- discount SOURCES combine against one subtotal. The F&B POS has a structurally different model
-- (one discount per line plus one per order) which this does not currently touch; changing that
-- is a separate decision rather than something to fold in silently here.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS allow_discount_stacking boolean NOT NULL DEFAULT false;
