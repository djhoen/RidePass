-- F&B open tabs (Script0291) gave concession_sale two statuses that are not sales: 'open' (a tab
-- still being rung up; no money has been taken) and 'voided' (a tab torn up by a manager; no money
-- ever will be). The concession branch of v_recent_sales selected every concession_sale row, so
-- both showed up in Admin -> Purchases and on the dashboard's Recent Purchases panel as if they
-- were sales, inflating counts and listing amounts nobody collected. The branch now leaves them out,
-- the same way the gift_card branch leaves out imported cards: a tab appears in the feed the moment
-- it is paid (status 'paid', dated from when it was opened), and open/voided tabs stay visible where
-- staff work them, in F&B -> Orders. RecentSalesRepository also excludes both statuses by default so
-- the screens are right even before this script has run.
--
-- Definition copied from Script0272 with that single WHERE added; the column list is unchanged.

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
  WHERE cs.status NOT IN ('open', 'voided')
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
