-- Direct charges, phase 2: extend the connected-account snapshot to the remaining standalone
-- online sale tables so they can be charged on a tenant's own Stripe account (see Script0136).
-- event_extra_purchase and membership_purchase already got the column in Script0136 (they can be
-- bundled onto an event-ticket cart); this adds it to the season-pass and rental flows.

ALTER TABLE season_pass_purchase ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;
ALTER TABLE rental_purchase      ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;
