-- Expose redemption_token on v_recent_sales so the Admin Purchases search can match the
-- rider-facing "Order #" (derived from the redemption token) alongside the internal
-- purchase id and the Stripe payment-intent id. Membership, gift card, and concession
-- have no redemption token, so they emit NULL.
--
-- Based on the current definition (Script0118, which dropped the day-pass branch and
-- added concession). CREATE OR REPLACE VIEW requires the existing columns in the same
-- order/type; the new redemption_token column is appended last in every UNION branch.

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
 UNION ALL
 SELECT 'rental'::text, r.id, r.tenant_id, r.status, r.amount_cents,
        r.purchaser_user_id, r.purchaser_email, r.purchaser_name, r.rental_pi_id,
        rp.name, r.created_at, r.redemption_token
   FROM rental_purchase r
   LEFT JOIN rental_product rp ON rp.id = r.product_id
 UNION ALL
 SELECT 'concession'::text, cs.id, cs.tenant_id, cs.status, cs.total_cents,
        NULL::uuid, NULL::text, NULL::text, cs.stripe_payment_intent_id,
        ('Concession (' || (SELECT COALESCE(sum(l.quantity), 0) FROM concession_sale_line l WHERE l.sale_id = cs.id)::text) || ' items)',
        cs.created_at, NULL::uuid
   FROM concession_sale cs;

COMMENT ON VIEW v_recent_sales IS
    'Unified read model across all per-kind purchase tables. Used by the admin dashboard, the Admin Purchases list, and any other cross-cutting "all sales" feature. When adding a new purchase kind, append a UNION ALL branch. redemption_token is NULL for kinds without one (membership, gift card, concession).';
