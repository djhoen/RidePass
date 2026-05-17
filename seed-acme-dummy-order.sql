-- Local-only dummy order: places a paid race-entry purchase against the seeded
-- "Spring Series Round 3" event so you can test the bundled-coupon + share flow
-- without Stripe sandbox set up. Re-runnable: deletes the prior dummy purchase
-- (matched by stripe_payment_intent_id starting with 'dummy_') before inserting.
--
-- Defaults to djhoen@gmail.com — change the BUYER_EMAIL line below if you want
-- to attach the order to a different user.

\set ON_ERROR_STOP on
BEGIN;

DO $dummy$
DECLARE
    v_buyer_email text := 'djhoen@gmail.com';   -- ← change to whichever user you log in as
    v_tenant_id    uuid;
    v_buyer_id     uuid;
    v_event_id     uuid;
    v_tier_id      uuid;
    v_purchase_id  uuid;
    v_purchase_token uuid;
    v_dummy_pi     text;
    v_now          timestamptz := now();
    v_code         text;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE subdomain = 'acme';
    IF v_tenant_id IS NULL THEN RAISE EXCEPTION 'tenant "acme" not found'; END IF;

    SELECT id INTO v_buyer_id FROM users WHERE lower(email) = lower(v_buyer_email);
    IF v_buyer_id IS NULL THEN
        RAISE EXCEPTION 'user % not found — sign up first or change v_buyer_email at top of script', v_buyer_email;
    END IF;

    -- Pick the first race-entry tier from the seeded "Spring Series Round 3" event.
    SELECT t.id, t.event_id INTO v_tier_id, v_event_id
    FROM event_ticket_tier t
    JOIN event e ON e.id = t.event_id
    WHERE t.tenant_id = v_tenant_id
      AND t.kind = 'race_entry'
      AND e.description LIKE '%[seed]%'
    ORDER BY t.sort_order
    LIMIT 1;
    IF v_tier_id IS NULL THEN
        RAISE EXCEPTION 'no race-entry tier found — re-run seed-acme.sql first';
    END IF;

    -- Configure 4 bundled coupons (20% off spectator tickets, scoped to this race) on
    -- the chosen tier so the dummy purchase has something to mint.
    UPDATE event_ticket_tier
    SET bundled_coupon_count          = 4,
        bundled_coupon_discount_kind  = 'percent',
        bundled_coupon_discount_value = 2000,   -- 2000 bps = 20%
        bundled_coupon_scope          = 'event_ticket',
        bundled_coupon_expires_in_days = NULL
    WHERE id = v_tier_id;

    -- Wipe any prior dummy order from this script so re-runs don't accumulate.
    DELETE FROM coupon
        WHERE issued_to_user_id = v_buyer_id
          AND issued_from_purchase_id IN (
              SELECT id FROM event_ticket_purchase
              WHERE tenant_id = v_tenant_id AND purchaser_user_id = v_buyer_id
                AND stripe_payment_intent_id LIKE 'dummy_%'
          );
    DELETE FROM tenant_ledger_entry
        WHERE tenant_id = v_tenant_id
          AND source_kind = 'event_ticket'
          AND source_id IN (
              SELECT id FROM event_ticket_purchase
              WHERE tenant_id = v_tenant_id AND purchaser_user_id = v_buyer_id
                AND stripe_payment_intent_id LIKE 'dummy_%'
          );
    DELETE FROM event_ticket_purchase
        WHERE tenant_id = v_tenant_id AND purchaser_user_id = v_buyer_id
          AND stripe_payment_intent_id LIKE 'dummy_%';

    -- Insert the paid race-entry purchase.
    v_dummy_pi := 'dummy_' || replace(uuid_generate_v4()::text, '-', '');
    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, purchaser_user_id, stripe_payment_intent_id,
         amount_cents, status, purchaser_email, purchaser_name)
    SELECT v_tenant_id, v_tier_id, v_buyer_id, v_dummy_pi,
           t.price_cents, 'paid',
           u.email, u.first_name || ' ' || u.last_name
    FROM event_ticket_tier t, users u
    WHERE t.id = v_tier_id AND u.id = v_buyer_id
    RETURNING id, redemption_token INTO v_purchase_id, v_purchase_token;

    -- Insert a matching sale ledger row so reports show the order.
    INSERT INTO tenant_ledger_entry
        (tenant_id, entry_kind, source_kind, source_id, occurred_at_utc,
         gross_cents, stripe_fee_cents, ridepass_cut_cents, net_to_tenant_cents,
         stripe_payment_intent_id, payment_method)
    SELECT v_tenant_id, 'sale', 'event_ticket', v_purchase_id, v_now,
           t.price_cents, 0, 0, t.price_cents,
           v_dummy_pi, 'stripe'
    FROM event_ticket_tier t WHERE t.id = v_tier_id;

    -- Mint the 4 bundled coupons (matching what the webhook handler would do).
    -- Codes use a simple readable pattern — the production minter generates
    -- crypto-random ones, but for seed data a predictable pattern is fine.
    FOR i IN 1..4 LOOP
        v_code := 'DUMMY-' || upper(substr(replace(uuid_generate_v4()::text, '-', ''), 1, 8));
        INSERT INTO coupon
            (tenant_id, code, description,
             discount_kind, discount_value, applicable_scope, applicable_event_id,
             valid_from_utc, valid_to_utc, max_total_uses, max_uses_per_user,
             is_active, issued_to_user_id, issued_from_purchase_id)
        VALUES
            (v_tenant_id, v_code, 'From Pro class entry (dummy order)',
             'percent', 2000, 'event_ticket', v_event_id,
             v_now, NULL, 1, 1,
             true, v_buyer_id, v_purchase_id);
    END LOOP;

    RAISE NOTICE 'Dummy paid race-entry order created for % (purchase id %, 4 bundled coupons).',
        v_buyer_email, v_purchase_id;
END $dummy$;

COMMIT;
