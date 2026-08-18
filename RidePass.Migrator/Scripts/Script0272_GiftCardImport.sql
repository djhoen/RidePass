-- Gift card import (legacy balances from a previous system, e.g. Highland's Card Dog cards) plus
-- the admin surface that goes with it.
--
-- 1) Imports carry only code + balance, so the buyer/recipient columns must allow NULL. The buy
--    flow still always writes them; only imported rows leave them empty.
-- 2) Provenance columns mark imported cards, keep them OUT of the Purchases feed (they are not
--    RidePass sales; the money was collected in the old system), and record who ran the import.
-- 3) Latent bug fix: the bike shop register records gift-card redemptions with
--    source_kind = 'shop_sale' (BikeShopRegisterController), but the CHECK (last set in
--    Script0057) never learned that value, so a shop sale tendered with a gift card violates the
--    constraint. Widen it here.
--
-- Rerunnable: DROP NOT NULL is idempotent, ADD COLUMN IF NOT EXISTS, constraint drop-then-add,
-- CREATE OR REPLACE VIEW.

ALTER TABLE gift_card ALTER COLUMN buyer_name       DROP NOT NULL;
ALTER TABLE gift_card ALTER COLUMN buyer_email      DROP NOT NULL;
ALTER TABLE gift_card ALTER COLUMN recipient_name   DROP NOT NULL;
ALTER TABLE gift_card ALTER COLUMN recipient_email  DROP NOT NULL;

ALTER TABLE gift_card ADD COLUMN IF NOT EXISTS imported_from        text        NULL;
ALTER TABLE gift_card ADD COLUMN IF NOT EXISTS imported_at          timestamptz NULL;
ALTER TABLE gift_card ADD COLUMN IF NOT EXISTS imported_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE gift_card_redemption DROP CONSTRAINT IF EXISTS gift_card_redemption_source_kind_check;
ALTER TABLE gift_card_redemption ADD CONSTRAINT gift_card_redemption_source_kind_check
    CHECK (source_kind IN ('pass', 'event_ticket', 'season_pass', 'rental', 'shop_sale'));

-- Recreate the recent-sales read model (definition from Script0200) with one change: imported
-- gift cards are excluded — they are outstanding liability brought over from another system, not
-- sales made through RidePass, and would otherwise inflate the Purchases feed.
CREATE OR REPLACE VIEW v_recent_sales AS
 SELECT 'event_ticket'::text AS kind, t.id, t.tenant_id, t.status, t.amount_cents,
        t.purchaser_user_id, t.purchaser_email, t.purchaser_name, t.stripe_payment_intent_id,
        tt.name AS item_name, t.created_at, t.redemption_token
   FROM event_ticket_purchase t
   LEFT JOIN event_ticket_tier tt ON tt.id = t.tier_id
 UNION ALL
 SELECT 'event_extra'::text, e.id, e.tenant_id, e.status, e.amount_cents,
        e.purchaser_user_id, e.purchaser_email, e.purchaser_name, e.stripe_payment_intent_id,
        ep.name, e.created_at, e.redemption_token
   FROM event_extra_purchase e
   LEFT JOIN event_extra_product ep ON ep.id = e.product_id
 UNION ALL
 SELECT 'season_pass'::text, s.id, s.tenant_id, s.status, s.amount_cents,
        s.purchaser_user_id, s.purchaser_email, s.purchaser_name, s.stripe_payment_intent_id,
        sp.name, s.created_at, s.redemption_token
   FROM season_pass_purchase s
   LEFT JOIN season_pass_product sp ON sp.id = s.product_id
 UNION ALL
 SELECT 'membership'::text, m.id, m.tenant_id, m.status, m.amount_cents,
        m.user_id, u.email,
        TRIM(BOTH FROM (COALESCE(u.first_name, '') || ' ') || COALESCE(u.last_name, '')),
        m.stripe_payment_intent_id, m.name_at_purchase, m.created_at, NULL::uuid
   FROM membership_purchase m
   LEFT JOIN users u ON u.id = m.user_id
 UNION ALL
 SELECT 'gift_card'::text, g.id, g.tenant_id, g.status, g.initial_amount_cents,
        g.buyer_user_id, g.buyer_email, g.buyer_name, g.stripe_payment_intent_id,
        'Gift Card $' || (g.initial_amount_cents / 100)::text, g.created_at, NULL::uuid
   FROM gift_card g
  WHERE g.imported_from IS NULL
 UNION ALL
 SELECT 'concession'::text, cs.id, cs.tenant_id, cs.status, cs.total_cents,
        cs.purchaser_user_id, cs.purchaser_email, cs.purchaser_name, cs.stripe_payment_intent_id,
        ('Food & Beverage (' || (SELECT COALESCE(sum(l.quantity), 0) FROM concession_sale_line l WHERE l.sale_id = cs.id)::text) || ' items)',
        cs.created_at, NULL::uuid
   FROM concession_sale cs
 UNION ALL
 SELECT 'shop_sale'::text, ss.id, ss.tenant_id, ss.status, ss.total_cents,
        ss.buyer_user_id, ss.buyer_email, ss.buyer_name, ss.stripe_payment_intent_id,
        ('Bike Shop (' || (SELECT COALESCE(sum(l.quantity), 0) FROM shop_sale_line l WHERE l.sale_id = ss.id)::text || ' items)'),
        ss.created_at, ss.receipt_token
   FROM shop_sale ss
 UNION ALL
 SELECT 'shop_rental'::text, sr.id, sr.tenant_id, sr.status, sr.total_cents,
        sr.renter_user_id, sr.renter_email, sr.renter_name, sr.stripe_payment_intent_id,
        ('Bike Shop Rental (' || (SELECT COALESCE(sum(l.quantity), 0) FROM shop_rental_line l WHERE l.rental_id = sr.id)::text || ' items)'),
        sr.created_at, sr.receipt_token
   FROM shop_rental sr;
