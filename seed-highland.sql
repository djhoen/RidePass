-- ============================================================================
-- Highland Bike Park demo tenant seed (STAGING ONLY).
-- Built for the Highland Bike Park sales demo: one tenant showing the full
-- consolidation story (ticketing, season passes, rentals, bike shop retail +
-- repairs, F&B/QSR, lessons) with ~3 weeks of history so reports look alive.
--
-- Product names/prices mirror Highland Mountain Bike Park's public lineup
-- (highlandmountain.com, fetched 2026-07-22; re-verified 2026-07-23 incl.
-- lessons, camps, and rentals). Rerunnable: each block wipes its own prior
-- seed rows (scoped to the 'highland' tenant / @highland.test markers) before
-- reinserting. Never touches other tenants.
--
-- The final fragment ($hl_sales_year$) layers a full trailing year of sales
-- history (~$4.0M: 70% tickets/passes/camps, 15% bike shop, 15% F&B) on top
-- of the catalog fragments, following the park's real seasonal calendar.
--
-- Run (from the stage droplet, ridepass_stage DB):
--   psql "$STAGE_DB_URL" -v ON_ERROR_STOP=1 -f seed-highland.sql
--
-- Demo logins (same passwords as the existing QA accounts; hashes copied):
--   demo.admin@highland.test  (tenant_admin)   password = qa.admin's
--   demo.staff@highland.test  (tenant_manager) password = qa.admin's
--   jordan.vance@highland.test + 3 more riders, password = qa.rider's
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ============================================================================
-- Highland Bike Park (subdomain 'highland') -- tenant creation + branding + users
-- Fragment 1 of the Highland demo-tenant seed. Runs on STAGING only.
-- Rerunnable: creates the tenant only if absent, then idempotently re-applies
-- branding/flags/users every run.
-- ============================================================================

DO $hl_tenant$
DECLARE
    v_tenant_id uuid;
BEGIN
    -- Resolve or create the tenant. Subdomain lookups are done case
    -- insensitively since subdomains are user derived text.
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';

    IF v_tenant_id IS NULL THEN
        -- venue_category = 'resort' is the deliberate choice here: the
        -- seed_default_event_types / seed_default_extra_products triggers key
        -- off venue_category for tenant_type = 'mountain_bike' and produce
        -- "Lift Day" / "Lift Ticket" naming for 'resort', which matches the
        -- lift served bike park demo story. Downstream seed sections (passes,
        -- rentals, lessons) should assume venue_category = 'resort'.
        INSERT INTO tenant (
            subdomain, display_name, tenant_type, venue_category, timezone, status,
            concessions_enabled, bike_shop_enabled, season_passes_enabled,
            gift_cards_enabled, wristbands_enabled,
            is_published, client_type
        ) VALUES (
            'highland', 'Highland Bike Park', 'mountain_bike', 'resort', 'America/New_York', 'active',
            true, true, true,
            true, true,
            true, 'hosted'
        )
        RETURNING id INTO v_tenant_id;

        RAISE NOTICE 'Created Highland Bike Park tenant id %', v_tenant_id;
    END IF;

    -- ── Feature flags (idempotent re-apply even if the tenant pre-existed) ──
    UPDATE tenant SET
        concessions_enabled    = true,
        bike_shop_enabled      = true,
        season_passes_enabled  = true,
        gift_cards_enabled     = true,
        wristbands_enabled     = true,
        rentals_enabled        = false,  -- retired flag (Script0200); shop_* rentals ride on bike_shop_enabled
        is_published            = true
    WHERE id = v_tenant_id;

    -- ── Branding / public page content ──────────────────────────────────────
    UPDATE tenant SET
        about_html = '<p>Welcome to <strong>Highland Bike Park</strong>, a lift served mountain bike park cut into the hills above Northfield, New Hampshire. Thirty-plus lift-served trails run from smooth flow lines to rowdy black diamond rock rolls, alongside a dedicated skills and jump zone, pump track, and slopestyle course.</p><p>Day tickets, season passes, bike and pad rentals, and our full service bike shop are all available on site. The Highland Pub in the lodge serves craft pizza, pub fare, and our house-brewed Hellion IPA.</p><p>We are open Wednesday through Sunday in season. Check today''s trail status above before you drive up.</p>',
        hours_json = '{"mon":{"closed":true,"open":"10:00","close":"17:00"},"tue":{"closed":true,"open":"10:00","close":"17:00"},"wed":{"closed":false,"open":"10:00","close":"17:00"},"thu":{"closed":false,"open":"10:00","close":"17:00"},"fri":{"closed":false,"open":"10:00","close":"18:00"},"sat":{"closed":false,"open":"09:00","close":"18:00"},"sun":{"closed":false,"open":"09:00","close":"17:00"}}'::jsonb,
        daily_status_open = true,
        daily_status_message = 'Trails groomed and running dry. Lift is spinning on schedule.',
        daily_status_updated_at = now() - INTERVAL '3 hours',
        contact_email = 'info@highland.test',
        refund_policy_html = '<p>Lift tickets and day passes are refundable up to 24 hours before your riding date. Inside 24 hours, tickets can be transferred to another date at no charge.</p><p>Season passes are prorated and refundable within 30 days of purchase, less a 10% administration fee.</p><p>Lesson and clinic bookings are non-refundable but may be transferred to another rider or rescheduled with 48 hours'' notice.</p><p>Bike and gear rentals: the rental fee is non-refundable once the bike leaves the shop, but unused rental time can be credited toward a future visit.</p>',
        shipping_name = 'Highland Bike Park Office',
        address_line = '75 Ski Hill Drive',
        city = 'Northfield',
        region = 'NH',
        postal_code = '03276',
        country = 'USA',
        latitude = 43.446,
        longitude = -71.629
    WHERE id = v_tenant_id;

    -- ── Theme touch (tenant_branding row already exists via trg_tenant_insert_branding) ──
    UPDATE tenant_branding SET
        tagline = 'Lift served riding in the foothills of the White Mountains',
        primary_color = '#2E7D32',
        secondary_color = '#33691E',
        accent_color = '#8BC34A'
    WHERE tenant_id = v_tenant_id;

    -- ── Safety net on trigger seeded names ───────────────────────────────────
    -- seed_default_event_types / seed_default_extra_products already produce
    -- Highland appropriate names for tenant_type = 'mountain_bike' +
    -- venue_category = 'resort' ("Lift Day" event type, "Lift Ticket" extra
    -- product), so no duplicate rows are inserted here. These UPDATEs just
    -- guard against a prior manual/test row existing with a different
    -- venue_category on file (e.g. from an earlier partial run).
    UPDATE tenant_event_type SET name = 'Lift Day', color = '#1976D2'
        WHERE tenant_id = v_tenant_id AND code = 'open_ride' AND is_system;
    UPDATE event_extra_product SET name = 'Lift Ticket', kind = 'lift', price_cents = 5000
        WHERE tenant_id = v_tenant_id AND sort_order = 5 AND kind IN ('lift', 'day_pass', 'shuttle');

    RAISE NOTICE 'Highland Bike Park tenant ready, id %', v_tenant_id;
END $hl_tenant$;


-- ============================================================================
-- Highland Bike Park -- users (tenant staff + global riders)
-- ============================================================================
DO $hl_users$
DECLARE
    v_tenant_id uuid;
    v_admin_hash text;
    v_rider_hash text;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found -- run the tenant creation block first';
    END IF;

    -- Known-password hashes copied from existing QA accounts so the demo
    -- logins work without touching Stripe or any password-reset flow.
    SELECT password_hash INTO v_admin_hash FROM users WHERE lower(email) = lower('qa.admin@ridepass-qa.test');
    SELECT password_hash INTO v_rider_hash FROM users WHERE lower(email) = lower('qa.rider@ridepass-qa.test');

    IF v_admin_hash IS NULL THEN
        RAISE EXCEPTION 'reference user qa.admin@ridepass-qa.test not found -- cannot copy password hash';
    END IF;
    IF v_rider_hash IS NULL THEN
        RAISE EXCEPTION 'reference user qa.rider@ridepass-qa.test not found -- cannot copy password hash';
    END IF;

    -- ── Wipe prior seed rows (rerunnable) ────────────────────────────────────
    -- Purchase rows from a prior run reference these riders via ON DELETE RESTRICT
    -- FKs, so they must go first (the ticketing block re-seeds its own domain later).
    DELETE FROM season_pass_reservation WHERE season_pass_purchase_id IN (
        SELECT id FROM season_pass_purchase WHERE tenant_id = v_tenant_id AND lower(purchaser_email) LIKE '%@highland.test');
    DELETE FROM event_ticket_purchase WHERE tenant_id = v_tenant_id AND lower(purchaser_email) LIKE '%@highland.test';
    DELETE FROM season_pass_purchase  WHERE tenant_id = v_tenant_id AND lower(purchaser_email) LIKE '%@highland.test';
    DELETE FROM users WHERE tenant_id = v_tenant_id AND lower(email) LIKE '%@highland.test';
    DELETE FROM users WHERE tenant_id IS NULL AND lower(email) LIKE '%@highland.test';

    -- ── Tenant staff ──────────────────────────────────────────────────────────
    INSERT INTO users (
        tenant_id, email, password_hash, first_name, last_name, role, roles,
        status, email_verified
    ) VALUES
        (v_tenant_id, 'demo.admin@highland.test', v_admin_hash, 'Demo', 'Admin',
            'tenant_admin', ARRAY['tenant_admin'], 'active', true),
        (v_tenant_id, 'demo.staff@highland.test', v_admin_hash, 'Demo', 'Staff',
            'tenant_manager', ARRAY['tenant_cashier', 'tenant_manager'], 'active', true);

    -- ── Global riders (tenant_id NULL) ───────────────────────────────────────
    INSERT INTO users (
        email, password_hash, first_name, last_name, role, roles, status, email_verified,
        birthdate, emergency_contact_name, emergency_contact_phone
    ) VALUES
        ('jordan.vance@highland.test',    v_rider_hash, 'Jordan',    'Vance',     'rider', ARRAY['rider'], 'active', true, '1990-05-12', 'Alex Vance',     '603-555-0111'),
        ('mackenzie.reyes@highland.test', v_rider_hash, 'Mackenzie', 'Reyes',     'rider', ARRAY['rider'], 'active', true, '1995-11-03', 'Sam Reyes',      '603-555-0112'),
        ('devin.kowalski@highland.test',  v_rider_hash, 'Devin',     'Kowalski',  'rider', ARRAY['rider'], 'active', true, '1987-02-27', 'Robin Kowalski', '603-555-0113'),
        ('sierra.whitfield@highland.test',v_rider_hash, 'Sierra',    'Whitfield', 'rider', ARRAY['rider'], 'active', true, '2001-08-19', 'Pat Whitfield',  '603-555-0114');

    RAISE NOTICE 'Seeded Highland Bike Park users under tenant %', v_tenant_id;
END $hl_users$;


-- Highland Bike Park (subdomain 'highland') - ticketing, season passes, events,
-- and purchase history. Schema note: the day-pass subsystem (pass_product /
-- pass_purchase / event_pass_eligibility) that the older 'acme' seed used was
-- hard-dropped in Script0118_RemoveDayPass.sql. Day/gate tickets now live as
-- event_ticket_tier rows (kind='gate_fee') hung off an actual event - so every
-- "day ticket" below is a gate_fee tier on an Open Ride day-event, and the old
-- "eligibility" concept is now season_pass_benefit + the legacy
-- season_pass_event_type_perk (per season-pass-product x event-TYPE, not per
-- event). Both are seeded here since the deployed app still reads the legacy
-- perk table (Script0178 comment) pending its removal.
DO $hl_tix$
DECLARE
    v_tenant_id   uuid;
    v_open_ride   uuid;
    v_race        uuid;
    v_practice    uuid;
    v_lesson      uuid;

    v_sp_unlim    uuid;
    v_sp_wkdy     uuid;
    v_sp_wed      uuid;
    v_sp_3ride    uuid;
    v_sp_purchase_unlim uuid;
    v_sp_purchase_wkdy  uuid;

    v_rider_count int;

    v_ny_midnight    timestamp; -- midnight today, America/New_York, naive
    v_days_to_sat    int;
    v_days_to_wed    int;
    v_days_to_sun    int;

    v_evt_p1 uuid; -- past Saturday Open Ride, ~3 weeks ago
    v_evt_p2 uuid; -- past midweek Open Ride, ~2 weeks ago
    v_evt_p3 uuid; -- past Saturday Open Ride, ~1 week ago
    v_evt_open_future uuid; -- upcoming weekend Open Ride
    v_evt_race        uuid; -- Dual Slalom Race
    v_evt_fri1 uuid;
    v_evt_fri2 uuid;
    v_evt_fri3 uuid;
    v_evt_clinic uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found - create it first';
    END IF;

    SELECT id INTO v_open_ride FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'open_ride';
    SELECT id INTO v_race      FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'race';
    SELECT id INTO v_practice  FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'practice';
    SELECT id INTO v_lesson    FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'lesson';
    IF v_open_ride IS NULL OR v_race IS NULL OR v_practice IS NULL OR v_lesson IS NULL THEN
        RAISE EXCEPTION 'standard tenant_event_type rows missing for tenant % - the tenant-creation trigger should seed these', v_tenant_id;
    END IF;

    -- ── Wipe prior seed rows (child tables first; event/product deletes cascade
    --    their tiers and benefit/perk rows automatically - see FK check above) ──
    DELETE FROM season_pass_reservation
     WHERE event_id IN (SELECT id FROM event WHERE tenant_id = v_tenant_id)
        OR season_pass_purchase_id IN (SELECT id FROM season_pass_purchase WHERE tenant_id = v_tenant_id AND purchaser_email LIKE '%@highland.test');

    DELETE FROM event_ticket_purchase
     WHERE tenant_id = v_tenant_id
       AND (purchaser_email LIKE '%@highland.test'
            OR tier_id IN (SELECT id FROM event_ticket_tier
                            WHERE event_id IN (SELECT id FROM event WHERE tenant_id = v_tenant_id)));

    DELETE FROM season_pass_purchase
     WHERE tenant_id = v_tenant_id AND purchaser_email LIKE '%@highland.test';

    DELETE FROM event
     WHERE tenant_id = v_tenant_id; -- all events on this tenant are seed-owned; cascades event_ticket_tier

    DELETE FROM season_pass_product
     WHERE tenant_id = v_tenant_id; -- cascades season_pass_benefit + season_pass_event_type_perk

    -- ── Riders (created by another seed section) ───────────────────────────
    SELECT count(*) INTO v_rider_count FROM users WHERE role = 'rider' AND email LIKE '%@highland.test';
    IF v_rider_count = 0 THEN
        RAISE NOTICE 'No @highland.test riders found yet - products/events will still seed, but purchase history is skipped this run.';
    END IF;

    -- ── Day-of-week math so events land on the right weekday relative to
    --    whenever this script runs, in the tenant's own timezone ────────────
    v_ny_midnight := date_trunc('day', now() AT TIME ZONE 'America/New_York');
    v_days_to_sat := ((6 - EXTRACT(DOW FROM v_ny_midnight)::int + 7) % 7);
    v_days_to_wed := ((3 - EXTRACT(DOW FROM v_ny_midnight)::int + 7) % 7);
    v_days_to_sun := ((0 - EXTRACT(DOW FROM v_ny_midnight)::int + 7) % 7);

    -- ── Season pass products ────────────────────────────────────────────────
    INSERT INTO season_pass_product
        (tenant_id, name, description, price_cents, valid_from_date, valid_to_date, kind, valid_days_of_week, total_credits)
        VALUES
            (v_tenant_id, 'Adult All-Access Season Pass',
                'Every open day, all season: lift, trails, and skills zones. Ages 18+.',
                66900, CURRENT_DATE, (CURRENT_DATE + INTERVAL '9 months')::date, 'unlimited', NULL, NULL),
            (v_tenant_id, 'Teen All-Access Season Pass',
                'Every open day, all season, ages 13-17.',
                51900, CURRENT_DATE, (CURRENT_DATE + INTERVAL '9 months')::date, 'unlimited', NULL, NULL),
            (v_tenant_id, 'Wednesduro Season Pass',
                'Wednesday evening uphill + e-bike access nights, all season, all ages.',
                15900, CURRENT_DATE, (CURRENT_DATE + INTERVAL '9 months')::date, 'days_of_week', ARRAY[3], NULL),
            -- Mirrors Highland's real "3 Ride Pass" (highlandmountain.com/3ridepass/): three
            -- lift days, one rider, same season, no blackout dates. Price is NOT published on
            -- their page ($219 ~= 11% off 3x the $82 weekend full-day); adjust if known.
            (v_tenant_id, '3 Ride Pass',
                'Three lift-served ride days, any open day this season. One rider, no blackout dates.',
                21900, CURRENT_DATE, (CURRENT_DATE + INTERVAL '9 months')::date, 'credits', NULL, 3);
    SELECT id INTO v_sp_unlim FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Adult All-Access Season Pass';
    SELECT id INTO v_sp_wkdy  FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Teen All-Access Season Pass';
    SELECT id INTO v_sp_wed   FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Wednesduro Season Pass';
    SELECT id INTO v_sp_3ride FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = '3 Ride Pass';

    -- Published landing page for the 3 Ride Pass (mirrors the shape of the park's real
    -- /3ridepass/ page: progression narrative + no-blackout bullets + buy CTA). The columns
    -- are Script0228; guard so the seed still runs against a pre-0228 stage database.
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'season_pass_product' AND column_name = 'landing_published') THEN
        UPDATE season_pass_product SET
            slug = '3-ride-pass',
            landing_published = true,
            landing_html =
                '<h2>Three days. One season. Zero pressure.</h2>'
             || '<p>One day at Highland gets you hooked. Three days is where it clicks: day one you '
             || 'explore the mountain, day two you start linking sections, and by day three you''re '
             || 'riding trails top to bottom with real flow.</p>'
             || '<ul>'
             || '<li><strong>Three full lift-served ride days</strong> to use any open day this season</li>'
             || '<li><strong>No blackout dates</strong> - weekends, holidays, race weekends, all fair game</li>'
             || '<li><strong>No advance booking</strong> - show your pass at the gate whenever you''re ready</li>'
             || '<li><strong>One rider, all season</strong> - your pass, your progression</li>'
             || '</ul>'
             || '<p>Cheaper than three day tickets, with none of the commitment of a full season pass. '
             || 'When the forecast looks perfect, just come ride.</p>'
        WHERE id = v_sp_3ride;
    END IF;

    -- Season pass benefits - current model (read at pricing/checkout time).
    -- Unlimited: free at Open Ride + Friday practice sessions, 20% off race entry.
    -- Midweek: free at Open Ride + Friday practice (the pass's own valid_days_of_week
    -- restriction is what keeps it from being used on a weekend Open Ride day).
    INSERT INTO season_pass_benefit (tenant_id, pass_product_id, benefit_type, scope_id, discount_kind, discount_value)
        VALUES
            (v_tenant_id, v_sp_unlim, 'event', v_open_ride, 'percent', 10000),
            (v_tenant_id, v_sp_unlim, 'event', v_practice,  'percent', 10000),
            (v_tenant_id, v_sp_unlim, 'event', v_race,      'percent', 2000),
            (v_tenant_id, v_sp_wkdy,  'event', v_open_ride, 'percent', 10000),
            (v_tenant_id, v_sp_wkdy,  'event', v_practice,  'percent', 10000),
            (v_tenant_id, v_sp_wed,   'event', v_practice,  'percent', 10000),
            -- 3 Ride Pass: each credit covers one Open Ride day in full (the checkout burn
            -- only honors 100% event benefits on credits products).
            (v_tenant_id, v_sp_3ride, 'event', v_open_ride, 'percent', 10000);

    -- Legacy mirror - deployed app still reads this table per Script0178.
    INSERT INTO season_pass_event_type_perk (pass_product_id, event_type_id, discount_percent)
        VALUES
            (v_sp_unlim, v_open_ride, 100),
            (v_sp_unlim, v_practice,  100),
            (v_sp_unlim, v_race,      20),
            (v_sp_wkdy,  v_open_ride, 100),
            (v_sp_wkdy,  v_practice,  100),
            (v_sp_wed,   v_practice,  100),
            (v_sp_3ride, v_open_ride, 100);

    -- ── Past Open Ride days (history for reports) ──────────────────────────
    -- P1: Saturday, ~3 weeks ago
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (v_tenant_id, v_open_ride, 'Saturday Open Riding',
                'Lift-served trails open, all skill levels.',
                (v_ny_midnight + (v_days_to_sat - 21) * INTERVAL '1 day' + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + (v_days_to_sat - 21) * INTERVAL '1 day' + TIME '17:00') AT TIME ZONE 'America/New_York',
                false, 300, 'Lift Base Area', 'scheduled')
        RETURNING id INTO v_evt_p1;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt_p1, 'Full Day Lift Ticket', 8200, 250, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_p1, 'Happy Hour Ticket (2pm-Close)', 5500, 150, 20, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_p1, 'Junior Lift Ticket (7-14)',   4100, 100, 30, 'gate_fee', 'rider');

    -- P2: midweek, ~2 weeks ago (so a pass holder has a realistic midweek day to redeem on)
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (v_tenant_id, v_open_ride, 'Midweek Open Riding',
                'Lift-served trails open, all skill levels.',
                (v_ny_midnight + (v_days_to_wed - 14) * INTERVAL '1 day' + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + (v_days_to_wed - 14) * INTERVAL '1 day' + TIME '17:00') AT TIME ZONE 'America/New_York',
                false, 300, 'Lift Base Area', 'scheduled')
        RETURNING id INTO v_evt_p2;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt_p2, 'Full Day Lift Ticket', 6800, 250, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_p2, 'Happy Hour Ticket (2pm-Close)', 4500, 150, 20, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_p2, 'Junior Lift Ticket (7-14)',   3400, 100, 30, 'gate_fee', 'rider');

    -- P3: Saturday, ~1 week ago
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (v_tenant_id, v_open_ride, 'Saturday Open Riding',
                'Lift-served trails open, all skill levels.',
                (v_ny_midnight + (v_days_to_sat - 7) * INTERVAL '1 day' + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + (v_days_to_sat - 7) * INTERVAL '1 day' + TIME '17:00') AT TIME ZONE 'America/New_York',
                false, 300, 'Lift Base Area', 'scheduled')
        RETURNING id INTO v_evt_p3;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt_p3, 'Full Day Lift Ticket', 8200, 250, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_p3, 'Happy Hour Ticket (2pm-Close)', 5500, 150, 20, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_p3, 'Junior Lift Ticket (7-14)',   4100, 100, 30, 'gate_fee', 'rider');

    -- ── Upcoming events (next ~3 weeks) ────────────────────────────────────
    -- Weekend Open Riding day
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity,
                        location_label, status, allows_spectators)
        VALUES (v_tenant_id, v_open_ride, 'Saturday Open Riding',
                'Lift-served trails open, all skill levels. Full-day, half-day, and junior tickets at the gate.',
                (v_ny_midnight + v_days_to_sat * INTERVAL '1 day' + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + v_days_to_sat * INTERVAL '1 day' + TIME '17:00') AT TIME ZONE 'America/New_York',
                false, 300, 'Lift Base Area', 'scheduled', true)
        RETURNING id INTO v_evt_open_future;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt_open_future, 'Full Day Lift Ticket',    8200, 250, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_open_future, 'Happy Hour Ticket (2pm-Close)',    5500, 150, 20, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_open_future, 'Junior Lift Ticket (7-14)',      4100, 100, 30, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_open_future, 'Non-Riding Spectator Gate', 1000, 100, 40, 'gate_fee', 'spectator');

    -- Dual Slalom Race, ~2 weeks out - 3 race-class tiers + a spectator gate
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity,
                        location_label, status, allows_spectators)
        VALUES (v_tenant_id, v_race, 'Dual Slalom Race',
                'Head-to-head dual slalom racing, all classes. Practice 8-9am, racing 9am-4pm.',
                (v_ny_midnight + 14 * INTERVAL '1 day' + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + 14 * INTERVAL '1 day' + TIME '16:00') AT TIME ZONE 'America/New_York',
                false, 150, 'Dual Slalom Course', 'scheduled', true)
        RETURNING id INTO v_evt_race;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt_race, 'Pro Class Entry',        7500, 30, 10, 'race_entry', 'rider'),
            (v_tenant_id, v_evt_race, 'Am Class Entry',         5500, 60, 20, 'race_entry', 'rider'),
            (v_tenant_id, v_evt_race, 'Junior Class Entry',     3500, 30, 30, 'race_entry', 'rider'),
            (v_tenant_id, v_evt_race, 'Race Day Spectator Gate',1000, 200, 40, 'gate_fee',   'spectator');

    -- Friday Night Sessions, recurring (3 of them)
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day,
                        location_label, status)
        VALUES (v_tenant_id, v_practice, 'Wednesduro Uphill Night',
                'Evening uphill + e-bike access night. Lit pump track.',
                (v_ny_midnight + v_days_to_wed * INTERVAL '1 day' + TIME '17:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + v_days_to_wed * INTERVAL '1 day' + TIME '20:00') AT TIME ZONE 'America/New_York',
                false, 'Lift Base Area / Pump Track', 'scheduled')
        RETURNING id INTO v_evt_fri1;
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day,
                        location_label, status)
        VALUES (v_tenant_id, v_practice, 'Wednesduro Uphill Night',
                'Evening uphill + e-bike access night. Lit pump track.',
                (v_ny_midnight + (v_days_to_wed + 7) * INTERVAL '1 day' + TIME '17:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + (v_days_to_wed + 7) * INTERVAL '1 day' + TIME '20:00') AT TIME ZONE 'America/New_York',
                false, 'Lift Base Area / Pump Track', 'scheduled')
        RETURNING id INTO v_evt_fri2;
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day,
                        location_label, status)
        VALUES (v_tenant_id, v_practice, 'Wednesduro Uphill Night',
                'Evening uphill + e-bike access night. Lit pump track.',
                (v_ny_midnight + (v_days_to_wed + 14) * INTERVAL '1 day' + TIME '17:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + (v_days_to_wed + 14) * INTERVAL '1 day' + TIME '20:00') AT TIME ZONE 'America/New_York',
                false, 'Lift Base Area / Pump Track', 'scheduled')
        RETURNING id INTO v_evt_fri3;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt_fri1, 'Wednesduro Evening Pass', 2000, 150, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_fri2, 'Wednesduro Evening Pass', 2000, 150, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_fri3, 'Wednesduro Evening Pass', 2000, 150, 10, 'gate_fee', 'rider');

    -- Women's Downhill Clinic - small-group lesson event
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity,
                        location_label, status)
        VALUES (v_tenant_id, v_lesson, 'Women''s Gravity Clinic',
                'Small-group downhill coaching for women riders, all levels. Demo bikes available.',
                (v_ny_midnight + (v_days_to_sun + 7) * INTERVAL '1 day' + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_ny_midnight + (v_days_to_sun + 7) * INTERVAL '1 day' + TIME '12:00') AT TIME ZONE 'America/New_York',
                false, 10, 'Skills Zone / Downhill Trail 2', 'scheduled')
        RETURNING id INTO v_evt_clinic;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES (v_tenant_id, v_evt_clinic, 'Clinic Spot', 8900, 10, 10, 'gate_fee', 'rider');

    -- ── Purchase history (only once riders exist) ──────────────────────────
    IF v_rider_count > 0 THEN

        -- Season pass purchases (2)
        INSERT INTO season_pass_purchase
            (tenant_id, purchaser_user_id, product_id, amount_cents, payment_method, status,
             purchaser_email, purchaser_name, holder_first_name, holder_last_name,
             valid_from_date, valid_to_date, created_at)
            SELECT v_tenant_id, u.id, v_sp_unlim, 66900, 'stripe', 'paid',
                   u.email, trim(both from (u.first_name || ' ' || u.last_name)), u.first_name, u.last_name,
                   CURRENT_DATE - 20, (CURRENT_DATE - 20 + INTERVAL '9 months')::date, now() - INTERVAL '20 days'
            FROM users u WHERE u.role = 'rider' AND u.email LIKE '%@highland.test'
            ORDER BY u.email OFFSET (0 % v_rider_count) LIMIT 1
            RETURNING id INTO v_sp_purchase_unlim;

        INSERT INTO season_pass_purchase
            (tenant_id, purchaser_user_id, product_id, amount_cents, payment_method, status,
             purchaser_email, purchaser_name, holder_first_name, holder_last_name,
             valid_from_date, valid_to_date, created_at)
            SELECT v_tenant_id, u.id, v_sp_wkdy, 51900, 'stripe', 'paid',
                   u.email, trim(both from (u.first_name || ' ' || u.last_name)), u.first_name, u.last_name,
                   CURRENT_DATE - 12, (CURRENT_DATE - 12 + INTERVAL '9 months')::date, now() - INTERVAL '12 days'
            FROM users u WHERE u.role = 'rider' AND u.email LIKE '%@highland.test'
            ORDER BY u.email OFFSET (1 % v_rider_count) LIMIT 1
            RETURNING id INTO v_sp_purchase_wkdy;

        -- Season pass reservations - shows pass holders redeeming on past Open Ride days
        INSERT INTO season_pass_reservation (season_pass_purchase_id, event_id, status, reserved_at, checked_in_at)
            VALUES
                (v_sp_purchase_unlim, v_evt_p1, 'checked_in', now() - INTERVAL '20 days', (v_ny_midnight + (v_days_to_sat - 21) * INTERVAL '1 day' + TIME '09:20') AT TIME ZONE 'America/New_York'),
                (v_sp_purchase_unlim, v_evt_p3, 'checked_in', now() - INTERVAL '7 days',  (v_ny_midnight + (v_days_to_sat - 7)  * INTERVAL '1 day' + TIME '09:15') AT TIME ZONE 'America/New_York'),
                (v_sp_purchase_wkdy,  v_evt_p2, 'checked_in', now() - INTERVAL '12 days', (v_ny_midnight + (v_days_to_wed - 14) * INTERVAL '1 day' + TIME '10:05') AT TIME ZONE 'America/New_York');

        -- Historical gate-ticket purchases across the 3 past Open Ride days -
        -- walk-up cash sales at the gate, ~3 weeks of history for reports.
        INSERT INTO event_ticket_purchase
            (tenant_id, tier_id, purchaser_user_id, amount_cents, status, purchaser_email, purchaser_name, payment_method, created_at)
            SELECT v_tenant_id, spec.tier_id, u.id, spec.amount_cents, 'paid', u.email,
                   trim(both from (u.first_name || ' ' || u.last_name)), 'cash', spec.created_at
            FROM (VALUES
                (0, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p1 AND name = 'Full Day Lift Ticket'), 8200,
                     (v_ny_midnight + (v_days_to_sat - 21) * INTERVAL '1 day' + TIME '08:40') AT TIME ZONE 'America/New_York'),
                (1, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p1 AND name = 'Full Day Lift Ticket'), 8200,
                     (v_ny_midnight + (v_days_to_sat - 21) * INTERVAL '1 day' + TIME '08:55') AT TIME ZONE 'America/New_York'),
                (2, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p1 AND name = 'Junior Lift Ticket (7-14)'), 4100,
                     (v_ny_midnight + (v_days_to_sat - 21) * INTERVAL '1 day' + TIME '09:05') AT TIME ZONE 'America/New_York'),
                (3, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p2 AND name = 'Full Day Lift Ticket'), 6800,
                     (v_ny_midnight + (v_days_to_wed - 14) * INTERVAL '1 day' + TIME '08:45') AT TIME ZONE 'America/New_York'),
                (0, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p2 AND name = 'Happy Hour Ticket (2pm-Close)'), 4500,
                     (v_ny_midnight + (v_days_to_wed - 14) * INTERVAL '1 day' + TIME '12:10') AT TIME ZONE 'America/New_York'),
                (1, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p2 AND name = 'Junior Lift Ticket (7-14)'), 3400,
                     (v_ny_midnight + (v_days_to_wed - 14) * INTERVAL '1 day' + TIME '09:00') AT TIME ZONE 'America/New_York'),
                (2, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p3 AND name = 'Full Day Lift Ticket'), 8200,
                     (v_ny_midnight + (v_days_to_sat - 7) * INTERVAL '1 day' + TIME '08:50') AT TIME ZONE 'America/New_York'),
                (3, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_p3 AND name = 'Happy Hour Ticket (2pm-Close)'), 5500,
                     (v_ny_midnight + (v_days_to_sat - 7) * INTERVAL '1 day' + TIME '12:30') AT TIME ZONE 'America/New_York')
            ) AS spec(k, tier_id, amount_cents, created_at)
            CROSS JOIN LATERAL (
                SELECT id, email, first_name, last_name FROM users
                WHERE role = 'rider' AND email LIKE '%@highland.test'
                ORDER BY email OFFSET (spec.k % v_rider_count) LIMIT 1
            ) u;

        -- Advance race-entry sales for the Dual Slalom Race (online, so 'stripe' with no
        -- fabricated PaymentIntent - see contract note on not reconciling with Stripe).
        INSERT INTO event_ticket_purchase
            (tenant_id, tier_id, purchaser_user_id, amount_cents, status, purchaser_email, purchaser_name, payment_method, created_at)
            SELECT v_tenant_id, spec.tier_id, u.id, spec.amount_cents, 'paid', u.email,
                   trim(both from (u.first_name || ' ' || u.last_name)), 'stripe', spec.created_at
            FROM (VALUES
                (2, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_race AND name = 'Pro Class Entry'), 7500, now() - INTERVAL '3 days'),
                (3, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_race AND name = 'Am Class Entry'),  5500, now() - INTERVAL '2 days'),
                (0, (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_race AND name = 'Junior Class Entry'), 3500, now() - INTERVAL '1 days')
            ) AS spec(k, tier_id, amount_cents, created_at)
            CROSS JOIN LATERAL (
                SELECT id, email, first_name, last_name FROM users
                WHERE role = 'rider' AND email LIKE '%@highland.test'
                ORDER BY email OFFSET (spec.k % v_rider_count) LIMIT 1
            ) u;

    END IF;

    RAISE NOTICE 'Highland ticketing/season-pass/events seed complete for tenant % (% riders found)', v_tenant_id, v_rider_count;
END
$hl_tix$;


-- ============================================================================
-- Highland Bike Park: Concessions / QSR seed fragment
-- Rerunnable: wipes and reseeds every concession_* row for this tenant only.
-- Schema verified against ridepass-stage via information_schema / pg_catalog
-- (Script0140_ConcessionQsr + Script0144_ConcessionMenuBoard equivalent).
-- ============================================================================

DO $hl_concessions$
DECLARE
    v_tenant_id uuid;

    v_cat_grill   uuid;
    v_cat_snacks  uuid;
    v_cat_drinks  uuid;
    v_cat_coffee  uuid;

    v_p_burger    uuid;
    v_p_pizza     uuid;
    v_p_pizza2    uuid;
    v_p_ipa       uuid;
    v_p_tenders   uuid;
    v_p_veggie    uuid;
    v_p_fries     uuid;
    v_p_pretzel   uuid;
    v_p_energybar uuid;
    v_p_trailmix  uuid;
    v_p_fountain  uuid;
    v_p_water     uuid;
    v_p_sports    uuid;
    v_p_soda      uuid;
    v_p_coffee    uuid;
    v_p_coldbrew  uuid;
    v_p_hotchoc   uuid;

    v_mg_toppings uuid;
    v_mg_size     uuid;

    v_opt_cheese  uuid;
    v_opt_bacon   uuid;
    v_opt_avocado uuid;
    v_opt_noonion uuid;
    v_opt_small   uuid;
    v_opt_medium  uuid;
    v_opt_large   uuid;

    v_sale_id  uuid;
    v_line_id  uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found, create it first';
    END IF;

    -- Belt-and-suspenders: make sure the feature is on regardless of fragment order.
    UPDATE tenant SET concessions_enabled = true WHERE id = v_tenant_id;

    -- ── Wipe prior seed data for this tenant (FK-safe order) ───────────────────
    -- concession_sale cascades to concession_sale_line, which cascades to
    -- concession_sale_line_modifier. Must go before concession_product because
    -- concession_sale_line.product_id is ON DELETE RESTRICT.
    DELETE FROM concession_sale WHERE tenant_id = v_tenant_id;
    DELETE FROM concession_product_modifier_group
        WHERE product_id IN (SELECT id FROM concession_product WHERE tenant_id = v_tenant_id);
    DELETE FROM concession_product WHERE tenant_id = v_tenant_id;
    DELETE FROM concession_modifier_option
        WHERE group_id IN (SELECT id FROM concession_modifier_group WHERE tenant_id = v_tenant_id);
    DELETE FROM concession_modifier_group WHERE tenant_id = v_tenant_id;
    DELETE FROM concession_category WHERE tenant_id = v_tenant_id;
    DELETE FROM concession_order_counter WHERE tenant_id = v_tenant_id;

    -- ── Menu categories ─────────────────────────────────────────────────────
    INSERT INTO concession_category (tenant_id, name, sort_order, is_active)
        VALUES (v_tenant_id, 'Pub Kitchen', 10, true) RETURNING id INTO v_cat_grill;
    INSERT INTO concession_category (tenant_id, name, sort_order, is_active)
        VALUES (v_tenant_id, 'Snacks', 20, true) RETURNING id INTO v_cat_snacks;
    INSERT INTO concession_category (tenant_id, name, sort_order, is_active)
        VALUES (v_tenant_id, 'Drinks', 30, true) RETURNING id INTO v_cat_drinks;
    INSERT INTO concession_category (tenant_id, name, sort_order, is_active)
        VALUES (v_tenant_id, 'Coffee', 40, true) RETURNING id INTO v_cat_coffee;

    -- ── Menu items ──────────────────────────────────────────────────────────
    -- Grill
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active, combo_available)
        VALUES (v_tenant_id, 'Smash Burger', 'Double-smashed beef patty, lettuce, tomato, house sauce', 'food', v_cat_grill, 1200, 10, true, true)
        RETURNING id INTO v_p_burger;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active, combo_available)
        VALUES (v_tenant_id, 'Chicken Tenders', 'Hand-breaded, choice of dipping sauce', 'food', v_cat_grill, 1000, 20, true, true)
        RETURNING id INTO v_p_tenders;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active, combo_available)
        VALUES (v_tenant_id, 'Vegan Burger', 'Plant-based patty, lettuce, tomato, house sauce', 'food', v_cat_grill, 1100, 30, true, true)
        RETURNING id INTO v_p_veggie;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Craft Cheese Pizza', 'Wood-fired 12in, house red sauce, basil', 'food', v_cat_grill, 1600, 40, true)
        RETURNING id INTO v_p_pizza;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Pepperoni Pizza', 'Wood-fired 12in, cup-and-char pepperoni', 'food', v_cat_grill, 1800, 50, true)
        RETURNING id INTO v_p_pizza2;

    -- Snacks
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Fries', 'Crispy fries, salted', 'food', v_cat_snacks, 500, 10, true)
        RETURNING id INTO v_p_fries;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Pretzel', 'Warm soft pretzel with cheese sauce', 'food', v_cat_snacks, 600, 20, true)
        RETURNING id INTO v_p_pretzel;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Energy Bar', 'Grab-and-go trail fuel', 'food', v_cat_snacks, 350, 30, true)
        RETURNING id INTO v_p_energybar;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Trail Mix', 'Nuts, dried fruit, chocolate', 'food', v_cat_snacks, 400, 40, true)
        RETURNING id INTO v_p_trailmix;

    -- Drinks (Fountain Drink carries the Size modifier group)
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Fountain Drink', 'Coke products, free refills at the counter', 'drink', v_cat_drinks, 350, 10, true)
        RETURNING id INTO v_p_fountain;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Bottled Water', 'Spring water, 16.9oz', 'drink', v_cat_drinks, 250, 20, true)
        RETURNING id INTO v_p_water;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Sports Drink', 'Electrolyte replacement', 'drink', v_cat_drinks, 400, 30, true)
        RETURNING id INTO v_p_sports;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Canned Soda', 'Assorted cans', 'drink', v_cat_drinks, 300, 40, true)
        RETURNING id INTO v_p_soda;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Hellion IPA (16oz)', 'House-brewed hazy IPA. 21+ with valid ID.', 'drink', v_cat_drinks, 800, 50, true)
        RETURNING id INTO v_p_ipa;

    -- Coffee (Coffee also carries the Size modifier group)
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Coffee', 'Fresh-brewed drip coffee', 'drink', v_cat_coffee, 400, 10, true)
        RETURNING id INTO v_p_coffee;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Cold Brew', 'Slow-steeped, served over ice', 'drink', v_cat_coffee, 500, 20, true)
        RETURNING id INTO v_p_coldbrew;
    INSERT INTO concession_product (tenant_id, name, description, category, category_id, price_cents, sort_order, is_active)
        VALUES (v_tenant_id, 'Hot Chocolate', 'Whipped cream on request', 'drink', v_cat_coffee, 350, 30, true)
        RETURNING id INTO v_p_hotchoc;

    -- ── Modifier groups ─────────────────────────────────────────────────────
    -- Burger Toppings: optional, multi-select, no cap.
    INSERT INTO concession_modifier_group (tenant_id, name, min_select, max_select, is_required, sort_order, is_active)
        VALUES (v_tenant_id, 'Burger Toppings', 0, NULL, false, 10, true)
        RETURNING id INTO v_mg_toppings;
    INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
        VALUES (v_mg_toppings, 'Extra Cheese', 100, 10, true) RETURNING id INTO v_opt_cheese;
    INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
        VALUES (v_mg_toppings, 'Bacon', 150, 20, true) RETURNING id INTO v_opt_bacon;
    INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
        VALUES (v_mg_toppings, 'Avocado', 150, 30, true) RETURNING id INTO v_opt_avocado;
    INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
        VALUES (v_mg_toppings, 'No Onion', 0, 40, true) RETURNING id INTO v_opt_noonion;

    INSERT INTO concession_product_modifier_group (product_id, group_id, sort_order)
        VALUES (v_p_burger, v_mg_toppings, 10);

    -- Size: required single-select. Priced relative to the listed (Medium) price
    -- so the catalog price stays the advertised $3.50 / $4.00.
    INSERT INTO concession_modifier_group (tenant_id, name, min_select, max_select, is_required, sort_order, is_active)
        VALUES (v_tenant_id, 'Size', 1, 1, true, 20, true)
        RETURNING id INTO v_mg_size;
    INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
        VALUES (v_mg_size, 'Small', -50, 10, true) RETURNING id INTO v_opt_small;
    INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
        VALUES (v_mg_size, 'Medium', 0, 20, true) RETURNING id INTO v_opt_medium;
    INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
        VALUES (v_mg_size, 'Large', 50, 30, true) RETURNING id INTO v_opt_large;

    INSERT INTO concession_product_modifier_group (product_id, group_id, sort_order)
        VALUES (v_p_fountain, v_mg_size, 10), (v_p_coffee, v_mg_size, 10);

    -- ── Past orders (cash tender, no Stripe reconciliation needed) ────────
    -- Order 1: yesterday morning, completed. Burger w/ cheese, fries, medium fountain drink.
    INSERT INTO concession_sale (tenant_id, status, subtotal_cents, total_cents, order_number,
        fulfillment_status, payment_method, order_channel, created_at, paid_at, completed_at)
        VALUES (v_tenant_id, 'paid', 2150, 2150, 1, 'completed', 'cash', 'counter',
            now() - INTERVAL '1 day 6 hours', now() - INTERVAL '1 day 6 hours', now() - INTERVAL '1 day 6 hours' + INTERVAL '8 minutes')
        RETURNING id INTO v_sale_id;
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_burger, 'Smash Burger', 1200, 1, 1300, 'ready') RETURNING id INTO v_line_id;
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_cheese, 'Burger Toppings', 'Extra Cheese', 100);
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_fries, 'Fries', 500, 1, 500, 'ready');
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_fountain, 'Fountain Drink', 350, 1, 350, 'ready') RETURNING id INTO v_line_id;
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_medium, 'Size', 'Medium', 0);

    -- Order 2: yesterday early afternoon, completed. Tenders, pretzel, soda.
    INSERT INTO concession_sale (tenant_id, status, subtotal_cents, total_cents, order_number,
        fulfillment_status, payment_method, order_channel, created_at, paid_at, completed_at)
        VALUES (v_tenant_id, 'paid', 1900, 1900, 2, 'completed', 'cash', 'counter',
            now() - INTERVAL '1 day 3 hours', now() - INTERVAL '1 day 3 hours', now() - INTERVAL '1 day 3 hours' + INTERVAL '6 minutes')
        RETURNING id INTO v_sale_id;
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_tenders, 'Chicken Tenders', 1000, 1, 1000, 'ready');
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_pretzel, 'Pretzel', 600, 1, 600, 'ready');
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_soda, 'Canned Soda', 300, 1, 300, 'ready');

    -- Order 3: yesterday afternoon, completed, bigger ticket. 2x burger (bacon+avocado), 2x fries, 2x large fountain drink.
    INSERT INTO concession_sale (tenant_id, status, subtotal_cents, total_cents, order_number,
        fulfillment_status, payment_method, order_channel, created_at, paid_at, completed_at)
        VALUES (v_tenant_id, 'paid', 4800, 4800, 3, 'completed', 'cash', 'counter',
            now() - INTERVAL '1 day 1 hour', now() - INTERVAL '1 day 1 hour', now() - INTERVAL '1 day 1 hour' + INTERVAL '9 minutes')
        RETURNING id INTO v_sale_id;
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_burger, 'Smash Burger', 1200, 2, 3000, 'ready') RETURNING id INTO v_line_id;
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_bacon, 'Burger Toppings', 'Bacon', 150);
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_avocado, 'Burger Toppings', 'Avocado', 150);
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_fries, 'Fries', 500, 2, 1000, 'ready');
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_fountain, 'Fountain Drink', 350, 2, 800, 'ready') RETURNING id INTO v_line_id;
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_large, 'Size', 'Large', 50);

    -- Order 4: today mid-morning, completed. Coffee (medium), energy bar.
    INSERT INTO concession_sale (tenant_id, status, subtotal_cents, total_cents, order_number,
        fulfillment_status, payment_method, order_channel, created_at, paid_at, completed_at)
        VALUES (v_tenant_id, 'paid', 750, 750, 1, 'completed', 'cash', 'counter',
            now() - INTERVAL '5 hours', now() - INTERVAL '5 hours', now() - INTERVAL '5 hours' + INTERVAL '4 minutes')
        RETURNING id INTO v_sale_id;
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_coffee, 'Coffee', 400, 1, 400, 'ready') RETURNING id INTO v_line_id;
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_medium, 'Size', 'Medium', 0);
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_energybar, 'Energy Bar', 350, 1, 350, 'ready');

    -- Order 5: today midday, completed. Vegan Burger, fries, water.
    INSERT INTO concession_sale (tenant_id, status, subtotal_cents, total_cents, order_number,
        fulfillment_status, payment_method, order_channel, created_at, paid_at, completed_at)
        VALUES (v_tenant_id, 'paid', 1850, 1850, 2, 'completed', 'cash', 'counter',
            now() - INTERVAL '3 hours', now() - INTERVAL '3 hours', now() - INTERVAL '3 hours' + INTERVAL '7 minutes')
        RETURNING id INTO v_sale_id;
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_veggie, 'Vegan Burger', 1100, 1, 1100, 'ready');
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_fries, 'Fries', 500, 1, 500, 'ready');
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_water, 'Bottled Water', 250, 1, 250, 'ready');

    -- Order 6: 20 minutes ago, still "ready" (not yet picked up). Keeps the cook
    -- screen / recent-activity views looking live right now. Burger w/ cheese+bacon,
    -- pretzel, small fountain drink.
    INSERT INTO concession_sale (tenant_id, status, subtotal_cents, total_cents, order_number,
        fulfillment_status, payment_method, order_channel, created_at, paid_at, ready_at)
        VALUES (v_tenant_id, 'paid', 2350, 2350, 3, 'ready', 'cash', 'counter',
            now() - INTERVAL '20 minutes', now() - INTERVAL '20 minutes', now() - INTERVAL '5 minutes')
        RETURNING id INTO v_sale_id;
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_burger, 'Smash Burger', 1200, 1, 1450, 'ready') RETURNING id INTO v_line_id;
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_cheese, 'Burger Toppings', 'Extra Cheese', 100);
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_bacon, 'Burger Toppings', 'Bacon', 150);
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_pretzel, 'Pretzel', 600, 1, 600, 'ready');
    INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
        VALUES (v_sale_id, v_p_fountain, 'Fountain Drink', 350, 1, 300, 'ready') RETURNING id INTO v_line_id;
    INSERT INTO concession_sale_line_modifier (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
        VALUES (v_line_id, v_opt_small, 'Size', 'Small', -50);

    -- ── Keep the daily order-number counter consistent with what we just seeded,
    --    grouped by the actual UTC calendar date of each sale, so the next
    --    real counter-app order doesn't collide with a seeded order_number.
    INSERT INTO concession_order_counter (tenant_id, business_date, last_number)
        SELECT v_tenant_id, (s.created_at AT TIME ZONE 'UTC')::date, MAX(s.order_number)
        FROM concession_sale s
        WHERE s.tenant_id = v_tenant_id AND s.order_number IS NOT NULL
        GROUP BY (s.created_at AT TIME ZONE 'UTC')::date
        ON CONFLICT (tenant_id, business_date) DO UPDATE SET last_number = EXCLUDED.last_number;

    RAISE NOTICE 'Seeded highland concessions for tenant %', v_tenant_id;
END
$hl_concessions$;


-- ============================================================================
-- Highland Bike Park (subdomain 'highland') - demo seed fragment
-- SECTION: bike shop catalog (rental fleet + retail), rentals, lessons
-- (instructors + skill-group ticket tiers), repair work order.
--
-- Schema verified directly against the STAGING database (readonly_mcp lacks
-- SELECT grants on every table this fragment touches - shop_*, instructor,
-- event_instructor - so all discovery below used pg_attribute/pg_constraint,
-- which are readable without table-level privilege; see notes to the boss).
--
-- Confirmed on stage:
--   * The legacy rental_product/rental_item/rental_purchase/event_rental_eligibility
--     tables (Script0043/0048/0177) are RETIRED (Script0200) - zero prod rows,
--     rentals_enabled forced false tenant-wide, v_recent_sales no longer reads them.
--     NOT used below.
--   * Rentals + bike-shop-owned fleet now live entirely on the shop_* catalog
--     (shop_category/shop_product/shop_variant/shop_item/shop_rental/shop_rental_line),
--     introduced by Script0186+.
--   * "Lessons" are still `event` rows with tenant_event_type.code = 'lesson' (named
--     'Clinic' for mountain_bike tenants), but a "training group" IS an
--     event_ticket_tier row (Script0201_LessonGroups) carrying instructor_id,
--     skill_level, equipment_label and its own starts_at/ends_at nested inside the
--     event window. event_instructor is just the event-level "who's working this
--     clinic" roster; shop_lesson_rentable (event_id, variant_id) is the current
--     bike-add-on-for-a-lesson link (successor to the retired event_rental_eligibility).
--   * shop_work_order.status is a free-text CHECK enum, NOT an FK to
--     shop_work_order_status - that table is a tenant-customizable label/color
--     layer the app seeds "lazily in code" (per Script0222's own comment), so a raw
--     seed script has to seed the 7 built-ins itself or the work-order board has no
--     columns to render into.
-- ============================================================================

DO $hl_shop$
DECLARE
    v_tenant_id uuid;
    v_wo_id     uuid;
    v_bike_id   uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found - create it first';
    END IF;

    -- ── Cleanup (children first) ───────────────────────────────────────────
    DELETE FROM shop_work_order_note WHERE work_order_id IN (
        SELECT id FROM shop_work_order WHERE tenant_id = v_tenant_id AND customer_email LIKE '%@highland.test');
    DELETE FROM shop_work_order_line WHERE work_order_id IN (
        SELECT id FROM shop_work_order WHERE tenant_id = v_tenant_id AND customer_email LIKE '%@highland.test');
    DELETE FROM shop_work_order WHERE tenant_id = v_tenant_id AND customer_email LIKE '%@highland.test';
    DELETE FROM shop_customer_bike WHERE tenant_id = v_tenant_id AND customer_phone = '603-555-0199';

    DELETE FROM shop_rental_line WHERE rental_id IN (
        SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email LIKE '%@highland.test');
    DELETE FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email LIKE '%@highland.test';

    -- Retail sale history (year-of-sales fragment) holds RESTRICT FKs onto
    -- shop_variant; clear it before the product/variant wipe below.
    DELETE FROM shop_sale_line WHERE sale_id IN (SELECT id FROM shop_sale WHERE tenant_id = v_tenant_id);
    DELETE FROM shop_sale WHERE tenant_id = v_tenant_id;

    -- shop_variant -> shop_item cascade; shop_product -> shop_variant cascade.
    -- Safe now that shop_rental_line/shop_work_order_line (RESTRICT on variant_id)
    -- have already been cleared above.
    DELETE FROM shop_product WHERE tenant_id = v_tenant_id AND name IN (
        'Santa Cruz V10 DH', 'Giant Reign Enduro', 'Norco Fluid 24 Kids',
        'MTB Tube 27.5/29', 'Lock-On Grips', 'MTB Gloves', 'Full-Face Helmet');
    DELETE FROM shop_category WHERE tenant_id = v_tenant_id AND name IN ('Rental Bikes', 'Shop Retail');

    -- ── Categories ──────────────────────────────────────────────────────────
    INSERT INTO shop_category (tenant_id, name, sort_order) VALUES
        (v_tenant_id, 'Rental Bikes', 10),
        (v_tenant_id, 'Shop Retail',  20);

    -- ── Rental fleet: Santa Cruz V10 DH (downhill) ────────────────────────────────
    INSERT INTO shop_product (tenant_id, category_id, name, description, brand, is_sellable, is_rentable)
        VALUES (v_tenant_id, (SELECT id FROM shop_category WHERE tenant_id = v_tenant_id AND name = 'Rental Bikes'),
                'Santa Cruz V10 DH', 'Premium full-suspension 29in downhill fleet, dialed in for the lift-served trails.',
                'Highland Demo Fleet', false, true);
    INSERT INTO shop_variant (tenant_id, product_id, sku, size, daily_rate_cents, deposit_cents, tracking_kind) VALUES
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Santa Cruz V10 DH'),
            'HL-DH29-M', 'Medium', 15000, 20000, 'serialized'),
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Santa Cruz V10 DH'),
            'HL-DH29-L', 'Large',  15000, 20000, 'serialized');
    INSERT INTO shop_item (tenant_id, variant_id, label, serial, acquired_cost_cents, status) VALUES
        (v_tenant_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-DH29-M'),
            'Santa Cruz V10 DH - Medium', 'HL-DH29-M-01', 280000, 'available'),
        (v_tenant_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-DH29-L'),
            'Santa Cruz V10 DH - Large', 'HL-DH29-L-01', 280000, 'available');

    -- ── Rental fleet: Giant Reign Enduro ───────────────────────────────────────
    INSERT INTO shop_product (tenant_id, category_id, name, description, brand, is_sellable, is_rentable)
        VALUES (v_tenant_id, (SELECT id FROM shop_category WHERE tenant_id = v_tenant_id AND name = 'Rental Bikes'),
                'Giant Reign Enduro', 'Do-it-all enduro/all-mountain bike - the pick for most park laps.',
                'Highland Demo Fleet', false, true);
    INSERT INTO shop_variant (tenant_id, product_id, sku, size, daily_rate_cents, deposit_cents, tracking_kind) VALUES
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Giant Reign Enduro'),
            'HL-TR275-S', 'Small',  13000, 15000, 'serialized'),
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Giant Reign Enduro'),
            'HL-TR275-M', 'Medium', 13000, 15000, 'serialized'),
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Giant Reign Enduro'),
            'HL-TR275-L', 'Large',  13000, 15000, 'serialized');
    INSERT INTO shop_item (tenant_id, variant_id, label, serial, acquired_cost_cents, status) VALUES
        (v_tenant_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-TR275-S'),
            'Giant Reign Enduro - Small', 'HL-TR275-S-01', 180000, 'available'),
        (v_tenant_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-TR275-M'),
            'Giant Reign Enduro - Medium', 'HL-TR275-M-01', 180000, 'available'),
        -- Currently out on the active rental seeded below.
        (v_tenant_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-TR275-L'),
            'Giant Reign Enduro - Large', 'HL-TR275-L-01', 180000, 'rented_out');

    -- ── Rental fleet: Norco Fluid 24 Kids" ─────────────────────────────────────
    INSERT INTO shop_product (tenant_id, category_id, name, description, brand, is_sellable, is_rentable)
        VALUES (v_tenant_id, (SELECT id FROM shop_category WHERE tenant_id = v_tenant_id AND name = 'Rental Bikes'),
                'Norco Fluid 24 Kids', '24in hardtail sized for the kids trails and green-circle laps.',
                'Highland Demo Fleet', false, true);
    -- $130/day per the website's Kids 24"/27.5" rate (the $90 tier is the 20" bike).
    INSERT INTO shop_variant (tenant_id, product_id, sku, size, daily_rate_cents, deposit_cents, tracking_kind)
        VALUES (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Norco Fluid 24 Kids'),
                'HL-KIDS24-STD', 'One Size', 13000, 10000, 'serialized');
    INSERT INTO shop_item (tenant_id, variant_id, label, serial, acquired_cost_cents, status) VALUES
        (v_tenant_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-KIDS24-STD'),
            'Norco Fluid 24 Kids - Unit 1', 'HL-KIDS24-01', 45000, 'available'),
        (v_tenant_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-KIDS24-STD'),
            'Norco Fluid 24 Kids - Unit 2', 'HL-KIDS24-02', 45000, 'available');

    -- ── Retail: tubes / grips / gloves / helmet ─────────────────────────────
    INSERT INTO shop_product (tenant_id, category_id, name, description, is_sellable, is_rentable) VALUES
        (v_tenant_id, (SELECT id FROM shop_category WHERE tenant_id = v_tenant_id AND name = 'Shop Retail'),
            'MTB Tube 27.5/29', 'Standard butyl tube, fits 27.5" and 29" MTB rims.', true, false),
        (v_tenant_id, (SELECT id FROM shop_category WHERE tenant_id = v_tenant_id AND name = 'Shop Retail'),
            'Lock-On Grips', 'Single lock-on collar, half-waffle pattern.', true, false),
        (v_tenant_id, (SELECT id FROM shop_category WHERE tenant_id = v_tenant_id AND name = 'Shop Retail'),
            'MTB Gloves', 'Padded-palm trail glove, breathable back-of-hand mesh.', true, false),
        (v_tenant_id, (SELECT id FROM shop_category WHERE tenant_id = v_tenant_id AND name = 'Shop Retail'),
            'Full-Face Helmet', 'ASTM downhill-rated full-face, required in the bike park lift line.', true, false);

    INSERT INTO shop_variant
        (tenant_id, product_id, sku, sale_price_cents, cost_cents, stock_on_hand, low_stock_threshold, tracking_kind)
        VALUES
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'MTB Tube 27.5/29'),
            'HL-TUBE-2729', 1200, 500, 40, 10, 'pool'),
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Lock-On Grips'),
            'HL-GRIPS-LOCKON', 2500, 1200, 4, 5, 'pool'),   -- intentionally under threshold: demo low-stock flag
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'MTB Gloves'),
            'HL-GLOVES-STD', 3500, 1600, 25, 5, 'pool'),
        (v_tenant_id, (SELECT id FROM shop_product WHERE tenant_id = v_tenant_id AND name = 'Full-Face Helmet'),
            'HL-HELMET-FF', 18000, 9000, 10, 3, 'pool');

    -- ── Work-order status board (app seeds these "lazily in code"; a raw seed
    -- script has to do it explicitly or the board has no columns) ───────────
    INSERT INTO shop_work_order_status
        (tenant_id, code, name, color, behavior, notify_customer, sort_order, is_builtin, is_default)
    SELECT v_tenant_id, s.code, s.name, s.color, s.behavior, s.notify_customer, s.sort_order, true, s.is_default
    FROM (VALUES
        ('estimate',       'Estimate',         'grey',      'estimate',  false, 10, false),
        ('intake',         'Intake',           'blue-grey', 'open',      false, 20, true),
        ('awaiting_parts', 'Awaiting parts',   'warning',   'open',      false, 30, false),
        ('in_progress',    'In progress',      'indigo',    'open',      false, 40, false),
        ('ready',          'Ready for pickup', 'success',   'ready',     true,  50, false),
        ('picked_up',      'Picked up',        'primary',   'done',      false, 60, false),
        ('cancelled',      'Cancelled',        'error',     'cancelled', false, 70, false)
    ) AS s(code, name, color, behavior, notify_customer, sort_order, is_default)
    ON CONFLICT (tenant_id, lower(code)) DO NOTHING;

    -- Bike-shop labor rate, only if the tenant setup fragment left it unset.
    UPDATE tenant SET shop_labor_rate_cents = 6500
        WHERE id = v_tenant_id AND shop_labor_rate_cents IS NULL;

    -- ── Rental history: a few weeks of counter rentals, cash tendered (no
    -- Stripe artifacts to reconcile) ────────────────────────────────────────
    INSERT INTO shop_rental
        (tenant_id, renter_name, renter_email, renter_phone, starts_at, ends_at, status,
         amount_cents, tax_cents, total_cents, deposit_cents, payment_method,
         checked_out_at, returned_at, condition_notes)
    VALUES
        (v_tenant_id, 'Casey Lin', 'casey.lin@highland.test', '603-555-0111',
            now() - INTERVAL '19 days', now() - INTERVAL '18 days', 'returned',
            13000, 0, 13000, 15000, 'cash',
            now() - INTERVAL '19 days', now() - INTERVAL '18 days', 'Returned, normal wear.'),
        (v_tenant_id, 'Drew Malone', 'drew.malone@highland.test', '603-555-0112',
            now() - INTERVAL '15 days', now() - INTERVAL '13 days', 'returned',
            26000, 0, 26000, 15000, 'cash',
            now() - INTERVAL '15 days', now() - INTERVAL '13 days', 'Returned, normal wear.'),
        (v_tenant_id, 'Priya Shah', 'priya.shah@highland.test', '603-555-0113',
            now() - INTERVAL '10 days', now() - INTERVAL '9 days', 'returned',
            15000, 0, 15000, 20000, 'cash',
            now() - INTERVAL '10 days', now() - INTERVAL '9 days', 'Returned, normal wear.'),
        (v_tenant_id, 'Nguyen Family', 'nguyen.family@highland.test', '603-555-0114',
            now() - INTERVAL '6 days', now() - INTERVAL '5 days', 'returned',
            9000, 0, 9000, 10000, 'cash',
            now() - INTERVAL '6 days', now() - INTERVAL '5 days', 'Returned, normal wear.'),
        (v_tenant_id, 'Jordan Blake', 'jordan.blake@highland.test', '603-555-0115',
            now() - INTERVAL '2 days', now() - INTERVAL '1 day', 'returned',
            15000, 0, 15000, 20000, 'cash',
            now() - INTERVAL '2 days', now() - INTERVAL '1 day', 'Returned, normal wear.'),
        (v_tenant_id, 'Sam Rivera', 'sam.rivera@highland.test', '603-555-0116',
            now() - INTERVAL '3 hours', now() + INTERVAL '5 hours', 'out',
            13000, 0, 13000, 15000, 'cash',
            now() - INTERVAL '3 hours', NULL, NULL);

    INSERT INTO shop_rental_line
        (rental_id, variant_id, item_id, quantity, name_snapshot, variant_label,
         daily_rate_cents_frozen, deposit_cents_frozen, line_amount_cents)
    VALUES
        ((SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email = 'casey.lin@highland.test'),
         (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-TR275-S'),
         (SELECT id FROM shop_item WHERE tenant_id = v_tenant_id AND label = 'Giant Reign Enduro - Small'),
         1, 'Giant Reign Enduro', 'Small', 13000, 15000, 13000),
        ((SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email = 'drew.malone@highland.test'),
         (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-TR275-M'),
         (SELECT id FROM shop_item WHERE tenant_id = v_tenant_id AND label = 'Giant Reign Enduro - Medium'),
         1, 'Giant Reign Enduro', 'Medium', 13000, 15000, 26000),
        ((SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email = 'priya.shah@highland.test'),
         (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-DH29-M'),
         (SELECT id FROM shop_item WHERE tenant_id = v_tenant_id AND label = 'Santa Cruz V10 DH - Medium'),
         1, 'Santa Cruz V10 DH', 'Medium', 15000, 20000, 15000),
        ((SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email = 'nguyen.family@highland.test'),
         (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-KIDS24-STD'),
         (SELECT id FROM shop_item WHERE tenant_id = v_tenant_id AND label = 'Norco Fluid 24 Kids - Unit 1'),
         1, 'Norco Fluid 24 Kids', 'One Size', 9000, 10000, 9000),
        ((SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email = 'jordan.blake@highland.test'),
         (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-DH29-L'),
         (SELECT id FROM shop_item WHERE tenant_id = v_tenant_id AND label = 'Santa Cruz V10 DH - Large'),
         1, 'Santa Cruz V10 DH', 'Large', 15000, 20000, 15000),
        ((SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email = 'sam.rivera@highland.test'),
         (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-TR275-L'),
         (SELECT id FROM shop_item WHERE tenant_id = v_tenant_id AND label = 'Giant Reign Enduro - Large'),
         1, 'Giant Reign Enduro', 'Large', 13000, 15000, 13000);

    -- ── One open repair work order: customer drop-off, fork service ────────
    INSERT INTO shop_customer_bike
        (tenant_id, customer_name, customer_phone, brand, model, model_year, color, size, notes)
    VALUES
        (v_tenant_id, 'Jordan Pierce', '603-555-0199', 'Trek', 'Fuel EX 8', 2022, 'Matte Black', 'L',
         'Walk-in drop-off, no rider account on file.')
    RETURNING id INTO v_bike_id;

    INSERT INTO shop_work_order
        (tenant_id, customer_name, customer_phone, customer_email, customer_bike_id,
         status, intake_notes, promised_at, actual_minutes, timer_started_at, created_at)
    VALUES
        (v_tenant_id, 'Jordan Pierce', '603-555-0199', 'jordan.pierce@highland.test', v_bike_id,
         'in_progress', 'Fork feels harsh over square-edge hits - full lower service requested.',
         CURRENT_DATE + 1, 20, now() - INTERVAL '35 minutes', now() - INTERVAL '3 hours')
    RETURNING id INTO v_wo_id;

    INSERT INTO shop_work_order_line
        (work_order_id, line_kind, description, quantity, unit_price_cents, labor_hours, labor_rate_cents,
         approval_status, estimated_minutes)
    VALUES
        (v_wo_id, 'labor', 'Fork lower service - seals, oil, bushings', 1, 9750, 1.5, 6500, 'approved', 90);

    RAISE NOTICE 'Seeded highland bike shop catalog + rentals + repair work order, tenant %', v_tenant_id;
END $hl_shop$;


-- ============================================================================
-- Highland Bike Park - lessons (instructors + skill-group ticket tiers)
-- ============================================================================
DO $hl_lessons$
DECLARE
    v_tenant_id  uuid;
    v_lesson_typ uuid;
    v_evt_id     uuid;
    v_ny_midnight timestamp;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found - create it first';
    END IF;

    v_ny_midnight := date_trunc('day', now() AT TIME ZONE 'America/New_York');

    SELECT id INTO v_lesson_typ FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'lesson';
    IF v_lesson_typ IS NULL THEN
        RAISE EXCEPTION 'tenant_event_type "lesson" missing for highland - seed_default_event_types trigger should have created it on tenant insert';
    END IF;

    -- ── Cleanup ─────────────────────────────────────────────────────────────
    DELETE FROM event_ticket_purchase WHERE tier_id IN (
        SELECT tt.id FROM event_ticket_tier tt JOIN event e ON e.id = tt.event_id
        WHERE e.tenant_id = v_tenant_id AND e.title = 'Find Your Ride Skills Clinic' AND e.event_type_id = v_lesson_typ);
    DELETE FROM shop_lesson_rentable WHERE event_id IN (
        SELECT id FROM event WHERE tenant_id = v_tenant_id AND title = 'Find Your Ride Skills Clinic' AND event_type_id = v_lesson_typ);
    DELETE FROM event_instructor WHERE event_id IN (
        SELECT id FROM event WHERE tenant_id = v_tenant_id AND title = 'Find Your Ride Skills Clinic' AND event_type_id = v_lesson_typ);
    DELETE FROM event_ticket_tier WHERE event_id IN (
        SELECT id FROM event WHERE tenant_id = v_tenant_id AND title = 'Find Your Ride Skills Clinic' AND event_type_id = v_lesson_typ);
    DELETE FROM event WHERE tenant_id = v_tenant_id AND title = 'Find Your Ride Skills Clinic' AND event_type_id = v_lesson_typ;
    -- event_instructor.instructor_id is ON DELETE RESTRICT, so clear any remaining
    -- links to our seed instructors (belt-and-suspenders beyond the event-scoped delete above)
    -- before the instructor rows themselves are removed.
    DELETE FROM event_instructor WHERE instructor_id IN (
        SELECT id FROM instructor WHERE tenant_id = v_tenant_id AND email LIKE '%@highland.test');
    DELETE FROM instructor WHERE tenant_id = v_tenant_id AND email LIKE '%@highland.test';

    -- ── Instructors ─────────────────────────────────────────────────────────
    INSERT INTO instructor (tenant_id, name, email, phone, bio, is_active, max_students_per_session) VALUES
        (v_tenant_id, 'Sam Porter', 'sam.instructor@highland.test', '603-555-0121',
            'PMBIA-certified coach focused on beginner and green-circle progression.', true, 8),
        (v_tenant_id, 'Jo Marchetti', 'jo.coach@highland.test', '603-555-0122',
            'Former enduro racer coaching intermediate and downhill technique.', true, 6);

    -- ── Lesson event: one clinic day, two skill-level groups ────────────────
    INSERT INTO event
        (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, location_label, status)
    VALUES
        (v_tenant_id, v_lesson_typ, 'Find Your Ride Skills Clinic',
         'Coached group sessions split by ability. Bring your own bike or add a rental at checkout.',
         (v_ny_midnight + INTERVAL '9 days' + TIME '09:00') AT TIME ZONE 'America/New_York',
         (v_ny_midnight + INTERVAL '9 days' + TIME '14:00') AT TIME ZONE 'America/New_York',
         false, 'Skills Area / Progression Park', 'scheduled')
    RETURNING id INTO v_evt_id;

    INSERT INTO event_instructor (event_id, instructor_id) VALUES
        (v_evt_id, (SELECT id FROM instructor WHERE tenant_id = v_tenant_id AND email = 'sam.instructor@highland.test')),
        (v_evt_id, (SELECT id FROM instructor WHERE tenant_id = v_tenant_id AND email = 'jo.coach@highland.test'));

    -- Training groups (each IS a ticket tier - Script0201_LessonGroups).
    INSERT INTO event_ticket_tier
        (tenant_id, event_id, name, price_cents, inventory, sort_order, is_active,
         instructor_id, skill_level, equipment_label, starts_at, ends_at, audience)
    VALUES
        (v_tenant_id, v_evt_id, 'Beginner Group (Green Circle)', 14900, 8, 10, true,
         (SELECT id FROM instructor WHERE tenant_id = v_tenant_id AND email = 'sam.instructor@highland.test'),
         'Green Circle', 'Trail',
         (v_ny_midnight + INTERVAL '9 days' + TIME '09:00') AT TIME ZONE 'America/New_York',
         (v_ny_midnight + INTERVAL '9 days' + TIME '11:00') AT TIME ZONE 'America/New_York', 'rider'),
        (v_tenant_id, v_evt_id, 'Intermediate Group (Blue Square)', 15900, 6, 20, true,
         (SELECT id FROM instructor WHERE tenant_id = v_tenant_id AND email = 'jo.coach@highland.test'),
         'Blue Square', 'Downhill',
         (v_ny_midnight + INTERVAL '9 days' + TIME '11:30') AT TIME ZONE 'America/New_York',
         (v_ny_midnight + INTERVAL '9 days' + TIME '13:30') AT TIME ZONE 'America/New_York', 'rider');

    -- Optional bike-with-lesson add-on (shop_lesson_rentable - the shop_* successor
    -- to the retired event_rental_eligibility), priced below the full day rate since
    -- a lesson group only runs ~2 hours. Requires the bike-shop fragment above to
    -- have already inserted these variants (same transaction, so visible here).
    INSERT INTO shop_lesson_rentable (event_id, variant_id, price_cents_override) VALUES
        (v_evt_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-TR275-M'), 3900),
        (v_evt_id, (SELECT id FROM shop_variant WHERE tenant_id = v_tenant_id AND sku = 'HL-DH29-M'), 5900);

    -- A few advance ticket sales so reports have lesson revenue to show.
    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name, payment_method, created_at)
    VALUES
        (v_tenant_id,
         (SELECT id FROM event_ticket_tier WHERE tenant_id = v_tenant_id AND event_id = v_evt_id AND name = 'Beginner Group (Green Circle)'),
         14900, 'paid', 'jamie.fischer@highland.test', 'Jamie Fischer', 'cash', now() - INTERVAL '3 days'),
        (v_tenant_id,
         (SELECT id FROM event_ticket_tier WHERE tenant_id = v_tenant_id AND event_id = v_evt_id AND name = 'Beginner Group (Green Circle)'),
         14900, 'paid', 'morgan.reyes@highland.test', 'Morgan Reyes', 'cash', now() - INTERVAL '2 days'),
        (v_tenant_id,
         (SELECT id FROM event_ticket_tier WHERE tenant_id = v_tenant_id AND event_id = v_evt_id AND name = 'Intermediate Group (Blue Square)'),
         15900, 'paid', 'taylor.brooks@highland.test', 'Taylor Brooks', 'cash', now() - INTERVAL '1 day');

    RAISE NOTICE 'Seeded highland lessons: instructors + Find Your Ride Skills Clinic, tenant %', v_tenant_id;
END $hl_lessons$;


-- ============================================================================
-- Highland Bike Park -- ONE FULL YEAR of sales history (~$4.0M total revenue)
--
-- Target mix (per the Highland sales-demo brief):
--   ~70% tickets + season passes + camps/clinics  (~$2.80M)
--   ~15% bike shop retail + rentals               (~$0.60M)
--   ~15% food & beverage                          (~$0.60M)
--
-- Seasonality mirrors the park's real published calendar
-- (highlandmountain.com, fetched 2026-07-23):
--   Spring  Apr 23 - Jun 28   lift Thu-Sun, Wednesduro uphill nights
--   Summer  Jun 29 - Sep  7   lift 7 days, Wednesduro nights
--   Fall    Sep  8 - Nov  1   lift Thu-Sun, Wednesduro nights
--   LateFall Nov 2 - Nov 15   lift Thu-Sun
--   Winter                    closed (season-pass sales only: Black Friday +
--                             the spring pass-sale ramp)
--
-- Prices are the park's real published prices: day tickets $82/$68 full,
-- $55/$45 happy hour, $41/$34 junior full, $30/$25 junior happy hour,
-- Wednesduro $20; private lesson 2hr $219; Ayr Academy camp $1,665 day /
-- $2,350 overnight, Summer Ride camp $825 day / $1,240 overnight, CIT $2,085;
-- rentals $150 (V10) / $130 (Reign, Kids 24"); season passes $669/$519/$159.
--
-- Rerunnable: everything this fragment creates carries a scoping marker --
--   * purchaser/renter/buyer emails end in `.hl@highland.test`
--   * events use this fragment's own titles
--   * concession history is strictly older than the base fragment's 2-day demo
--     window (deleted by age)
-- so the wipe below removes only this fragment's rows. The base fragments'
-- own wipes also cover these rows (same tenant / @highland.test), so the full
-- file remains rerunnable end to end.
-- ============================================================================

DO $hl_sales_year$
DECLARE
    v_tenant_id  uuid;
    v_open_ride  uuid;
    v_race       uuid;
    v_lesson     uuid;
    v_camp       uuid;
    v_sp_unlim   uuid;
    v_sp_teen    uuid;
    v_sp_wed     uuid;
    v_sp_3ride   uuid;
    v_instr_sam  uuid;
    v_instr_jo   uuid;
    v_rider_hash text;

    v_today_ny   date;
    v_start      date;   -- history window start (365 days back)
    v_end        date;   -- history window end (3 days back; the base fragments own the last ~2 days)
    v_y          int;    -- "current season" year
    v_total_w    numeric;

    -- Revenue tuning constants (cents). See the mix math in the header.
    c_day_ticket_target constant bigint := 187900000;  -- ~$1.88M open-riding gate revenue
                                                       -- (tier-count rounding trims ~3.5%, landing
                                                       -- the 70/15/15 mix on a $4.0M grand total)
    c_pass_count        constant int    := 1200;       -- season passes across the window

    v_first constant text[] := ARRAY['Avery','Blake','Casey','Devon','Emerson','Finley','Gray','Harper',
        'Indie','Jules','Kai','Logan','Marlow','Nico','Oakley','Parker','Quinn','Reese','Sawyer','Tatum',
        'Uma','Vaughn','Wren','Xavier','Yara','Zane','Micah','Lena','Theo','Sasha','Colby','Dana'];
    v_last constant text[] := ARRAY['Abbott','Barnes','Cortez','Dalton','Ellison','Fleming','Garner','Hayes',
        'Ibarra','Jennings','Keller','Lawson','Merritt','Nolan','Osborne','Pratt','Quimby','Rowe','Sutton','Tran',
        'Underwood','Vasquez','Whitaker','Xu','York','Zimmerman','Calloway','Drummond','Eastman','Forsythe','Granger','Holloway'];

    v_sum_tickets bigint; v_sum_passes bigint; v_sum_shop bigint; v_sum_fnb bigint;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found - create it first';
    END IF;

    SELECT id INTO v_open_ride FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'open_ride';
    SELECT id INTO v_race      FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'race';
    SELECT id INTO v_lesson    FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'lesson';
    IF v_open_ride IS NULL OR v_race IS NULL OR v_lesson IS NULL THEN
        RAISE EXCEPTION 'standard tenant_event_type rows missing for tenant %', v_tenant_id;
    END IF;

    -- Custom "Camp" event type (multi-day programs). Idempotent.
    SELECT id INTO v_camp FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'camp';
    IF v_camp IS NULL THEN
        INSERT INTO tenant_event_type (tenant_id, code, name, color, sort_order, is_system)
            VALUES (v_tenant_id, 'camp', 'Camp', '#F57C00', 50, false)
            RETURNING id INTO v_camp;
    END IF;

    SELECT id INTO v_sp_unlim FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Adult All-Access Season Pass';
    SELECT id INTO v_sp_teen  FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Teen All-Access Season Pass';
    SELECT id INTO v_sp_wed   FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Wednesduro Season Pass';
    SELECT id INTO v_sp_3ride FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = '3 Ride Pass';
    IF v_sp_unlim IS NULL OR v_sp_teen IS NULL OR v_sp_wed IS NULL OR v_sp_3ride IS NULL THEN
        RAISE EXCEPTION 'season pass products missing - run the ticketing fragment first';
    END IF;

    SELECT id INTO v_instr_sam FROM instructor WHERE tenant_id = v_tenant_id AND email = 'sam.instructor@highland.test';
    SELECT id INTO v_instr_jo  FROM instructor WHERE tenant_id = v_tenant_id AND email = 'jo.coach@highland.test';
    IF v_instr_sam IS NULL OR v_instr_jo IS NULL THEN
        RAISE EXCEPTION 'seed instructors missing - run the lessons fragment first';
    END IF;

    SELECT password_hash INTO v_rider_hash FROM users WHERE lower(email) = lower('qa.rider@ridepass-qa.test');
    IF v_rider_hash IS NULL THEN
        RAISE EXCEPTION 'reference user qa.rider@ridepass-qa.test not found';
    END IF;

    PERFORM setseed(0.42);
    v_today_ny := (now() AT TIME ZONE 'America/New_York')::date;
    v_end   := v_today_ny - 3;
    v_start := v_today_ny - 365;
    v_y     := EXTRACT(YEAR FROM v_end)::int;

    -- ══════════════════════════════════════════════════════════════════════
    -- WIPE this fragment's prior rows (marker-scoped; children first)
    -- ══════════════════════════════════════════════════════════════════════
    DELETE FROM event_ticket_purchase
     WHERE tenant_id = v_tenant_id
       AND (purchaser_email LIKE '%.hl@highland.test'
            OR tier_id IN (SELECT tt.id FROM event_ticket_tier tt
                            JOIN event e ON e.id = tt.event_id
                           WHERE e.tenant_id = v_tenant_id
                             AND (e.title IN ('Open Riding','Wednesduro Night Session','Saturday Skills Clinic')
                                  OR e.title LIKE 'Highland Race Series%'
                                  OR e.title LIKE 'Ayr Academy Camp%'
                                  OR e.title LIKE 'Summer Ride Camp%')));
    DELETE FROM event
     WHERE tenant_id = v_tenant_id
       AND (title IN ('Open Riding','Wednesduro Night Session','Saturday Skills Clinic')
            OR title LIKE 'Highland Race Series%'
            OR title LIKE 'Ayr Academy Camp%'
            OR title LIKE 'Summer Ride Camp%');

    DELETE FROM season_pass_reservation WHERE season_pass_purchase_id IN (
        SELECT id FROM season_pass_purchase WHERE tenant_id = v_tenant_id AND purchaser_email LIKE '%.hl@highland.test');
    DELETE FROM season_pass_purchase WHERE tenant_id = v_tenant_id AND purchaser_email LIKE '%.hl@highland.test';
    DELETE FROM users WHERE tenant_id IS NULL AND email LIKE '%.hl@highland.test';

    -- F&B history is delete-by-age: this fragment only writes orders older than
    -- 3 days; the base concessions fragment owns the fresh demo orders.
    DELETE FROM concession_sale WHERE tenant_id = v_tenant_id AND created_at < now() - INTERVAL '60 hours';

    DELETE FROM shop_rental_line WHERE rental_id IN (
        SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email LIKE '%.hl@highland.test');
    DELETE FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email LIKE '%.hl@highland.test';

    DELETE FROM shop_sale_line WHERE sale_id IN (SELECT id FROM shop_sale WHERE tenant_id = v_tenant_id);
    DELETE FROM shop_sale WHERE tenant_id = v_tenant_id;

    -- ══════════════════════════════════════════════════════════════════════
    -- OPERATING CALENDAR + per-day demand model
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_day;
    CREATE TEMP TABLE _hl_day ON COMMIT DROP AS
    SELECT d.day,
           d.dow,
           d.mm,
           d.mmdd,
           (d.dow IN (0, 6)) AS weekend,
           ((d.mmdd BETWEEN 423 AND 1115 AND d.dow IN (0,4,5,6)) OR (d.mmdd BETWEEN 629 AND 907)) AS lift,
           (d.dow = 3 AND d.mmdd BETWEEN 423 AND 1101) AS wednight,
           (CASE d.mm WHEN 4 THEN 0.35 WHEN 5 THEN 0.60 WHEN 6 THEN 0.85 WHEN 7 THEN 1.00
                      WHEN 8 THEN 1.00 WHEN 9 THEN 0.75 WHEN 10 THEN 0.65 WHEN 11 THEN 0.30 ELSE 0 END) AS month_mult,
           (CASE d.dow WHEN 6 THEN 1.00 WHEN 0 THEN 0.80 WHEN 5 THEN 0.48 WHEN 4 THEN 0.30
                       WHEN 3 THEN 0.22 WHEN 1 THEN 0.20 WHEN 2 THEN 0.18 END) AS dow_base,
           (0.8 + 0.4 * random()) AS jitter
    FROM (SELECT g::date AS day,
                 EXTRACT(DOW  FROM g)::int AS dow,
                 EXTRACT(MONTH FROM g)::int AS mm,
                 (EXTRACT(MONTH FROM g) * 100 + EXTRACT(DAY FROM g))::int AS mmdd
          FROM generate_series(v_start::timestamp, v_end::timestamp, INTERVAL '1 day') g) d;

    ALTER TABLE _hl_day ADD COLUMN w numeric, ADD COLUMN rev_target int,
        ADD COLUMN n_fa int, ADD COLUMN n_fj int, ADD COLUMN n_ha int, ADD COLUMN n_hj int,
        ADD COLUMN fnb_target int, ADD COLUMN n_rent int, ADD COLUMN n_retail int, ADD COLUMN n_wed int;

    UPDATE _hl_day SET w = CASE WHEN lift THEN dow_base * month_mult * jitter ELSE 0 END;
    SELECT COALESCE(SUM(w), 0) INTO v_total_w FROM _hl_day;
    IF v_total_w <= 0 THEN
        RAISE EXCEPTION 'no lift-operating days fell inside the seed window % .. %', v_start, v_end;
    END IF;

    UPDATE _hl_day SET rev_target = round(c_day_ticket_target * w / v_total_w)::int WHERE lift;
    UPDATE _hl_day SET
        n_fa = CASE WHEN weekend THEN round(rev_target * 0.52 / 8200) ELSE round(rev_target * 0.55 / 6800) END::int,
        n_fj = CASE WHEN weekend THEN round(rev_target * 0.16 / 4100) ELSE round(rev_target * 0.13 / 3400) END::int,
        n_ha = CASE WHEN weekend THEN round(rev_target * 0.21 / 5500) ELSE round(rev_target * 0.22 / 4500) END::int,
        n_hj = CASE WHEN weekend THEN round(rev_target * 0.11 / 3000) ELSE round(rev_target * 0.10 / 2500) END::int
    WHERE lift;
    UPDATE _hl_day SET
        fnb_target = round(rev_target * (0.275 + 0.09 * random()))::int,
        n_rent     = round((n_fa + n_fj + n_ha + n_hj) * 0.091)::int,
        n_retail   = round((n_fa + n_fj + n_ha + n_hj) * 0.105)::int
    WHERE lift;
    UPDATE _hl_day SET n_wed = round((30 + 40 * month_mult) * (0.8 + 0.4 * random()))::int WHERE wednight;

    -- ══════════════════════════════════════════════════════════════════════
    -- OPEN RIDING day events + gate tickets
    -- Days already carrying a base-fragment Open Ride event reuse its tiers.
    -- ══════════════════════════════════════════════════════════════════════
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
    SELECT v_tenant_id, v_open_ride, 'Open Riding',
           'Lift-served trails open, all skill levels.',
           (d.day + TIME '09:00') AT TIME ZONE 'America/New_York',
           (d.day + TIME '17:00') AT TIME ZONE 'America/New_York',
           false, 700, 'Lift Base Area', 'scheduled'
    FROM _hl_day d
    WHERE d.lift
      AND NOT EXISTS (SELECT 1 FROM event e
                       WHERE e.tenant_id = v_tenant_id AND e.event_type_id = v_open_ride
                         AND (e.starts_at AT TIME ZONE 'America/New_York')::date = d.day);

    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
    SELECT v_tenant_id, e.id, t.name,
           CASE WHEN d.weekend THEN t.we_price ELSE t.mw_price END,
           t.inventory, t.sort_order, 'gate_fee', 'rider'
    FROM event e
    JOIN _hl_day d ON d.day = (e.starts_at AT TIME ZONE 'America/New_York')::date
    CROSS JOIN (VALUES
        ('Full Day Lift Ticket',           8200, 6800, 500, 10),
        ('Junior Lift Ticket (7-14)',      4100, 3400, 200, 20),
        ('Happy Hour Ticket (2pm-Close)',  5500, 4500, 300, 30),
        ('Junior Happy Hour (7-14)',       3000, 2500, 150, 40)
    ) AS t(name, we_price, mw_price, inventory, sort_order)
    WHERE e.tenant_id = v_tenant_id AND e.title = 'Open Riding';

    -- Per-day tier plan: this fragment's events map 1:1 by tier name; the base
    -- fragment's few pre-existing days fold junior happy-hour demand into its
    -- junior full-day tier (it has no junior happy-hour tier).
    DROP TABLE IF EXISTS _hl_tier;
    CREATE TEMP TABLE _hl_tier ON COMMIT DROP AS
    SELECT d.day, d.weekend, tt.id AS tier_id, tt.price_cents,
           CASE tt.name
               WHEN 'Full Day Lift Ticket'          THEN 'full'
               WHEN 'Junior Lift Ticket (7-14)'     THEN 'full'
               ELSE 'happy'
           END AS window_kind,
           CASE tt.name
               WHEN 'Full Day Lift Ticket'          THEN d.n_fa
               WHEN 'Happy Hour Ticket (2pm-Close)' THEN d.n_ha
               WHEN 'Junior Happy Hour (7-14)'      THEN d.n_hj
               WHEN 'Junior Lift Ticket (7-14)'     THEN
                   CASE WHEN e.title = 'Open Riding' THEN d.n_fj ELSE d.n_fj + d.n_hj END
               ELSE 0
           END AS n
    FROM _hl_day d
    JOIN event e ON e.tenant_id = v_tenant_id AND e.event_type_id = v_open_ride
               AND (e.starts_at AT TIME ZONE 'America/New_York')::date = d.day
    JOIN event_ticket_tier tt ON tt.event_id = e.id
    WHERE d.lift
      AND tt.name IN ('Full Day Lift Ticket','Junior Lift Ticket (7-14)',
                      'Happy Hour Ticket (2pm-Close)','Junior Happy Hour (7-14)');

    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at, redeemed_at_utc)
    SELECT v_tenant_id, y.tier_id, y.price_cents,
           CASE WHEN y.r_status < 0.87 THEN 'redeemed' ELSE 'paid' END,
           lower(y.fn || '.' || y.ln || '.' || y.g || '.hl@highland.test'),
           y.fn || ' ' || y.ln,
           CASE WHEN y.advance THEN 'stripe'              -- advance buys are always online
                WHEN y.r_pay < 0.35 THEN 'cash' ELSE 'stripe' END,
           y.created_at, y.created_at,
           CASE WHEN y.r_status < 0.87 THEN y.redeemed_at ELSE NULL END
    FROM (
        SELECT x.*,
               CASE WHEN x.advance
                    THEN ((x.day - x.adv_days + TIME '08:00') AT TIME ZONE 'America/New_York')
                         + (x.m_adv * INTERVAL '1 minute')
                    WHEN x.window_kind = 'full'
                    THEN ((x.day + TIME '07:40') AT TIME ZONE 'America/New_York') + (x.m_full * INTERVAL '1 minute')
                    ELSE ((x.day + TIME '13:20') AT TIME ZONE 'America/New_York') + (x.m_happy * INTERVAL '1 minute')
               END AS created_at
        FROM (
            SELECT t.tier_id, t.price_cents, g.g,
                   v_first[1 + floor(random() * 32)::int] AS fn,
                   v_last [1 + floor(random() * 32)::int] AS ln,
                   random() AS r_status,
                   random() AS r_pay,
                   (random() < 0.30) AS advance,
                   (1 + floor(random() * 9))::int AS adv_days,
                   floor(random() * 780)::int AS m_adv,
                   floor(random() * 230)::int AS m_full,
                   floor(random() * 110)::int AS m_happy,
                   t.day, t.window_kind,
                   CASE WHEN t.window_kind = 'full'
                        THEN ((t.day + TIME '08:50') AT TIME ZONE 'America/New_York') + (floor(random() * 180) * INTERVAL '1 minute')
                        ELSE ((t.day + TIME '14:00') AT TIME ZONE 'America/New_York') + (floor(random() * 90)  * INTERVAL '1 minute')
                   END AS redeemed_at
            FROM _hl_tier t
            CROSS JOIN LATERAL generate_series(1, GREATEST(t.n, 0)) g(g)
        ) x
    ) y;

    -- ══════════════════════════════════════════════════════════════════════
    -- WEDNESDURO uphill nights ($20 evening pass)
    -- ══════════════════════════════════════════════════════════════════════
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
    SELECT v_tenant_id, v_open_ride, 'Wednesduro Night Session',
           'Evening uphill + e-bike access night. Lit pump track.',
           (d.day + TIME '17:00') AT TIME ZONE 'America/New_York',
           (d.day + TIME '20:30') AT TIME ZONE 'America/New_York',
           false, 150, 'Lift Base Area / Pump Track', 'scheduled'
    FROM _hl_day d WHERE d.wednight;

    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
    SELECT v_tenant_id, e.id, 'Wednesduro Evening Pass', 2000, 150, 10, 'gate_fee', 'rider'
    FROM event e WHERE e.tenant_id = v_tenant_id AND e.title = 'Wednesduro Night Session';

    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at, redeemed_at_utc)
    SELECT v_tenant_id, x.tier_id, 2000,
           CASE WHEN x.r_status < 0.90 THEN 'redeemed' ELSE 'paid' END,
           lower(x.fn || '.' || x.ln || '.w' || x.g || '.hl@highland.test'),
           x.fn || ' ' || x.ln,
           CASE WHEN x.r_pay < 0.40 THEN 'cash' ELSE 'stripe' END,
           x.created_at, x.created_at,
           CASE WHEN x.r_status < 0.90 THEN x.created_at + INTERVAL '10 minutes' ELSE NULL END
    FROM (
        SELECT tt.id AS tier_id, g.g,
               v_first[1 + floor(random() * 32)::int] AS fn,
               v_last [1 + floor(random() * 32)::int] AS ln,
               random() AS r_status, random() AS r_pay,
               ((d.day + TIME '16:30') AT TIME ZONE 'America/New_York') + (floor(random() * 180) * INTERVAL '1 minute') AS created_at
        FROM _hl_day d
        JOIN event e ON e.tenant_id = v_tenant_id AND e.title = 'Wednesduro Night Session'
                    AND (e.starts_at AT TIME ZONE 'America/New_York')::date = d.day
        JOIN event_ticket_tier tt ON tt.event_id = e.id
        CROSS JOIN LATERAL generate_series(1, GREATEST(d.n_wed, 0)) g(g)
        WHERE d.wednight
    ) x;

    -- ══════════════════════════════════════════════════════════════════════
    -- RACE SERIES: first Saturday of Aug/Sep/Oct (prev season) + May/Jun/Jul
    -- ══════════════════════════════════════════════════════════════════════
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity,
                       location_label, status, allows_spectators)
    SELECT v_tenant_id, v_race,
           'Highland Race Series - ' || to_char(r.race_day, 'Mon YYYY'),
           'Head-to-head dual slalom racing, all classes. Practice 8-9am, racing 9am-4pm.',
           (r.race_day + TIME '09:00') AT TIME ZONE 'America/New_York',
           (r.race_day + TIME '16:00') AT TIME ZONE 'America/New_York',
           false, 200, 'Dual Slalom Course', 'scheduled', true
    FROM (
        SELECT (m.m0 + ((6 - EXTRACT(DOW FROM m.m0)::int + 7) % 7))::date AS race_day
        FROM (VALUES (make_date(v_y - 1, 8, 1)), (make_date(v_y - 1, 9, 1)), (make_date(v_y - 1, 10, 1)),
                     (make_date(v_y, 5, 1)), (make_date(v_y, 6, 1)), (make_date(v_y, 7, 1))) m(m0)
    ) r
    WHERE r.race_day BETWEEN v_start AND v_end;

    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
    SELECT v_tenant_id, e.id, t.name, t.price, t.inv, t.so, t.kind, t.aud
    FROM event e
    CROSS JOIN (VALUES
        ('Pro Class Entry',         7500, 30,  10, 'race_entry', 'rider'),
        ('Am Class Entry',          5500, 75,  20, 'race_entry', 'rider'),
        ('Junior Class Entry',      3500, 30,  30, 'race_entry', 'rider'),
        ('Race Day Spectator Gate', 1000, 200, 40, 'gate_fee',   'spectator')
    ) AS t(name, price, inv, so, kind, aud)
    WHERE e.tenant_id = v_tenant_id AND e.title LIKE 'Highland Race Series%';

    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at, redeemed_at_utc)
    SELECT v_tenant_id, x.tier_id, x.price_cents,
           CASE WHEN x.r_status < 0.92 THEN 'redeemed' ELSE 'paid' END,
           lower(x.fn || '.' || x.ln || '.r' || x.g || '.hl@highland.test'),
           x.fn || ' ' || x.ln,
           'stripe',
           x.created_at, x.created_at,
           CASE WHEN x.r_status < 0.92
                THEN ((x.race_day + TIME '07:45') AT TIME ZONE 'America/New_York') + (floor(random() * 90) * INTERVAL '1 minute')
                ELSE NULL END
    FROM (
        SELECT tt.id AS tier_id, tt.price_cents, g.g,
               (e.starts_at AT TIME ZONE 'America/New_York')::date AS race_day,
               v_first[1 + floor(random() * 32)::int] AS fn,
               v_last [1 + floor(random() * 32)::int] AS ln,
               random() AS r_status,
               ((e.starts_at AT TIME ZONE 'America/New_York')::date
                    - (1 + floor(random() * 13))::int + TIME '09:00') AT TIME ZONE 'America/New_York'
                    + (floor(random() * 660) * INTERVAL '1 minute') AS created_at
        FROM event e
        JOIN event_ticket_tier tt ON tt.event_id = e.id
        CROSS JOIN LATERAL generate_series(1,
            CASE tt.name
                WHEN 'Pro Class Entry'         THEN 18 + floor(random() * 9)::int
                WHEN 'Am Class Entry'          THEN 45 + floor(random() * 21)::int
                WHEN 'Junior Class Entry'      THEN 15 + floor(random() * 11)::int
                ELSE 55 + floor(random() * 31)::int
            END) g(g)
        WHERE e.tenant_id = v_tenant_id AND e.title LIKE 'Highland Race Series%'
    ) x;

    -- ══════════════════════════════════════════════════════════════════════
    -- SATURDAY SKILLS CLINICS (weekly, in season) - group lessons + a
    -- private-lesson tier at the park's real $219 2-hour rate
    -- ══════════════════════════════════════════════════════════════════════
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
    SELECT v_tenant_id, v_lesson, 'Saturday Skills Clinic',
           'Coached group sessions split by ability, plus bookable private lessons. Bring your own bike or add a rental.',
           (d.day + TIME '09:00') AT TIME ZONE 'America/New_York',
           (d.day + TIME '14:00') AT TIME ZONE 'America/New_York',
           false, 20, 'Skills Zone / Progression Park', 'scheduled'
    FROM _hl_day d
    WHERE d.lift AND d.dow = 6;

    INSERT INTO event_ticket_tier
        (tenant_id, event_id, name, price_cents, inventory, sort_order, is_active,
         instructor_id, skill_level, equipment_label, starts_at, ends_at, audience)
    SELECT v_tenant_id, e.id, t.name, t.price, t.inv, t.so, true,
           CASE t.so WHEN 10 THEN v_instr_sam WHEN 20 THEN v_instr_jo ELSE NULL END,
           t.skill, t.equip,
           e.starts_at + t.ofs_start, e.starts_at + t.ofs_end, 'rider'
    FROM event e
    CROSS JOIN (VALUES
        ('Beginner Group (Green Circle)',    14900, 8, 10, 'Green Circle', 'Trail',    INTERVAL '0 hours', INTERVAL '2 hours'),
        ('Intermediate Group (Blue Square)', 15900, 6, 20, 'Blue Square',  'Downhill', INTERVAL '2.5 hours', INTERVAL '4.5 hours'),
        ('Private Lesson (2hr)',             21900, 3, 30, NULL,           NULL,       INTERVAL '0 hours', INTERVAL '2 hours')
    ) AS t(name, price, inv, so, skill, equip, ofs_start, ofs_end)
    WHERE e.tenant_id = v_tenant_id AND e.title = 'Saturday Skills Clinic';

    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at, redeemed_at_utc)
    SELECT v_tenant_id, x.tier_id, x.price_cents,
           CASE WHEN x.r_status < 0.93 THEN 'redeemed' ELSE 'paid' END,
           lower(x.fn || '.' || x.ln || '.c' || x.g || '.hl@highland.test'),
           x.fn || ' ' || x.ln,
           'stripe',
           x.created_at, x.created_at,
           CASE WHEN x.r_status < 0.93
                THEN ((x.clinic_day + TIME '08:40') AT TIME ZONE 'America/New_York') + (floor(random() * 60) * INTERVAL '1 minute')
                ELSE NULL END
    FROM (
        SELECT tt.id AS tier_id, tt.price_cents, g.g,
               (e.starts_at AT TIME ZONE 'America/New_York')::date AS clinic_day,
               v_first[1 + floor(random() * 32)::int] AS fn,
               v_last [1 + floor(random() * 32)::int] AS ln,
               random() AS r_status,
               ((e.starts_at AT TIME ZONE 'America/New_York')::date
                    - (1 + floor(random() * 11))::int + TIME '09:00') AT TIME ZONE 'America/New_York'
                    + (floor(random() * 700) * INTERVAL '1 minute') AS created_at
        FROM event e
        JOIN event_ticket_tier tt ON tt.event_id = e.id
        CROSS JOIN LATERAL generate_series(1,
            CASE tt.sort_order
                WHEN 10 THEN 4 + floor(random() * 5)::int   -- beginner 4-8
                WHEN 20 THEN 3 + floor(random() * 4)::int   -- intermediate 3-6
                ELSE         1 + floor(random() * 3)::int   -- private 1-3
            END) g(g)
        WHERE e.tenant_id = v_tenant_id AND e.title = 'Saturday Skills Clinic'
    ) x;

    -- ══════════════════════════════════════════════════════════════════════
    -- SUMMER CAMPS - real programs, prices, and weekly session dates:
    -- Ayr Academy (Mon-Fri, $1,665 day / $2,350 overnight; CIT $2,085 in
    -- sessions 4-5) and Summer Ride (Mon-Wed, $825 day / $1,240 overnight in
    -- sessions 1-3). Seven weekly sessions from the last Sunday of June.
    -- Registrations land Feb..session-start, so camp revenue books in spring.
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_camp;
    CREATE TEMP TABLE _hl_camp ON COMMIT DROP AS
    SELECT s.n AS session_no,
           (make_date(v_y, 6, 28) + (s.n - 1) * 7 + 1)::date AS mon,   -- session week's Monday
           p.program
    FROM generate_series(1, 7) s(n)
    CROSS JOIN (VALUES ('ayr'), ('sr')) p(program);

    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
    SELECT v_tenant_id, v_camp,
           CASE c.program WHEN 'ayr' THEN 'Ayr Academy Camp (Session ' || c.session_no || ')'
                          ELSE 'Summer Ride Camp (Session ' || c.session_no || ')' END,
           CASE c.program
               WHEN 'ayr' THEN 'Five-day progression camp for ages 10-16. Day or overnight; bike + gear rentals available.'
               ELSE 'Three-day intro camp for ages 8-13. Day or overnight; bike + gear rentals available.' END,
           (c.mon + TIME '09:00') AT TIME ZONE 'America/New_York',
           ((c.mon + CASE c.program WHEN 'ayr' THEN 4 ELSE 2 END) + TIME '15:00') AT TIME ZONE 'America/New_York',
           false, CASE c.program WHEN 'ayr' THEN 26 ELSE 16 END,
           'Camp Basecamp / Progression Park', 'scheduled'
    FROM _hl_camp c;

    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
    SELECT v_tenant_id, e.id, t.name, t.price, t.inv, t.so, 'gate_fee', 'rider'
    FROM _hl_camp c
    JOIN event e ON e.tenant_id = v_tenant_id
                AND e.title = CASE c.program WHEN 'ayr' THEN 'Ayr Academy Camp (Session ' || c.session_no || ')'
                                             ELSE 'Summer Ride Camp (Session ' || c.session_no || ')' END
    CROSS JOIN LATERAL (
        SELECT * FROM (VALUES
            ('ayr', 'Day Camp (Ages 10-16)',        166500, 14, 10, 0),
            ('ayr', 'Overnight Camp (Ages 10-16)',  235000,  8, 20, 0),
            ('ayr', 'CIT Program (Overnight Week)', 208500,  4, 30, 1),
            ('sr',  'Day Camp (Ages 8-13)',          82500, 12, 10, 0),
            ('sr',  'Overnight Camp (Ages 8-13)',   124000,  4, 20, 2)
        ) v(program, name, price, inv, so, gate)
        WHERE v.program = c.program
          AND (v.gate = 0
               OR (v.gate = 1 AND c.session_no IN (4, 5))     -- CIT: sessions 4-5 only
               OR (v.gate = 2 AND c.session_no <= 3))         -- SR overnight: sessions 1-3 only
    ) t;

    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at, redeemed_at_utc)
    SELECT v_tenant_id, x.tier_id, x.price_cents,
           CASE WHEN x.mon <= v_end THEN 'redeemed' ELSE 'paid' END,
           lower(x.fn || '.' || x.ln || '.k' || x.g || '.hl@highland.test'),
           x.fn || ' ' || x.ln,
           'stripe',
           x.created_at, x.created_at,
           CASE WHEN x.mon <= v_end
                THEN ((x.mon + TIME '08:30') AT TIME ZONE 'America/New_York') + (floor(random() * 45) * INTERVAL '1 minute')
                ELSE NULL END
    FROM (
        SELECT tt.id AS tier_id, tt.price_cents, g.g, c.mon,
               v_first[1 + floor(random() * 32)::int] AS fn,
               v_last [1 + floor(random() * 32)::int] AS ln,
               -- Registered somewhere from Feb 1 up to a week before the session
               -- (or up to the history-window end for the upcoming sessions).
               ((LEAST(c.mon - 7, v_end)
                     - floor(random() * GREATEST(LEAST(c.mon - 7, v_end) - make_date(v_y, 2, 1), 1))::int
                     + TIME '09:00') AT TIME ZONE 'America/New_York')
                  + (floor(random() * 600) * INTERVAL '1 minute') AS created_at
        FROM _hl_camp c
        JOIN event e ON e.tenant_id = v_tenant_id
                    AND e.title = CASE c.program WHEN 'ayr' THEN 'Ayr Academy Camp (Session ' || c.session_no || ')'
                                                 ELSE 'Summer Ride Camp (Session ' || c.session_no || ')' END
        JOIN event_ticket_tier tt ON tt.event_id = e.id
        CROSS JOIN LATERAL generate_series(1,
            CASE
                WHEN tt.name LIKE 'Day Camp (Ages 10-16)%'       THEN 6 + floor(random() * 4)::int  -- 6-9
                WHEN tt.name LIKE 'Overnight Camp (Ages 10-16)%' THEN 3 + floor(random() * 2)::int  -- 3-4
                WHEN tt.name LIKE 'CIT%'                         THEN 2 + floor(random() * 2)::int  -- 2-3
                WHEN tt.name LIKE 'Day Camp (Ages 8-13)%'        THEN 6 + floor(random() * 4)::int  -- 6-9
                ELSE                                                  1 + floor(random() * 2)::int  -- 1-2
            END) g(g)
    ) x
    WHERE x.created_at >= (v_start + TIME '00:00') AT TIME ZONE 'America/New_York';

    -- ══════════════════════════════════════════════════════════════════════
    -- SEASON PASSES: ~1,200 passes with buyer accounts. Sales cluster on
    -- Black Friday and the Mar-May spring ramp, tail through midsummer, plus
    -- a small batch of late buys for the previous season.
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_pass;
    CREATE TEMP TABLE _hl_pass ON COMMIT DROP AS
    SELECT n,
           v_first[1 + floor(random() * 32)::int] AS fn,
           v_last [1 + floor(random() * 32)::int] AS ln,
           random() AS r_when, random() AS r_span, random() AS r_prod, random() AS r_pay,
           random() AS r_burn,
           floor(random() * 720)::int AS minute_of_day
    FROM generate_series(1, c_pass_count) n;

    ALTER TABLE _hl_pass ADD COLUMN pdate date, ADD COLUMN product_id uuid,
        ADD COLUMN amount int, ADD COLUMN vfrom date, ADD COLUMN vto date,
        ADD COLUMN n_burn int;

    UPDATE _hl_pass SET pdate = CASE
        WHEN r_when < 0.06 THEN make_date(v_y - 1, 7, 24) + floor(r_span * 39)::int   -- late prev-season buys
        WHEN r_when < 0.22 THEN make_date(v_y - 1, 11, 24) + floor(r_span * 9)::int   -- Black Friday week
        WHEN r_when < 0.28 THEN make_date(v_y, 1, 5)  + floor(r_span * 55)::int       -- Jan-Feb trickle
        WHEN r_when < 0.42 THEN make_date(v_y, 3, 1)  + floor(r_span * 31)::int       -- March
        WHEN r_when < 0.68 THEN make_date(v_y, 4, 1)  + floor(r_span * 30)::int       -- April (peak)
        WHEN r_when < 0.86 THEN make_date(v_y, 5, 1)  + floor(r_span * 31)::int       -- May
        WHEN r_when < 0.96 THEN make_date(v_y, 6, 1)  + floor(r_span * 30)::int       -- June
        ELSE                    make_date(v_y, 7, 1)  + floor(r_span * 19)::int       -- July tail
    END;
    DELETE FROM _hl_pass WHERE pdate < v_start OR pdate > v_end;

    UPDATE _hl_pass SET
        product_id = CASE WHEN r_prod < 0.55 THEN v_sp_unlim WHEN r_prod < 0.71 THEN v_sp_teen
                          WHEN r_prod < 0.85 THEN v_sp_wed ELSE v_sp_3ride END,
        amount     = CASE WHEN r_prod < 0.55 THEN 66900 WHEN r_prod < 0.71 THEN 51900
                          WHEN r_prod < 0.85 THEN 15900 ELSE 21900 END,
        vfrom      = CASE WHEN r_when < 0.06 THEN pdate ELSE GREATEST(pdate, make_date(v_y, 4, 23)) END,
        vto        = CASE WHEN r_when < 0.06 THEN make_date(v_y - 1, 11, 16) ELSE make_date(v_y, 11, 15) END;
    -- 3 Ride Pass consumption: most packs have used some rides by now (weighted 0-3);
    -- passes whose season already ended read as mostly used up.
    UPDATE _hl_pass SET n_burn =
        CASE WHEN product_id <> v_sp_3ride THEN NULL
             WHEN vto < CURRENT_DATE THEN CASE WHEN r_burn < 0.30 THEN 2 ELSE 3 END
             WHEN r_burn < 0.25 THEN 0 WHEN r_burn < 0.55 THEN 1
             WHEN r_burn < 0.80 THEN 2 ELSE 3 END;

    INSERT INTO users (tenant_id, email, password_hash, first_name, last_name, role, roles,
                       status, email_verified, created_at, updated_at)
    SELECT NULL, lower(p.fn || '.' || p.ln || '.p' || p.n || '.hl@highland.test'), v_rider_hash,
           p.fn, p.ln, 'rider', ARRAY['rider'], 'active', true,
           ((p.pdate + TIME '08:00') AT TIME ZONE 'America/New_York') + (p.minute_of_day * INTERVAL '1 minute') - INTERVAL '12 minutes',
           ((p.pdate + TIME '08:00') AT TIME ZONE 'America/New_York') + (p.minute_of_day * INTERVAL '1 minute') - INTERVAL '12 minutes'
    FROM _hl_pass p;

    INSERT INTO season_pass_purchase
        (tenant_id, purchaser_user_id, product_id, amount_cents, payment_method, status,
         purchaser_email, purchaser_name, holder_first_name, holder_last_name,
         valid_from_date, valid_to_date, credits_remaining, created_at, updated_at)
    SELECT v_tenant_id, u.id, p.product_id, p.amount,
           CASE WHEN p.r_pay < 0.92 THEN 'stripe' ELSE 'cash' END, 'paid',
           u.email, p.fn || ' ' || p.ln,
           CASE WHEN p.product_id = v_sp_teen THEN v_first[1 + ((p.n * 7) % 32)] ELSE p.fn END,
           p.ln,
           p.vfrom, p.vto,
           CASE WHEN p.product_id = v_sp_3ride THEN 3 - COALESCE(p.n_burn, 0) END,
           ((p.pdate + TIME '08:00') AT TIME ZONE 'America/New_York') + (p.minute_of_day * INTERVAL '1 minute'),
           ((p.pdate + TIME '08:00') AT TIME ZONE 'America/New_York') + (p.minute_of_day * INTERVAL '1 minute')
    FROM _hl_pass p
    JOIN users u ON u.email = lower(p.fn || '.' || p.ln || '.p' || p.n || '.hl@highland.test');

    -- Burned rides: each consumed 3 Ride Pass credit is a checked-in reservation on a
    -- distinct past Open Riding day between purchase and season end (UNIQUE(pass, event)
    -- is satisfied by the DISTINCT event pick). Walk-up shaped: checked in mid-morning.
    INSERT INTO season_pass_reservation
        (season_pass_purchase_id, event_id, status, reserved_at, checked_in_at)
    SELECT x.pass_id, x.event_id, 'checked_in', x.checked_in_at, x.checked_in_at
    FROM (
        SELECT spp.id AS pass_id, e.id AS event_id,
               e.starts_at + INTERVAL '95 minutes' AS checked_in_at,
               row_number() OVER (PARTITION BY spp.id ORDER BY random()) AS rn,
               p.n_burn
        FROM _hl_pass p
        JOIN users u ON u.email = lower(p.fn || '.' || p.ln || '.p' || p.n || '.hl@highland.test')
        JOIN season_pass_purchase spp ON spp.purchaser_user_id = u.id AND spp.tenant_id = v_tenant_id
        JOIN event e ON e.tenant_id = v_tenant_id AND e.title = 'Open Riding'
                    AND (e.starts_at AT TIME ZONE 'America/New_York')::date
                        BETWEEN GREATEST(p.pdate, p.vfrom) AND LEAST(p.vto, v_end)
        WHERE p.product_id = v_sp_3ride AND COALESCE(p.n_burn, 0) > 0
    ) x
    WHERE x.rn <= x.n_burn;

    -- Late-season buyers can have fewer eligible event days than their rolled burn count;
    -- true up so credits_remaining always equals 3 minus the check-ins that actually exist.
    UPDATE season_pass_purchase spp
    SET credits_remaining = 3 - (SELECT count(*) FROM season_pass_reservation r
                                  WHERE r.season_pass_purchase_id = spp.id AND r.status = 'checked_in')
    WHERE spp.tenant_id = v_tenant_id AND spp.product_id = v_sp_3ride
      AND spp.purchaser_email LIKE '%.hl@highland.test';

    -- ══════════════════════════════════════════════════════════════════════
    -- FOOD & BEVERAGE: order templates built from the seeded menu, scaled to
    -- each day's gate revenue (the pub does ~1/3 of gate, per the mix target).
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_fnb_tpl;
    CREATE TEMP TABLE _hl_fnb_tpl (tid int, total int);
    INSERT INTO _hl_fnb_tpl VALUES (1,2050),(2,2400),(3,1900),(4,4100),(5,1600),(6,750),(7,2400),(8,1850),(9,1350),(10,2500);

    DROP TABLE IF EXISTS _hl_fnb_tpl_line;
    CREATE TEMP TABLE _hl_fnb_tpl_line (tid int, product text, qty int, unit int);
    INSERT INTO _hl_fnb_tpl_line VALUES
        (1,'Smash Burger',1,1200),(1,'Fries',1,500),(1,'Fountain Drink',1,350),
        (2,'Pepperoni Pizza',1,1800),(2,'Canned Soda',2,300),
        (3,'Chicken Tenders',1,1000),(3,'Pretzel',1,600),(3,'Canned Soda',1,300),
        (4,'Smash Burger',2,1200),(4,'Fries',2,500),(4,'Fountain Drink',2,350),
        (5,'Hellion IPA (16oz)',2,800),
        (6,'Coffee',1,400),(6,'Energy Bar',1,350),
        (7,'Craft Cheese Pizza',1,1600),(7,'Hellion IPA (16oz)',1,800),
        (8,'Vegan Burger',1,1100),(8,'Fries',1,500),(8,'Bottled Water',1,250),
        (9,'Chicken Tenders',1,1000),(9,'Fountain Drink',1,350),
        (10,'Smash Burger',1,1200),(10,'Hellion IPA (16oz)',1,800),(10,'Fries',1,500);

    IF NOT EXISTS (SELECT 1 FROM concession_product WHERE tenant_id = v_tenant_id AND name = 'Smash Burger') THEN
        RAISE EXCEPTION 'concession menu missing - run the concessions fragment first';
    END IF;

    DROP TABLE IF EXISTS _hl_fnb_o;
    CREATE TEMP TABLE _hl_fnb_o ON COMMIT DROP AS
    SELECT gen_random_uuid() AS id,
           o.day, o.tid, t.total,
           ((o.day + TIME '11:00') AT TIME ZONE 'America/New_York') + (floor(random() * 330) * INTERVAL '1 minute') AS created_at,
           (random() < 0.30) AS is_cash,
           (random() < 0.20) AS is_online,
           v_first[1 + floor(random() * 32)::int] AS fn,
           v_last [1 + floor(random() * 32)::int] AS ln,
           o.g
    FROM (
        SELECT d.day, g.g,
               -- Sequential independent draws; thresholds are conditional
               -- probabilities engineered to yield marginal template weights
               -- .18/.10/.12/.12/.08/.08/.08/.08/.08/.08 (avg ticket $21.65).
               CASE WHEN random() < 0.18 THEN 1 WHEN random() < 0.122 THEN 2 WHEN random() < 0.167 THEN 3
                    WHEN random() < 0.20 THEN 4 WHEN random() < 0.167 THEN 5 WHEN random() < 0.20 THEN 6
                    WHEN random() < 0.25 THEN 7 WHEN random() < 0.333 THEN 8 WHEN random() < 0.50 THEN 9
                    ELSE 10 END AS tid
        FROM _hl_day d
        CROSS JOIN LATERAL generate_series(1, GREATEST(round(d.fnb_target / 2165.0)::int, 0)) g(g)
        WHERE d.lift
    ) o
    JOIN _hl_fnb_tpl t ON t.tid = o.tid;

    INSERT INTO concession_sale
        (id, tenant_id, status, subtotal_cents, total_cents, order_number,
         fulfillment_status, payment_method, order_channel,
         purchaser_email, purchaser_name,
         created_at, paid_at, completed_at)
    SELECT o.id, v_tenant_id, 'paid', o.total, o.total,
           row_number() OVER (PARTITION BY (o.created_at AT TIME ZONE 'UTC')::date ORDER BY o.created_at),
           'completed',
           CASE WHEN o.is_cash THEN 'cash' ELSE 'stripe' END,
           CASE WHEN o.is_online THEN 'online' ELSE 'counter' END,
           CASE WHEN o.is_online THEN lower(o.fn || '.' || o.ln || '.f' || o.g || '.hl@highland.test') END,
           CASE WHEN o.is_online THEN o.fn || ' ' || o.ln END,
           o.created_at, o.created_at, o.created_at + INTERVAL '7 minutes'
    FROM _hl_fnb_o o;

    INSERT INTO concession_sale_line
        (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents, prep_status)
    SELECT o.id, cp.id, l.product, l.unit, l.qty, l.unit * l.qty, 'ready'
    FROM _hl_fnb_o o
    JOIN _hl_fnb_tpl_line l ON l.tid = o.tid
    JOIN concession_product cp ON cp.tenant_id = v_tenant_id AND cp.name = l.product;

    -- Keep the per-day order-number counter ahead of everything seeded.
    INSERT INTO concession_order_counter (tenant_id, business_date, last_number)
    SELECT v_tenant_id, (s.created_at AT TIME ZONE 'UTC')::date, MAX(s.order_number)
    FROM concession_sale s
    WHERE s.tenant_id = v_tenant_id AND s.order_number IS NOT NULL
    GROUP BY (s.created_at AT TIME ZONE 'UTC')::date
    ON CONFLICT (tenant_id, business_date)
        DO UPDATE SET last_number = GREATEST(concession_order_counter.last_number, EXCLUDED.last_number);

    -- ══════════════════════════════════════════════════════════════════════
    -- BIKE SHOP RENTALS: fleet mix at real daily rates ($150 V10 / $130 Reign
    -- and Kids 24"), ~11% of the day's riders.
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_rent;
    CREATE TEMP TABLE _hl_rent ON COMMIT DROP AS
    SELECT gen_random_uuid() AS id,
           r.day, r.g,
           CASE WHEN r.pick < 0.11 THEN 'HL-DH29-M'   WHEN r.pick < 0.22 THEN 'HL-DH29-L'
                WHEN r.pick < 0.40 THEN 'HL-TR275-S'  WHEN r.pick < 0.59 THEN 'HL-TR275-M'
                WHEN r.pick < 0.77 THEN 'HL-TR275-L'  ELSE 'HL-KIDS24-STD' END AS sku,
           (random() < 0.40) AS is_cash,
           v_first[1 + floor(random() * 32)::int] AS fn,
           v_last [1 + floor(random() * 32)::int] AS ln,
           ((r.day + TIME '09:00') AT TIME ZONE 'America/New_York') + (floor(random() * 120) * INTERVAL '1 minute') AS starts_at
    FROM (
        SELECT d.day, g.g, random() AS pick
        FROM _hl_day d
        CROSS JOIN LATERAL generate_series(1, GREATEST(d.n_rent, 0)) g(g)
        WHERE d.lift
    ) r;

    INSERT INTO shop_rental
        (id, tenant_id, renter_name, renter_email, renter_phone, starts_at, ends_at, status,
         amount_cents, tax_cents, total_cents, deposit_cents, payment_method,
         checked_out_at, returned_at, condition_notes, created_at, updated_at)
    SELECT r.id, v_tenant_id, r.fn || ' ' || r.ln,
           lower(r.fn || '.' || r.ln || '.b' || r.g || '.hl@highland.test'),
           '603-555-0' || lpad((100 + (r.g % 800))::text, 3, '0'),
           r.starts_at, r.starts_at + INTERVAL '7 hours', 'returned',
           v.daily_rate_cents, 0, v.daily_rate_cents, v.deposit_cents,
           CASE WHEN r.is_cash THEN 'cash' ELSE 'stripe' END,
           r.starts_at, r.starts_at + INTERVAL '7 hours', 'Returned, normal wear.',
           r.starts_at, r.starts_at
    FROM _hl_rent r
    JOIN shop_variant v ON v.tenant_id = v_tenant_id AND v.sku = r.sku;

    INSERT INTO shop_rental_line
        (rental_id, variant_id, item_id, quantity, name_snapshot, variant_label,
         daily_rate_cents_frozen, deposit_cents_frozen, line_amount_cents)
    SELECT r.id, v.id, NULL, 1, p.name, v.size, v.daily_rate_cents, v.deposit_cents, v.daily_rate_cents
    FROM _hl_rent r
    JOIN shop_variant v ON v.tenant_id = v_tenant_id AND v.sku = r.sku
    JOIN shop_product p ON p.id = v.product_id;

    -- ══════════════════════════════════════════════════════════════════════
    -- BIKE SHOP RETAIL: counter sales off the seeded retail catalog.
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_sale;
    CREATE TEMP TABLE _hl_sale ON COMMIT DROP AS
    SELECT gen_random_uuid() AS id,
           s.day,
           CASE WHEN s.pick < 0.40 THEN 'HL-TUBE-2729' WHEN s.pick < 0.65 THEN 'HL-GLOVES-STD'
                WHEN s.pick < 0.85 THEN 'HL-GRIPS-LOCKON' ELSE 'HL-HELMET-FF' END AS sku,
           (random() < 0.15) AS add_tube,
           (random() < 0.45) AS is_cash,
           ((s.day + TIME '10:00') AT TIME ZONE 'America/New_York') + (floor(random() * 420) * INTERVAL '1 minute') AS created_at
    FROM (
        SELECT d.day, g.g, random() AS pick
        FROM _hl_day d
        CROSS JOIN LATERAL generate_series(1, GREATEST(d.n_retail, 0)) g(g)
        WHERE d.lift
    ) s;

    INSERT INTO shop_sale
        (id, tenant_id, status, subtotal_cents, total_cents, payment_method, order_channel,
         created_at, updated_at)
    SELECT s.id, v_tenant_id, 'paid',
           v.sale_price_cents + CASE WHEN s.add_tube AND s.sku <> 'HL-TUBE-2729' THEN 1200 ELSE 0 END,
           v.sale_price_cents + CASE WHEN s.add_tube AND s.sku <> 'HL-TUBE-2729' THEN 1200 ELSE 0 END,
           CASE WHEN s.is_cash THEN 'cash' ELSE 'stripe' END, 'counter',
           s.created_at, s.created_at
    FROM _hl_sale s
    JOIN shop_variant v ON v.tenant_id = v_tenant_id AND v.sku = s.sku;

    INSERT INTO shop_sale_line
        (sale_id, variant_id, item_id, quantity, name_snapshot, variant_label,
         unit_price_cents, unit_cost_cents_frozen, created_at)
    SELECT s.id, v.id, NULL::uuid, 1, p.name, v.size, v.sale_price_cents, v.cost_cents, s.created_at
    FROM _hl_sale s
    JOIN shop_variant v ON v.tenant_id = v_tenant_id AND v.sku = s.sku
    JOIN shop_product p ON p.id = v.product_id
    UNION ALL
    SELECT s.id, v.id, NULL::uuid, 1, p.name, v.size, v.sale_price_cents, v.cost_cents, s.created_at
    FROM _hl_sale s
    JOIN shop_variant v ON v.tenant_id = v_tenant_id AND v.sku = 'HL-TUBE-2729'
    JOIN shop_product p ON p.id = v.product_id
    WHERE s.add_tube AND s.sku <> 'HL-TUBE-2729';

    -- ══════════════════════════════════════════════════════════════════════
    -- Totals check (trailing 365 days, revenue statuses only)
    -- ══════════════════════════════════════════════════════════════════════
    SELECT COALESCE(SUM(amount_cents), 0) INTO v_sum_tickets
      FROM event_ticket_purchase WHERE tenant_id = v_tenant_id
       AND status IN ('paid','redeemed') AND created_at >= now() - INTERVAL '365 days';
    SELECT COALESCE(SUM(amount_cents), 0) INTO v_sum_passes
      FROM season_pass_purchase WHERE tenant_id = v_tenant_id
       AND status = 'paid' AND created_at >= now() - INTERVAL '365 days';
    SELECT COALESCE((SELECT SUM(total_cents) FROM shop_rental WHERE tenant_id = v_tenant_id
                      AND status IN ('paid','out','returned') AND created_at >= now() - INTERVAL '365 days'), 0)
         + COALESCE((SELECT SUM(total_cents) FROM shop_sale WHERE tenant_id = v_tenant_id
                      AND status = 'paid' AND created_at >= now() - INTERVAL '365 days'), 0)
      INTO v_sum_shop;
    SELECT COALESCE(SUM(total_cents), 0) INTO v_sum_fnb
      FROM concession_sale WHERE tenant_id = v_tenant_id
       AND status = 'paid' AND created_at >= now() - INTERVAL '365 days';

    RAISE NOTICE 'Highland year-of-sales seed complete:';
    RAISE NOTICE '  tickets/camps/clinics: $%', to_char(v_sum_tickets / 100.0, 'FM999,999,990.00');
    RAISE NOTICE '  season passes:         $%', to_char(v_sum_passes  / 100.0, 'FM999,999,990.00');
    RAISE NOTICE '  bike shop + rentals:   $%', to_char(v_sum_shop    / 100.0, 'FM999,999,990.00');
    RAISE NOTICE '  food & beverage:       $%', to_char(v_sum_fnb     / 100.0, 'FM999,999,990.00');
    RAISE NOTICE '  GRAND TOTAL:           $%', to_char((v_sum_tickets + v_sum_passes + v_sum_shop + v_sum_fnb) / 100.0, 'FM999,999,990.00');
END $hl_sales_year$;


-- ============================================================================
-- Highland Bike Park -- TOMORROW (demo day): a busy Friday with advance sales.
-- Creates a Friday Open Riding day + a Friday Skills Clinic for tomorrow, loads
-- them with advance ticket sales (still 'paid' -- they check in at the gate),
-- books season-pass reservations for tomorrow, tops up the base fragment's
-- upcoming Saturday event with advance sales, and books tomorrow rentals.
-- The waiver fragment below then signs ~everyone (a couple of deterministic
-- holdouts stay unsigned so Compliance Today has real "Missing" rows).
-- Rerunnable: wipes its own events/purchases/reservations/rentals by title +
-- email-marker scope.
-- ============================================================================

DO $hl_upcoming$
DECLARE
    v_tenant_id  uuid;
    v_open_ride  uuid;
    v_lesson     uuid;
    v_instr_sam  uuid;
    v_instr_jo   uuid;
    v_tomorrow   date;
    v_evt_fri    uuid;
    v_evt_clinic uuid;
    v_evt_sat    uuid;
    v_first constant text[] := ARRAY['Avery','Blake','Casey','Devon','Emerson','Finley','Gray','Harper',
        'Indie','Jules','Kai','Logan','Marlow','Nico','Oakley','Parker','Quinn','Reese','Sawyer','Tatum',
        'Uma','Vaughn','Wren','Xavier','Yara','Zane','Micah','Lena','Theo','Sasha','Colby','Dana'];
    v_last constant text[] := ARRAY['Abbott','Barnes','Cortez','Dalton','Ellison','Fleming','Garner','Hayes',
        'Ibarra','Jennings','Keller','Lawson','Merritt','Nolan','Osborne','Pratt','Quimby','Rowe','Sutton','Tran',
        'Underwood','Vasquez','Whitaker','Xu','York','Zimmerman','Calloway','Drummond','Eastman','Forsythe','Granger','Holloway'];
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN RAISE EXCEPTION 'tenant "highland" not found'; END IF;
    SELECT id INTO v_open_ride FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'open_ride';
    SELECT id INTO v_lesson    FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'lesson';
    SELECT id INTO v_instr_sam FROM instructor WHERE tenant_id = v_tenant_id AND email = 'sam.instructor@highland.test';
    SELECT id INTO v_instr_jo  FROM instructor WHERE tenant_id = v_tenant_id AND email = 'jo.coach@highland.test';
    IF v_instr_sam IS NULL OR v_instr_jo IS NULL THEN
        RAISE EXCEPTION 'seed instructors missing - run the lessons fragment first';
    END IF;

    PERFORM setseed(0.9);
    v_tomorrow := (now() AT TIME ZONE 'America/New_York')::date + 1;

    -- ── Wipe (children first) ───────────────────────────────────────────────
    DELETE FROM event_ticket_purchase
     WHERE tenant_id = v_tenant_id
       AND (purchaser_email LIKE '%.adv.hl@highland.test'
            OR tier_id IN (SELECT tt.id FROM event_ticket_tier tt JOIN event e ON e.id = tt.event_id
                            WHERE e.tenant_id = v_tenant_id
                              AND e.title IN ('Friday Open Riding', 'Friday Skills Clinic')));
    DELETE FROM season_pass_reservation r
     USING season_pass_purchase sp, event e
     WHERE r.season_pass_purchase_id = sp.id AND sp.tenant_id = v_tenant_id
       AND r.event_id = e.id
       AND (e.title IN ('Friday Open Riding', 'Friday Skills Clinic')
            OR (r.status = 'reserved' AND e.starts_at > now() AND sp.purchaser_email LIKE '%.hl@highland.test'));
    DELETE FROM event WHERE tenant_id = v_tenant_id AND title IN ('Friday Open Riding', 'Friday Skills Clinic');
    DELETE FROM shop_rental_line WHERE rental_id IN (
        SELECT id FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email LIKE '%.adv.hl@highland.test');
    DELETE FROM shop_rental WHERE tenant_id = v_tenant_id AND renter_email LIKE '%.adv.hl@highland.test';

    -- ── Tomorrow's Open Riding day (Friday = midweek pricing) ───────────────
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (v_tenant_id, v_open_ride, 'Friday Open Riding',
                'Lift-served trails open, all skill levels. Full-day, happy-hour, and junior tickets.',
                (v_tomorrow + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_tomorrow + TIME '17:00') AT TIME ZONE 'America/New_York',
                false, 700, 'Lift Base Area', 'scheduled')
        RETURNING id INTO v_evt_fri;
    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt_fri, 'Full Day Lift Ticket',          6800, 500, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_fri, 'Junior Lift Ticket (7-14)',     3400, 200, 20, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_fri, 'Happy Hour Ticket (2pm-Close)', 4500, 300, 30, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt_fri, 'Junior Happy Hour (7-14)',      2500, 150, 40, 'gate_fee', 'rider');

    -- ── Tomorrow's skills clinic (lesson type, instructor groups + private) ─
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (v_tenant_id, v_lesson, 'Friday Skills Clinic',
                'Coached group sessions split by ability, plus bookable private lessons.',
                (v_tomorrow + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_tomorrow + TIME '14:00') AT TIME ZONE 'America/New_York',
                false, 20, 'Skills Zone / Progression Park', 'scheduled')
        RETURNING id INTO v_evt_clinic;
    INSERT INTO event_instructor (event_id, instructor_id) VALUES
        (v_evt_clinic, v_instr_sam), (v_evt_clinic, v_instr_jo);
    INSERT INTO event_ticket_tier
        (tenant_id, event_id, name, price_cents, inventory, sort_order, is_active,
         instructor_id, skill_level, equipment_label, starts_at, ends_at, audience)
    VALUES
        (v_tenant_id, v_evt_clinic, 'Beginner Group (Green Circle)', 14900, 8, 10, true,
         v_instr_sam, 'Green Circle', 'Trail',
         (v_tomorrow + TIME '09:00') AT TIME ZONE 'America/New_York',
         (v_tomorrow + TIME '11:00') AT TIME ZONE 'America/New_York', 'rider'),
        (v_tenant_id, v_evt_clinic, 'Intermediate Group (Blue Square)', 15900, 6, 20, true,
         v_instr_jo, 'Blue Square', 'Downhill',
         (v_tomorrow + TIME '11:30') AT TIME ZONE 'America/New_York',
         (v_tomorrow + TIME '13:30') AT TIME ZONE 'America/New_York', 'rider'),
        (v_tenant_id, v_evt_clinic, 'Private Lesson (2hr)', 21900, 3, 30, true,
         NULL, NULL, NULL,
         (v_tomorrow + TIME '09:00') AT TIME ZONE 'America/New_York',
         (v_tomorrow + TIME '11:00') AT TIME ZONE 'America/New_York', 'rider');

    -- The base fragment's upcoming Saturday event gets advance sales too.
    SELECT id INTO v_evt_sat FROM event
     WHERE tenant_id = v_tenant_id AND title = 'Saturday Open Riding' AND starts_at > now()
     ORDER BY starts_at LIMIT 1;

    -- ── Advance ticket sales (still 'paid': they check in at the gate) ──────
    -- Friday: ~85 across the four tiers. Saturday: ~130 (weekend). Clinic: 13.
    -- Two deterministic no-waiver holdouts land on tomorrow so Compliance Today
    -- always has real Missing rows (the waiver fragment skips 'nowaiver%').
    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at)
    SELECT v_tenant_id, x.tier_id, x.price_cents, 'paid',
           lower(x.fn || '.' || x.ln || '.' || x.g || '.adv.hl@highland.test'),
           x.fn || ' ' || x.ln, 'stripe', x.created_at, x.created_at
    FROM (
        SELECT tt.id AS tier_id, tt.price_cents, g.g,
               v_first[1 + floor(random() * 32)::int] AS fn,
               v_last [1 + floor(random() * 32)::int] AS ln,
               now() - (1 + floor(random() * 9)) * INTERVAL '1 day'
                     + (floor(random() * 700) * INTERVAL '1 minute') AS created_at
        FROM event_ticket_tier tt
        CROSS JOIN LATERAL generate_series(1,
            CASE
                WHEN tt.event_id = v_evt_fri AND tt.name = 'Full Day Lift Ticket'          THEN 45
                WHEN tt.event_id = v_evt_fri AND tt.name = 'Junior Lift Ticket (7-14)'     THEN 12
                WHEN tt.event_id = v_evt_fri AND tt.name = 'Happy Hour Ticket (2pm-Close)' THEN 20
                WHEN tt.event_id = v_evt_fri AND tt.name = 'Junior Happy Hour (7-14)'      THEN 8
                WHEN tt.event_id = v_evt_sat AND tt.name = 'Full Day Lift Ticket'          THEN 85
                WHEN tt.event_id = v_evt_sat AND tt.name = 'Junior Lift Ticket (7-14)'     THEN 22
                WHEN tt.event_id = v_evt_sat AND tt.name = 'Happy Hour Ticket (2pm-Close)' THEN 18
                WHEN tt.event_id = v_evt_sat AND tt.name = 'Non-Riding Spectator Gate'     THEN 8
                WHEN tt.event_id = v_evt_clinic AND tt.sort_order = 10                     THEN 6
                WHEN tt.event_id = v_evt_clinic AND tt.sort_order = 20                     THEN 5
                WHEN tt.event_id = v_evt_clinic AND tt.sort_order = 30                     THEN 2
                ELSE 0
            END) g(g)
        WHERE tt.tenant_id = v_tenant_id
          AND tt.event_id IN (v_evt_fri, v_evt_clinic, v_evt_sat)
    ) x;

    -- Deterministic Missing-waiver demo rows on tomorrow's events.
    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at)
    VALUES
        (v_tenant_id,
         (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_fri AND name = 'Full Day Lift Ticket'),
         6800, 'paid', 'nowaiver.dana.frost.adv.hl@highland.test', 'Dana Frost', 'stripe',
         now() - INTERVAL '2 days', now() - INTERVAL '2 days'),
        (v_tenant_id,
         (SELECT id FROM event_ticket_tier WHERE event_id = v_evt_clinic AND sort_order = 10),
         14900, 'paid', 'nowaiver.remy.calder.adv.hl@highland.test', 'Remy Calder', 'stripe',
         now() - INTERVAL '1 day', now() - INTERVAL '1 day');

    -- ── Season-pass reservations for tomorrow (unlimited/teen passes only:
    --    credits passes redeem walk-up at the gate, which is the live demo) ──
    INSERT INTO season_pass_reservation (season_pass_purchase_id, event_id, status, reserved_at)
    SELECT sp.id, v_evt_fri, 'reserved', now() - (floor(random() * 5) + 1) * INTERVAL '1 day'
    FROM season_pass_purchase sp
    JOIN season_pass_product p ON p.id = sp.product_id
    WHERE sp.tenant_id = v_tenant_id AND sp.status = 'paid'
      AND p.kind = 'unlimited'
      AND sp.valid_to_date >= v_tomorrow
      AND sp.purchaser_email LIKE '%.hl@highland.test'
    ORDER BY random()
    LIMIT 22;

    -- ── Rentals booked for tomorrow (paid, not yet picked up) ───────────────
    INSERT INTO shop_rental
        (tenant_id, renter_name, renter_email, renter_phone, starts_at, ends_at, status,
         amount_cents, tax_cents, total_cents, deposit_cents, payment_method, created_at, updated_at)
    SELECT v_tenant_id, x.fn || ' ' || x.ln,
           lower(x.fn || '.' || x.ln || '.r' || x.g || '.adv.hl@highland.test'),
           '603-555-0' || lpad((300 + x.g)::text, 3, '0'),
           (v_tomorrow + TIME '09:00') AT TIME ZONE 'America/New_York' + (x.g * INTERVAL '20 minutes'),
           (v_tomorrow + TIME '17:00') AT TIME ZONE 'America/New_York',
           'paid',
           v.daily_rate_cents, 0, v.daily_rate_cents, v.deposit_cents, 'stripe',
           now() - (x.g * INTERVAL '10 hours'), now() - (x.g * INTERVAL '10 hours')
    FROM (
        SELECT g.g,
               v_first[1 + (g.g * 5) % 32] AS fn,
               v_last [1 + (g.g * 11) % 32] AS ln,
               (ARRAY['HL-DH29-M','HL-TR275-S','HL-TR275-M','HL-TR275-L','HL-KIDS24-STD','HL-DH29-L'])[g.g] AS sku
        FROM generate_series(1, 6) g(g)
    ) x
    JOIN shop_variant v ON v.tenant_id = v_tenant_id AND v.sku = x.sku;

    INSERT INTO shop_rental_line
        (rental_id, variant_id, item_id, quantity, name_snapshot, variant_label,
         daily_rate_cents_frozen, deposit_cents_frozen, line_amount_cents)
    SELECT r.id, v.id, NULL, 1, p.name, v.size, v.daily_rate_cents, v.deposit_cents, v.daily_rate_cents
    FROM shop_rental r
    JOIN shop_variant v ON v.tenant_id = v_tenant_id
        AND v.sku = (ARRAY['HL-DH29-M','HL-TR275-S','HL-TR275-M','HL-TR275-L','HL-KIDS24-STD','HL-DH29-L'])
                    [CAST(substring(r.renter_email FROM '\.r(\d+)\.adv') AS int)]
    JOIN shop_product p ON p.id = v.product_id
    WHERE r.tenant_id = v_tenant_id AND r.renter_email LIKE '%.adv.hl@highland.test';

    RAISE NOTICE 'Highland tomorrow-demo seed: Friday Open Riding + Skills Clinic on %, advance tickets, % pass reservations, rentals booked',
        v_tomorrow, (SELECT count(*) FROM season_pass_reservation sr JOIN season_pass_purchase sp2 ON sp2.id = sr.season_pass_purchase_id
                     WHERE sr.event_id = v_evt_fri AND sp2.tenant_id = v_tenant_id);
END $hl_upcoming$;


-- ============================================================================
-- Highland Bike Park -- WAIVERS: real release text, per-event requirement, and
-- signatures wired onto the seeded passes + tickets.
--
-- What this creates:
--   * Real waiver content on the tenant's active tenant_waiver row.
--   * requires_rider_waiver = true on every event (races/camps/clinics/open ride).
--   * ONE rider_waiver_signature per registrant (the app's own model: checkout
--     writes one row per person and links it from each of their tickets), with a
--     generated cursive-SVG "drawn" signature image. Junior-tier and camp riders
--     are minors signed by a parent (different rider first name, parent named).
--   * ~96% of rider-audience tickets registered + signed (names, birthdates,
--     waiver links, registration_complete); the rest left unsigned so the
--     roster's missing-waiver alarm has something real to show.
--   * Every pass purchase fully registered: signature + a generated initials-
--     avatar photo, so the gate scanner's photo check and IsRegistered pass.
--
-- Rerunnable: clears all waiver linkage + signatures for this tenant (every
-- signature on the demo tenant is seed-owned) and rebuilds. Runs LAST so it can
-- cover both the base fragments' demo purchases and the year of history.
-- ============================================================================

DO $hl_waivers$
DECLARE
    v_tenant_id uuid;
    v_waiver_id uuid;
    -- Alternate first names for minor riders (the purchaser is the parent).
    v_kid constant text[] := ARRAY['Riley','Rowan','Milo','Piper','Ellis','June','Cass','Arlo',
        'Wren','Remy','Sage','Teddy','Nova','Beck','Lila','Otis'];
    v_colors constant text[] := ARRAY['steelblue','indianred','seagreen','peru',
        'slateblue','teal','maroon','darkslategray'];
    v_sigs int; v_reg_tix int; v_unreg_tix int; v_reg_pass int;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found - create it first';
    END IF;

    -- ── Waiver content (the tenant-creation trigger seeds an empty active row) ──
    SELECT id INTO v_waiver_id FROM tenant_waiver
     WHERE tenant_id = v_tenant_id AND is_active ORDER BY version DESC LIMIT 1;
    IF v_waiver_id IS NULL THEN
        INSERT INTO tenant_waiver (tenant_id, version, name, title, body)
            VALUES (v_tenant_id, 1, 'Rider Release', 'Release & Waiver of Liability', '')
            RETURNING id INTO v_waiver_id;
    END IF;
    UPDATE tenant_waiver SET
        name  = 'Rider Release & Waiver of Liability',
        title = 'Highland Bike Park Release & Waiver of Liability',
        body  = $body$ASSUMPTION OF RISK. Mountain biking, lift-served downhill riding, and the use of jumps, drops, and other trail features are HAZARDOUS ACTIVITIES that carry a risk of serious injury, paralysis, or death. Trail conditions change with weather and use. By signing, I acknowledge that I understand and freely accept these risks for myself or for the minor named below.

RELEASE. In consideration of being permitted to ride at Highland Bike Park, I release and hold harmless Highland Bike Park, its owners, employees, and volunteers from any and all claims arising out of my participation, including claims of ordinary negligence, to the fullest extent permitted by law.

RULES AND EQUIPMENT. I agree to obey posted trail signage and staff instructions, to ride trails within my ability, and to wear a helmet at all times while riding. Full-face helmets are required in the lift-served bike park.

MEDICAL. I authorize Highland Bike Park staff to secure emergency medical treatment if needed, at my expense.

MINORS. If signing for a rider under 18, I certify that I am the rider's parent or legal guardian and that I make this agreement on the minor's behalf.

This release remains in effect for the full season in which it is signed.$body$
    WHERE id = v_waiver_id;

    -- ── Every event needs the rider waiver (spectators stay waiver-free) ─────
    UPDATE event SET requires_rider_waiver = true WHERE tenant_id = v_tenant_id;

    -- ── Wipe prior seed signatures + linkage (all tenant signatures are seed-owned;
    --    ticket/pass FKs are ON DELETE SET NULL, but clear them explicitly so the
    --    rebuild is deterministic; shop_rental_waiver is RESTRICT so it goes first) ──
    DELETE FROM shop_rental_waiver WHERE signature_id IN
        (SELECT id FROM rider_waiver_signature WHERE tenant_id = v_tenant_id);
    UPDATE event_ticket_purchase
       SET waiver_id = NULL, waiver_signed_at = NULL,
           waiver_signature_data_url = NULL, waiver_signature_id = NULL
     WHERE tenant_id = v_tenant_id AND waiver_signature_id IS NOT NULL;
    UPDATE season_pass_purchase SET waiver_signature_id = NULL
     WHERE tenant_id = v_tenant_id AND waiver_signature_id IS NOT NULL;
    DELETE FROM rider_waiver_signature WHERE tenant_id = v_tenant_id;

    PERFORM setseed(0.7);

    -- ══════════════════════════════════════════════════════════════════════
    -- PASSES: one signature + initials-avatar photo per paid pass, so every
    -- pass reads as fully registered at the gate.
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_psig;
    CREATE TEMP TABLE _hl_psig ON COMMIT DROP AS
    SELECT sp.id AS pass_id,
           gen_random_uuid() AS sig_id,
           COALESCE(NULLIF(sp.holder_first_name, ''), split_part(sp.purchaser_name, ' ', 1)) AS fn,
           COALESCE(NULLIF(sp.holder_last_name, ''),  split_part(sp.purchaser_name, ' ', 2)) AS ln,
           sp.purchaser_user_id,
           sp.purchaser_name,
           sp.purchaser_email,
           sp.created_at,
           (p.name = 'Teen All-Access Season Pass') AS is_teen,
           row_number() OVER (ORDER BY sp.id) AS rn
    FROM season_pass_purchase sp
    JOIN season_pass_product p ON p.id = sp.product_id
    WHERE sp.tenant_id = v_tenant_id AND sp.status = 'paid'
      AND lower(sp.purchaser_email) LIKE '%@highland.test';

    INSERT INTO rider_waiver_signature
        (id, tenant_id, user_id, waiver_id, signed_at, ip_address, signature_data_url,
         signed_by_parent, parent_name, signer_name, signer_email,
         spectator_first_name, spectator_last_name, spectator_birthdate)
    SELECT s.sig_id, v_tenant_id, s.purchaser_user_id, v_waiver_id,
           s.created_at + INTERVAL '4 minutes',
           '203.0.113.' || (1 + s.rn % 250),
           'data:image/svg+xml;utf8,' || replace(
               $svg$<svg xmlns='http://www.w3.org/2000/svg' width='320' height='90'><text x='12' y='58' font-family='Segoe Script, Brush Script MT, cursive' font-size='34' font-style='italic' fill='rgb(24,32,68)'>$svg$
               || CASE WHEN s.is_teen THEN s.purchaser_name ELSE s.fn || ' ' || s.ln END
               || $svg$</text></svg>$svg$, ' ', '%20'),
           s.is_teen,
           CASE WHEN s.is_teen THEN s.purchaser_name END,
           CASE WHEN s.is_teen THEN s.purchaser_name ELSE s.fn || ' ' || s.ln END,
           s.purchaser_email,
           s.fn, s.ln,
           CASE WHEN s.is_teen
                THEN CURRENT_DATE - ((4745 + (s.rn * 61) % 1460))::int     -- ages ~13-17
                ELSE CURRENT_DATE - ((6570 + (s.rn * 137) % 12000))::int   -- ages ~18-50
           END
    FROM _hl_psig s;

    UPDATE season_pass_purchase sp SET
        waiver_signature_id = s.sig_id,
        holder_birthdate = COALESCE(sp.holder_birthdate,
            CASE WHEN s.is_teen
                 THEN CURRENT_DATE - ((4745 + (s.rn * 61) % 1460))::int
                 ELSE CURRENT_DATE - ((6570 + (s.rn * 137) % 12000))::int END),
        photo_data_url = 'data:image/svg+xml;utf8,' || replace(
            $svg$<svg xmlns='http://www.w3.org/2000/svg' width='200' height='200'><rect width='200' height='200' fill='$svg$
            || v_colors[1 + s.rn % 8]
            || $svg$'/><text x='100' y='130' font-family='Arial' font-size='82' font-weight='bold' fill='white' text-anchor='middle'>$svg$
            || upper(left(s.fn, 1) || left(s.ln, 1))
            || $svg$</text></svg>$svg$, ' ', '%20')
    FROM _hl_psig s
    WHERE sp.id = s.pass_id;

    -- ══════════════════════════════════════════════════════════════════════
    -- TICKETS: one signature per registrant (distinct purchaser email across
    -- rider-audience tickets), reused by every ticket that person holds -- the
    -- same one-row-per-person model checkout uses. ~4% stay unsigned.
    -- ══════════════════════════════════════════════════════════════════════
    DROP TABLE IF EXISTS _hl_treg;
    CREATE TEMP TABLE _hl_treg ON COMMIT DROP AS
    SELECT lower(etp.purchaser_email) AS email,
           gen_random_uuid() AS sig_id,
           max(etp.purchaser_name) AS pname,
           min(etp.created_at) AS first_at,
           bool_or(tt.name LIKE 'Junior%' OR tt.name LIKE '%Camp%' OR tt.name LIKE 'CIT%') AS is_minor,
           row_number() OVER () AS rn
    FROM event_ticket_purchase etp
    JOIN event_ticket_tier tt ON tt.id = etp.tier_id
    WHERE etp.tenant_id = v_tenant_id
      AND lower(etp.purchaser_email) LIKE '%@highland.test'
      AND (tt.audience IS NULL OR tt.audience = 'rider')
    GROUP BY lower(etp.purchaser_email);

    ALTER TABLE _hl_treg ADD COLUMN signed boolean,
        ADD COLUMN rider_fn text, ADD COLUMN rider_ln text, ADD COLUMN birthdate date;
    UPDATE _hl_treg SET
        -- 'nowaiver%' emails are the deterministic Compliance-Today "Missing" demo rows.
        signed = (random() >= 0.04) AND email NOT LIKE 'nowaiver%',
        rider_fn = CASE WHEN is_minor THEN v_kid[1 + rn % 16] ELSE split_part(pname, ' ', 1) END,
        rider_ln = split_part(pname, ' ', 2),
        birthdate = CASE WHEN is_minor
                         THEN CURRENT_DATE - ((2920 + (rn * 53) % 2190))::int    -- ages ~8-14
                         ELSE CURRENT_DATE - ((6570 + (rn * 131) % 14600))::int  -- ages ~18-58
                    END;

    INSERT INTO rider_waiver_signature
        (id, tenant_id, user_id, waiver_id, signed_at, ip_address, signature_data_url,
         signed_by_parent, parent_name, signer_name, signer_email,
         spectator_first_name, spectator_last_name, spectator_birthdate)
    SELECT r.sig_id, v_tenant_id, NULL, v_waiver_id,
           r.first_at + INTERVAL '2 minutes',
           '198.51.100.' || (1 + r.rn % 250),
           'data:image/svg+xml;utf8,' || replace(
               $svg$<svg xmlns='http://www.w3.org/2000/svg' width='320' height='90'><text x='12' y='58' font-family='Segoe Script, Brush Script MT, cursive' font-size='34' font-style='italic' fill='rgb(24,32,68)'>$svg$
               || CASE WHEN r.is_minor THEN r.pname ELSE r.rider_fn || ' ' || r.rider_ln END
               || $svg$</text></svg>$svg$, ' ', '%20'),
           r.is_minor,
           CASE WHEN r.is_minor THEN r.pname END,
           CASE WHEN r.is_minor THEN r.pname ELSE r.rider_fn || ' ' || r.rider_ln END,
           r.email,
           r.rider_fn, r.rider_ln, r.birthdate
    FROM _hl_treg r
    WHERE r.signed;

    -- Rider tickets: names + birthdate always; waiver linkage + completion when signed.
    UPDATE event_ticket_purchase etp SET
        rider_first_name = r.rider_fn,
        rider_last_name = r.rider_ln,
        rider_birthdate = r.birthdate,
        parent_guardian_name = CASE WHEN r.is_minor THEN r.pname END,
        registration_complete = r.signed,
        waiver_id = CASE WHEN r.signed THEN v_waiver_id END,
        waiver_signed_at = CASE WHEN r.signed THEN etp.created_at + INTERVAL '2 minutes' END,
        waiver_signature_id = CASE WHEN r.signed THEN r.sig_id END
    FROM _hl_treg r, event_ticket_tier tt
    WHERE etp.tenant_id = v_tenant_id
      AND tt.id = etp.tier_id
      AND (tt.audience IS NULL OR tt.audience = 'rider')
      AND lower(etp.purchaser_email) = r.email;

    -- Spectator tickets need no rider waiver: they're complete as sold.
    UPDATE event_ticket_purchase etp SET registration_complete = true
    FROM event_ticket_tier tt
    WHERE etp.tenant_id = v_tenant_id AND tt.id = etp.tier_id
      AND tt.audience = 'spectator'
      AND lower(etp.purchaser_email) LIKE '%@highland.test';

    SELECT count(*) INTO v_sigs FROM rider_waiver_signature WHERE tenant_id = v_tenant_id;
    SELECT count(*) INTO v_reg_pass FROM season_pass_purchase
     WHERE tenant_id = v_tenant_id AND waiver_signature_id IS NOT NULL;
    SELECT count(*) INTO v_reg_tix FROM event_ticket_purchase
     WHERE tenant_id = v_tenant_id AND waiver_signature_id IS NOT NULL;
    SELECT count(*) INTO v_unreg_tix FROM event_ticket_purchase etp
     JOIN event_ticket_tier tt ON tt.id = etp.tier_id
     WHERE etp.tenant_id = v_tenant_id AND NOT etp.registration_complete
       AND (tt.audience IS NULL OR tt.audience = 'rider');

    RAISE NOTICE 'Highland waiver seed: % signatures; % passes registered; % tickets signed; % rider tickets left unsigned',
        v_sigs, v_reg_pass, v_reg_tix, v_unreg_tix;
END $hl_waivers$;

-- ============================================================================
-- Highland Bike Park - F&B menu item images. The image files were uploaded once
-- through the admin API and live on the stage droplet's /uploads disk, so they
-- survive reseeds; this block just re-points the freshly reinserted products at
-- them. Photos are freely licensed (Wikimedia Commons), demo use only.
-- ============================================================================
DO $hl_conc_images$
DECLARE
    v_tenant_id uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found';
    END IF;
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-acd051a6d95b4290ad479a6bc2456b5d.jpg' WHERE tenant_id = v_tenant_id AND name = 'Smash Burger';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-856d5c8feb3f417da827502b9f58c0ec.jpg' WHERE tenant_id = v_tenant_id AND name = 'Chicken Tenders';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-0ba0f19695f048a68b73b18160ddbfb0.jpg' WHERE tenant_id = v_tenant_id AND name = 'Vegan Burger';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-0fc7c206008a4869b7180d13405a533f.jpg' WHERE tenant_id = v_tenant_id AND name = 'Craft Cheese Pizza';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-b266e7788eda4d97b10f5c35c686126e.jpg' WHERE tenant_id = v_tenant_id AND name = 'Pepperoni Pizza';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-e33a1f3fb27742c79971f48fbceab447.jpg' WHERE tenant_id = v_tenant_id AND name = 'Fries';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-40e237fd29dd4a75bcc8ff3c7e355a45.jpg' WHERE tenant_id = v_tenant_id AND name = 'Pretzel';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-b76a80b2b43747dda9352263ceb54079.jpg' WHERE tenant_id = v_tenant_id AND name = 'Energy Bar';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-c4b620203d234c389227a584318a065c.jpg' WHERE tenant_id = v_tenant_id AND name = 'Trail Mix';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-92aee6f126d54cf3a47688e96dac20df.jpg' WHERE tenant_id = v_tenant_id AND name = 'Fountain Drink';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-7e7953af942b4533b2b732309c47e21d.jpg' WHERE tenant_id = v_tenant_id AND name = 'Bottled Water';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-b79bf4e7914541458683b93fc63c4beb.jpg' WHERE tenant_id = v_tenant_id AND name = 'Sports Drink';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-8ec3c6ffa4c34f39a10f7e65f885b301.jpg' WHERE tenant_id = v_tenant_id AND name = 'Canned Soda';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-ca0a99af612d47b08ab188070e39894e.jpg' WHERE tenant_id = v_tenant_id AND name = 'Hellion IPA (16oz)';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-3bb3f95dd97e4c65b12a0b696e8a53c1.jpg' WHERE tenant_id = v_tenant_id AND name = 'Coffee';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-d31f635649bc40d88dbe58d508f3ca9c.jpg' WHERE tenant_id = v_tenant_id AND name = 'Cold Brew';
    UPDATE concession_product SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/concession-dcde31d0afb5488687395c34c205b056.jpg' WHERE tenant_id = v_tenant_id AND name = 'Hot Chocolate';

    -- event images: uploaded once via the admin API (files persist on the droplet);
    -- matched by title pattern so the year-history recurrences all get covered.
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-0841a4d0d3a44697bf5c652a704f417e.jpg' WHERE tenant_id = v_tenant_id AND title LIKE '%Open Riding%';
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-c95666f491004865987bd138cd37853a.jpg' WHERE tenant_id = v_tenant_id AND title LIKE 'Wednesduro%';
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-632d75f1cc2c4915badefd3bcb75eeac.jpg' WHERE tenant_id = v_tenant_id AND title LIKE '%Skills Clinic%';
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-4b3f229509b9483bb8ed7d9fc6a2210d.jpg' WHERE tenant_id = v_tenant_id AND title LIKE 'Women''s Gravity%';
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-b46b16e0da5b41cc9ae771d0a98ef86d.jpg' WHERE tenant_id = v_tenant_id AND title LIKE 'Highland Race Series%';
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-b46b16e0da5b41cc9ae771d0a98ef86d.jpg' WHERE tenant_id = v_tenant_id AND title LIKE 'Dual Slalom%';
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-75346b86e31043ab985d0d96e64a82f5.jpg' WHERE tenant_id = v_tenant_id AND title LIKE 'Ayr Academy%';
    UPDATE event SET image_url = '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-24521fccda7e4b159895bea5b5855639.jpg' WHERE tenant_id = v_tenant_id AND title LIKE 'Summer Ride Camp%';
END $hl_conc_images$;

-- ============================================================================
-- Highland Bike Park - online-ordering settings: order any open day (no event-day
-- gate) and kitchen capacity management on, so the customer order page/widget
-- shows live wait quotes and the at-capacity stop during the demo.
-- ============================================================================
DO $hl_conc_settings$
DECLARE
    v_tenant_id uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found';
    END IF;

    INSERT INTO concession_menu_settings (tenant_id, require_event_day)
    VALUES (v_tenant_id, false)
    ON CONFLICT (tenant_id) DO UPDATE SET require_event_day = false;

    INSERT INTO concession_ordering_capacity
        (tenant_id, capacity_enabled, base_prep_minutes, max_active_orders, show_quote_times, online_paused)
    VALUES (v_tenant_id, true, 10, 25, true, false)
    ON CONFLICT (tenant_id) DO UPDATE SET capacity_enabled = true, base_prep_minutes = 10,
        max_active_orders = 25, show_quote_times = true, online_paused = false;
END $hl_conc_settings$;

-- ============================================================================
-- Highland Bike Park - blog + membership demo content (original demo copy; images
-- are the freely-licensed uploads already on the tenant). Rerunnable via slug wipe.
-- ============================================================================
DO $hl_blog$
DECLARE
    v_tenant_id uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found';
    END IF;

    UPDATE tenant SET
        blog_enabled = true,
        membership_enabled = true,
        membership_name = 'Park Membership',
        membership_price_cents = 4900,
        membership_duration_kind = 'yearly',
        membership_required_for_riders = false,
        membership_required_for_spectators = false
    WHERE id = v_tenant_id;

    DELETE FROM blog_post WHERE tenant_id = v_tenant_id
        AND slug IN ('new-to-the-park-start-here', 'camp-weeks', 'refer-a-friend-camp', 'team-riding-beyond-summer');

    INSERT INTO blog_post (tenant_id, title, slug, excerpt, body_html, main_image_url, status, is_featured, published_at)
    VALUES
    (v_tenant_id,
     'New to the Park? Start Here',
     'new-to-the-park-start-here',
     'First time on a lift-served trail? Here is exactly how to make day one easy.',
     '<p>Your first day at a lift-served bike park is simpler than it looks. Book a Find Your Ride session and we pair you with a coach, a bike, and full protective gear, so all you bring is shoes and water.</p><p>Start on the green flow trails, session the pump track between laps, and let the chairlift do the climbing. Most first-timers are linking berms by lunch.</p><p>Rentals, tickets, and lessons are all bookable online, so the only line you stand in is the lift line.</p>',
     '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-632d75f1cc2c4915badefd3bcb75eeac.jpg',
     'published', true, now() - INTERVAL '3 days'),
    (v_tenant_id,
     'Why Camp Weeks Are Our Favorite Weeks',
     'camp-weeks',
     'Watching a camper clear their first tabletop never gets old.',
     '<p>Every camp week follows the same arc: nervous drop-offs on Monday, unstoppable confidence by Friday. Our coaches build skills in small groups, and the progression parks let riders level up at their own pace.</p><p>Summer Ride Camp covers ages 8 to 13, and Ayr Academy takes teens deeper into technique, trail etiquette, and bike care. Both fill fast; day and overnight options are available.</p>',
     '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-24521fccda7e4b159895bea5b5855639.jpg',
     'published', false, now() - INTERVAL '10 days'),
    (v_tenant_id,
     'Refer a Friend to Camp, Earn Rewards',
     'refer-a-friend-camp',
     'Campers ride better with friends, and this season referrals earn you gear.',
     '<p>New this season: refer a family to any camp session and you both earn rewards, from limited-edition park hoodies to gift cards good anywhere on the mountain.</p><p>There is no cap, so the more friends who ride, the more you earn. Ask at the front desk or mention your referral at registration.</p>',
     '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-75346b86e31043ab985d0d96e64a82f5.jpg',
     'published', false, now() - INTERVAL '17 days'),
    (v_tenant_id,
     'Training Beyond Summer: Team Riding',
     'team-riding-beyond-summer',
     'Our team program now runs past the summer season.',
     '<p>Riders who want more than camp weeks can now train year-round. The team program extends coached sessions into spring and fall, with structured progressions, race preparation, and indoor training when the trails are resting.</p><p>Spots are limited by coaching capacity. Current campers get first access before open enrollment.</p>',
     '/uploads/a31bc4c9-f35a-40f8-81a5-79c764781e68/event-b46b16e0da5b41cc9ae771d0a98ef86d.jpg',
     'published', false, now() - INTERVAL '24 days');
END $hl_blog$;

-- ============================================================================
-- Highland Bike Park - preserve the hand-built 3 Ride Pass (credits pass with a
-- published landing page at /SeasonPasses/3-ride-pass). The ticketing block wipes
-- all season pass products on reseed; this re-inserts it, values captured from
-- the live stage row. The demo site 3 Ride Pass menu link points at its landing.
-- ============================================================================
DO $hl_threeride$
DECLARE
    v_tenant_id uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found';
    END IF;

    INSERT INTO season_pass_product (tenant_id, name, description, price_cents, valid_from_date, valid_to_date, kind, total_credits, sort_order, slug, landing_published, hero_image_url, landing_html) SELECT v_tenant_id, '3 Ride Pass', 'Three lift-served ride days, any open day this season. One rider, no blackout dates.', 21900, CURRENT_DATE, (CURRENT_DATE + INTERVAL '9 months')::date, 'credits', 3, 100, '3-ride-pass', 't', NULL, '<h2>Three days. One season. Zero pressure.</h2><p>One day at Highland gets you hooked. Three days is where it clicks: day one you explore the mountain, day two you start linking sections, and by day three you''re riding trails top to bottom with real flow.</p><ul><li><strong>Three full lift-served ride days</strong> to use any open day this season</li><li><strong>No blackout dates</strong> - weekends, holidays, race weekends, all fair game</li><li><strong>No advance booking</strong> - show your pass at the gate whenever you''re ready</li><li><strong>One rider, all season</strong> - your pass, your progression</li></ul><p>Cheaper than three day tickets, with none of the commitment of a full season pass. When the forecast looks perfect, just come ride.</p>' WHERE NOT EXISTS (SELECT 1 FROM season_pass_product WHERE tenant_id = v_tenant_id AND slug = '3-ride-pass');
END $hl_threeride$;

COMMIT;
