-- Test-data seed for the RidePass Cashier (operator) app, for STAGE or local.
--   psql "<conn-string-to-ridepass_stage>" -f scripts/cashier-stage-seed.sql
--
-- Creates: tenant config tweaks (ID gate + address for Terminal + membership) + an event + two
-- tiers + N paid tickets (the check-in roster). The event is set NOT to require a rider waiver so
-- check-in admits succeed cleanly; the waiver-capture flow is exercised separately via a new sale
-- to an unsigned customer.
--
-- It does NOT create the staff users: their password is an ASP.NET PasswordHasher hash that can't
-- be produced in SQL. Create them once via the web admin Users page:
--     cashier -> role tenant_cashier   (sales.counter + sales.redeem + sales.view)
--     manager -> role tenant_manager   (adds cash.reconcile, refunds, reports)
-- (Or reuse a known password by cloning an existing account's hash -- see the block at the end.)

\set ON_ERROR_STOP on

DO $$
DECLARE
    -- ── EDIT THESE ──────────────────────────────────────────────────────────────────
    v_subdomain   text        := 'loampass-mx';            -- an EXISTING tenant's subdomain
    v_event_title text        := 'Cashier Test Race Day';
    v_starts      timestamptz := now() - interval '1 hour';  -- in-progress => the app auto-selects it
    v_ends        timestamptz := now() + interval '8 hours';
    v_ticket_count int        := 12;                       -- paid riders to put on the roster
    v_require_id  boolean     := true;                     -- exercise the ID-at-check-in gate
    -- ────────────────────────────────────────────────────────────────────────────────
    v_tenant uuid;
    v_etype  uuid;
    v_event  uuid;
    v_race   uuid;
    v_gate   uuid;
    v_i      int;
    v_token  uuid;
    v_first  uuid;
BEGIN
    SELECT id INTO v_tenant FROM tenant WHERE lower(subdomain) = lower(v_subdomain);
    IF v_tenant IS NULL THEN
        RAISE EXCEPTION 'No tenant with subdomain "%" — create it in the admin first.', v_subdomain;
    END IF;

    SELECT id INTO v_etype FROM tenant_event_type WHERE tenant_id = v_tenant LIMIT 1;
    IF v_etype IS NULL THEN
        RAISE EXCEPTION 'Tenant "%" has no event types (these seed on tenant creation).', v_subdomain;
    END IF;

    -- Tenant config the app exercises: ID gate, an address (so Terminal can provision a Location),
    -- and membership enabled (so the cart shows the Membership line). Existing values are kept.
    UPDATE tenant SET
        require_id_at_checkin  = v_require_id,
        address_line           = COALESCE(NULLIF(address_line, ''), '1 Track Rd'),
        city                   = COALESCE(NULLIF(city, ''), 'Austin'),
        region                 = COALESCE(NULLIF(region, ''), 'TX'),
        postal_code            = COALESCE(NULLIF(postal_code, ''), '78701'),
        country                = COALESCE(NULLIF(country, ''), 'US'),
        membership_enabled     = true,
        membership_name        = COALESCE(NULLIF(membership_name, ''), 'Annual Membership'),
        membership_price_cents = CASE WHEN COALESCE(membership_price_cents, 0) > 0 THEN membership_price_cents ELSE 10000 END
    WHERE id = v_tenant;

    -- Event (in-progress; waiver NOT required so check-in admits don't get blocked).
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day,
                       status, allows_riders, allows_spectators, requires_rider_waiver)
    VALUES (v_tenant, v_etype, v_event_title, 'Seeded by cashier-stage-seed.sql', v_starts, v_ends, false,
            'scheduled', true, true, false)
    RETURNING id INTO v_event;

    -- Tiers: a race entry (rider) + a gate fee (spectator).
    INSERT INTO event_ticket_tier (tenant_id, event_id, kind, audience, required, name, price_cents,
                                   inventory, sort_order, is_active, rider_paid_service_charge_bps)
    VALUES (v_tenant, v_event, 'race_entry', 'rider', false, '250 Class', 4000, NULL, 0, true, 0)
    RETURNING id INTO v_race;

    INSERT INTO event_ticket_tier (tenant_id, event_id, kind, audience, required, name, price_cents,
                                   inventory, sort_order, is_active, rider_paid_service_charge_bps)
    VALUES (v_tenant, v_event, 'gate_fee', 'spectator', false, 'Spectator Gate', 1500, NULL, 1, true, 0)
    RETURNING id INTO v_gate;

    -- Paid tickets = the roster. Guest purchasers; every 4th is a spectator. redemption_token
    -- auto-defaults to a fresh GUID (that's what the app scans / the gate QR encodes).
    FOR v_i IN 1..v_ticket_count LOOP
        INSERT INTO event_ticket_purchase (tenant_id, tier_id, amount_cents, service_charge_cents,
                                           payment_method, status, purchaser_email, purchaser_name,
                                           registration_complete)
        VALUES (v_tenant,
                CASE WHEN v_i % 4 = 0 THEN v_gate ELSE v_race END,
                CASE WHEN v_i % 4 = 0 THEN 1500 ELSE 4000 END,
                0, 'stripe', 'paid',
                format('rider%s@test.dev', v_i),
                format('Test Rider %s', v_i),
                true)
        RETURNING redemption_token INTO v_token;
        IF v_i = 1 THEN v_first := v_token; END IF;
    END LOOP;

    RAISE NOTICE '─────────────────────────────────────────────';
    RAISE NOTICE 'Tenant:  %  (%)', v_subdomain, v_tenant;
    RAISE NOTICE 'Event:   "%"  id=%', v_event_title, v_event;
    RAISE NOTICE 'Roster:  % paid tickets ready', v_ticket_count;
    RAISE NOTICE 'ID gate: %', v_require_id;
    RAISE NOTICE 'Scan a QR: open  <api-host>/api/Qr/%  in a browser (it renders the scannable code).', v_first;
    RAISE NOTICE 'More tokens:  SELECT redemption_token, purchaser_name FROM event_ticket_purchase WHERE tenant_id = %;', v_tenant;
    RAISE NOTICE 'Next: create cashier + manager users in the admin, then sign in to the app.';
    RAISE NOTICE '─────────────────────────────────────────────';
END $$;

-- ── OPTIONAL: create staff users by CLONING a known account's password hash ──────────────────
-- If you already have a working login on this DB whose password you know, clone its hash onto new
-- cashier/manager rows so you know their password too. Replace <KNOWN-LOGIN-EMAIL>.
--
-- INSERT INTO users (tenant_id, email, password_hash, first_name, last_name, role, roles, status)
-- SELECT t.id, 'cashier@test.dev', u.password_hash, 'Test', 'Cashier',
--        'tenant_cashier', ARRAY['tenant_cashier'], 'active'
--   FROM tenant t, users u
--  WHERE lower(t.subdomain) = 'loampass-mx' AND u.email = '<KNOWN-LOGIN-EMAIL>';
--
-- INSERT INTO users (tenant_id, email, password_hash, first_name, last_name, role, roles, status)
-- SELECT t.id, 'manager@test.dev', u.password_hash, 'Test', 'Manager',
--        'tenant_manager', ARRAY['tenant_manager'], 'active'
--   FROM tenant t, users u
--  WHERE lower(t.subdomain) = 'loampass-mx' AND u.email = '<KNOWN-LOGIN-EMAIL>';
