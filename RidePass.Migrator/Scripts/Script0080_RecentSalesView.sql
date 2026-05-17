-- Unified read model across the seven per-kind purchase tables. Until now, every
-- cross-cutting feature (dashboard recent-sales, admin "all purchases" list,
-- accounting, refunds) had to UNION ALL these tables by hand. That drifted: the
-- dashboard panel only ever showed day passes, so spectator and ticket purchases
-- were invisible. This view is the single source of truth callers should hit
-- when they want "any kind of sale, sorted by date."
--
-- Each branch normalizes to the same column shape:
--   kind, id, tenant_id, status, amount_cents,
--   purchaser_user_id, purchaser_email, purchaser_name,
--   stripe_payment_intent_id, item_name, created_at
--
-- Where a table's columns differ, the SELECT renames them (membership uses
-- user_id, gift_card uses buyer_*, rental uses rental_pi_id, etc.). Where a
-- displayable item name lives on a parent table (pass_product.name,
-- event_ticket_tier.name, …) we LEFT JOIN; gift cards synthesize a label from
-- the denomination since there's no product row.
--
-- IMPORTANT: when adding a new purchase kind, also add a UNION ALL branch here.
-- A global Claude skill (~/.claude/skills/recent-sales-view/SKILL.md) fires
-- whenever a purchase-shaped table is introduced or its columns shift, so the
-- view stays in lockstep with the schema.

CREATE OR REPLACE VIEW v_recent_sales AS

-- Day passes (one rider, one day, against a pass_product).
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

-- Event tickets: race entries and spectator-pass tiers (different tier.kind).
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

-- Event extras: gate fees, camping, parking, merch (with variants).
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

-- Season passes (multi-event entitlement; unlimited / days-of-week / credits).
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

-- Memberships: uses user_id (not purchaser_user_id) and stores the membership
-- name at purchase time (name_at_purchase). No purchaser_email on the row, so
-- we join users for the buyer's email + display name.
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

-- Gift cards: buyer_* fields instead of purchaser_*; the charged amount is
-- initial_amount_cents (balance_cents drifts as the card is spent). No product
-- row — synthesize a label from the denomination.
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

-- Rentals: rental_pi_id is the charge PI (deposit_pi_id is the separate hold
-- for damages and isn't a "sale"). Item name comes from the rental_product.
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
LEFT JOIN rental_product rp ON rp.id = r.product_id;

COMMENT ON VIEW v_recent_sales IS
    'Unified read model across all per-kind purchase tables. Used by the admin dashboard, the Admin Purchases list, and any other cross-cutting "all sales" feature. When adding a new purchase kind, append a UNION ALL branch.';
