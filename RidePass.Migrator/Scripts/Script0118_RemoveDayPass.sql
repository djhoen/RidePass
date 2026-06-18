-- Remove the Day Pass (pass_product / pass_purchase) subsystem entirely. Open Ride and
-- Practice cover what Day Pass did, and non-race rider entry now uses rider gate fees.
-- No live clients yet, so this hard-drops the tables and their dependents instead of
-- retiring code only. Season passes and memberships are unrelated and stay.

-- 1. Stop seeding a default Day Pass on tenant creation (the function/trigger reference
--    pass_product, so they must go before the table is dropped).
DROP TRIGGER IF EXISTS trg_tenant_insert_pass_products ON tenant;
DROP FUNCTION IF EXISTS seed_default_pass_products();

-- 2. Rebuild the unified sales read model without the pass branch so the table can drop.
CREATE OR REPLACE VIEW v_recent_sales AS
 SELECT 'event_ticket'::text AS kind, t.id, t.tenant_id, t.status, t.amount_cents,
        t.purchaser_user_id, t.purchaser_email, t.purchaser_name, t.stripe_payment_intent_id,
        tt.name AS item_name, t.created_at
   FROM event_ticket_purchase t
   LEFT JOIN event_ticket_tier tt ON tt.id = t.tier_id
 UNION ALL
 SELECT 'event_extra'::text, e.id, e.tenant_id, e.status, e.amount_cents,
        e.purchaser_user_id, e.purchaser_email, e.purchaser_name, e.stripe_payment_intent_id,
        ep.name, e.created_at
   FROM event_extra_purchase e
   LEFT JOIN event_extra_product ep ON ep.id = e.product_id
 UNION ALL
 SELECT 'season_pass'::text, s.id, s.tenant_id, s.status, s.amount_cents,
        s.purchaser_user_id, s.purchaser_email, s.purchaser_name, s.stripe_payment_intent_id,
        sp.name, s.created_at
   FROM season_pass_purchase s
   LEFT JOIN season_pass_product sp ON sp.id = s.product_id
 UNION ALL
 SELECT 'membership'::text, m.id, m.tenant_id, m.status, m.amount_cents,
        m.user_id, u.email,
        TRIM(BOTH FROM (COALESCE(u.first_name, '') || ' ') || COALESCE(u.last_name, '')),
        m.stripe_payment_intent_id, m.name_at_purchase, m.created_at
   FROM membership_purchase m
   LEFT JOIN users u ON u.id = m.user_id
 UNION ALL
 SELECT 'gift_card'::text, g.id, g.tenant_id, g.status, g.initial_amount_cents,
        g.buyer_user_id, g.buyer_email, g.buyer_name, g.stripe_payment_intent_id,
        'Gift Card $' || (g.initial_amount_cents / 100)::text, g.created_at
   FROM gift_card g
 UNION ALL
 SELECT 'rental'::text, r.id, r.tenant_id, r.status, r.amount_cents,
        r.purchaser_user_id, r.purchaser_email, r.purchaser_name, r.rental_pi_id,
        rp.name, r.created_at
   FROM rental_purchase r
   LEFT JOIN rental_product rp ON rp.id = r.product_id
 UNION ALL
 SELECT 'concession'::text, cs.id, cs.tenant_id, cs.status, cs.total_cents,
        NULL::uuid, NULL::text, NULL::text, cs.stripe_payment_intent_id,
        ('Concession (' || (SELECT COALESCE(sum(l.quantity), 0) FROM concession_sale_line l WHERE l.sale_id = cs.id)::text) || ' items)',
        cs.created_at
   FROM concession_sale cs;

-- 3. Drop dispute's link to pass purchases, then the day-pass tables (children first).
ALTER TABLE dispute DROP COLUMN IF EXISTS pass_purchase_id;
DROP TABLE IF EXISTS event_pass_eligibility;
DROP TABLE IF EXISTS pass_purchase;
DROP TABLE IF EXISTS pass_product;
