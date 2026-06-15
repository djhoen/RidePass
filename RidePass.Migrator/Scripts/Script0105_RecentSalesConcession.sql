-- Add concession sales to the unified v_recent_sales read model so they show on the
-- admin dashboard's Recent Purchases panel and the Admin -> Purchases list like every
-- other sale kind. Concession sales are anonymous (no buyer identity — staff rings up a
-- walk-up via sold_by_user_id), so the purchaser_* columns are NULL and the item label
-- summarizes the line count. View migrations are append-only: this restates every
-- existing branch from Script0080 and adds the concession branch.

CREATE OR REPLACE VIEW v_recent_sales AS

SELECT 'pass'::text                                  AS kind,
       p.id,
       p.tenant_id,
       p.status,
       p.amount_cents,
       p.purchaser_user_id,
       p.purchaser_email,
       p.purchaser_name,
       p.stripe_payment_intent_id,
       pp.name                                       AS item_name,
       p.created_at
FROM pass_purchase p
LEFT JOIN pass_product pp ON pp.id = p.product_id

UNION ALL

SELECT 'event_ticket'::text,
       t.id,
       t.tenant_id,
       t.status,
       t.amount_cents,
       t.purchaser_user_id,
       t.purchaser_email,
       t.purchaser_name,
       t.stripe_payment_intent_id,
       tt.name                                       AS item_name,
       t.created_at
FROM event_ticket_purchase t
LEFT JOIN event_ticket_tier tt ON tt.id = t.tier_id

UNION ALL

SELECT 'event_extra'::text,
       e.id,
       e.tenant_id,
       e.status,
       e.amount_cents,
       e.purchaser_user_id,
       e.purchaser_email,
       e.purchaser_name,
       e.stripe_payment_intent_id,
       ep.name                                       AS item_name,
       e.created_at
FROM event_extra_purchase e
LEFT JOIN event_extra_product ep ON ep.id = e.product_id

UNION ALL

SELECT 'season_pass'::text,
       s.id,
       s.tenant_id,
       s.status,
       s.amount_cents,
       s.purchaser_user_id,
       s.purchaser_email,
       s.purchaser_name,
       s.stripe_payment_intent_id,
       sp.name                                       AS item_name,
       s.created_at
FROM season_pass_purchase s
LEFT JOIN season_pass_product sp ON sp.id = s.product_id

UNION ALL

SELECT 'membership'::text,
       m.id,
       m.tenant_id,
       m.status,
       m.amount_cents,
       m.user_id                                     AS purchaser_user_id,
       u.email                                       AS purchaser_email,
       TRIM(BOTH FROM (COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, ''))) AS purchaser_name,
       m.stripe_payment_intent_id,
       m.name_at_purchase                            AS item_name,
       m.created_at
FROM membership_purchase m
LEFT JOIN users u ON u.id = m.user_id

UNION ALL

SELECT 'gift_card'::text,
       g.id,
       g.tenant_id,
       g.status,
       g.initial_amount_cents                        AS amount_cents,
       g.buyer_user_id                               AS purchaser_user_id,
       g.buyer_email                                 AS purchaser_email,
       g.buyer_name                                  AS purchaser_name,
       g.stripe_payment_intent_id,
       ('Gift Card $' || (g.initial_amount_cents / 100)::text) AS item_name,
       g.created_at
FROM gift_card g

UNION ALL

SELECT 'rental'::text,
       r.id,
       r.tenant_id,
       r.status,
       r.amount_cents,
       r.purchaser_user_id,
       r.purchaser_email,
       r.purchaser_name,
       r.rental_pi_id                                AS stripe_payment_intent_id,
       rp.name                                       AS item_name,
       r.created_at
FROM rental_purchase r
LEFT JOIN rental_product rp ON rp.id = r.product_id

UNION ALL

-- Concessions: anonymous cashier sales (food/drink/swag). No buyer identity, so the
-- purchaser_* columns are NULL; the label summarizes total item count across the sale.
SELECT 'concession'::text,
       cs.id,
       cs.tenant_id,
       cs.status,
       cs.total_cents                                AS amount_cents,
       NULL::uuid                                    AS purchaser_user_id,
       NULL::text                                    AS purchaser_email,
       NULL::text                                    AS purchaser_name,
       cs.stripe_payment_intent_id,
       ('Concession (' || (SELECT COALESCE(SUM(l.quantity), 0)
                           FROM concession_sale_line l WHERE l.sale_id = cs.id)::text || ' items)') AS item_name,
       cs.created_at
FROM concession_sale cs;

COMMENT ON VIEW v_recent_sales IS
    'Unified read model across all per-kind purchase tables. Used by the admin dashboard, the Admin Purchases list, and any other cross-cutting "all sales" feature. When adding a new purchase kind, append a UNION ALL branch.';
