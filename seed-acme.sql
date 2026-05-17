-- Local-only dummy data for the 'acme' tenant. Re-runnable: it deletes prior
-- demo rows (matched by description containing '[seed]') before inserting fresh.
-- Run against your local dev DB:
--   psql -h 127.0.0.1 -U ridepass -d ridepass_dev -f seed-acme.sql

\set ON_ERROR_STOP on

BEGIN;

DO $seed$
DECLARE
    v_tenant_id uuid;
    v_open_ride uuid;
    v_race      uuid;
    v_practice  uuid;
    v_lesson    uuid;

    v_user_alex   uuid;
    v_user_bri    uuid;
    v_user_charlie uuid;
    v_user_dani   uuid;
    v_user_evan   uuid;

    v_dp_adult uuid;
    v_dp_late  uuid;

    v_sp_unlim uuid;
    v_sp_wkdy  uuid;

    v_evt_id   uuid;
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE subdomain = 'acme';
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant "acme" not found — create it first via the super admin UI';
    END IF;

    -- ── Wipe prior seed-tagged rows (cascading inserts later) ──────────────
    DELETE FROM event_ticket_purchase WHERE tenant_id = v_tenant_id
        AND purchaser_email LIKE '%@acme.test';
    DELETE FROM day_pass_purchase WHERE tenant_id = v_tenant_id
        AND purchaser_email LIKE '%@acme.test';
    DELETE FROM season_pass_purchase WHERE tenant_id = v_tenant_id
        AND purchaser_email LIKE '%@acme.test';
    DELETE FROM event_ticket_tier WHERE tenant_id = v_tenant_id
        AND name LIKE '[seed]%';
    DELETE FROM event WHERE tenant_id = v_tenant_id
        AND description LIKE '%[seed]%';
    DELETE FROM blackout WHERE tenant_id = v_tenant_id
        AND reason LIKE '%[seed]%';
    DELETE FROM users WHERE tenant_id IS NULL
        AND email LIKE '%@acme.test';

    -- ── Tenant home page content ──────────────────────────────────────────
    UPDATE tenant SET
        about_html = '<p>Welcome to <strong>Acme MX Park</strong> — five tracks across 80 acres of southern Colorado high desert. Beginner-friendly groomer, intermediate motocross, pro-level supercross, single-track woods, and a kids'' loop with no jumps.</p><p>We''re open year-round, but ride conditions matter — check today''s status above before driving out.</p>',
        hours_json = '{"mon":{"closed":true,"open":"09:00","close":"17:00"},"tue":{"closed":false,"open":"10:00","close":"19:00"},"wed":{"closed":false,"open":"10:00","close":"19:00"},"thu":{"closed":false,"open":"10:00","close":"19:00"},"fri":{"closed":false,"open":"10:00","close":"21:00"},"sat":{"closed":false,"open":"08:00","close":"21:00"},"sun":{"closed":false,"open":"08:00","close":"18:00"}}'::jsonb,
        daily_status_open = true,
        daily_status_message = 'Tacky after morning rain — perfect dirt all afternoon.',
        daily_status_updated_at = now() - INTERVAL '2 hours',
        contact_email = 'info@acmemx.test',
        social_facebook_url = 'https://facebook.com/acmemx',
        social_instagram_url = 'https://instagram.com/acmemx',
        social_youtube_url = 'https://youtube.com/@acmemx',
        refund_policy_html = '<p>Day passes are refundable up to 24 hours before the riding date. Within 24 hours, passes can be transferred to another date.</p><p>Event tickets are non-refundable but transferable to another rider.</p><p>Season passes: pro-rated refund within 30 days of purchase, less a 10% admin fee.</p>',
        shipping_name = 'Acme MX Park – Office',
        address_line = '12450 Track Rd',
        city = 'Pueblo West',
        region = 'CO',
        postal_code = '81007',
        country = 'USA',
        latitude = 38.348,
        longitude = -104.722,
        timezone = 'America/Denver'
    WHERE id = v_tenant_id;

    -- Cache event-type IDs we'll reference
    SELECT id INTO v_open_ride FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'open_ride';
    SELECT id INTO v_race      FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'race';
    SELECT id INTO v_practice  FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'practice';
    SELECT id INTO v_lesson    FROM tenant_event_type WHERE tenant_id = v_tenant_id AND code = 'lesson';

    -- ── Day pass products ─────────────────────────────────────────────────
    INSERT INTO day_pass_product (tenant_id, name, description, price_cents, sort_order)
        VALUES
            (v_tenant_id, 'Adult day pass', 'Full-day access for riders 18+', 4500, 10),
            (v_tenant_id, 'Late day pass',  'After 3pm only — discounted',   2500, 30)
        ON CONFLICT DO NOTHING;
    SELECT id INTO v_dp_adult FROM day_pass_product WHERE tenant_id = v_tenant_id AND name = 'Adult day pass';
    SELECT id INTO v_dp_late  FROM day_pass_product WHERE tenant_id = v_tenant_id AND name = 'Late day pass';

    -- ── Season pass products ──────────────────────────────────────────────
    INSERT INTO season_pass_product
        (tenant_id, name, description, price_cents, valid_from_date, valid_to_date, kind, valid_days_of_week, total_credits)
        VALUES
            (v_tenant_id, 'Unlimited season pass', 'Ride any open day, all season',
                40000, CURRENT_DATE, CURRENT_DATE + INTERVAL '8 months', 'unlimited', NULL, NULL),
            (v_tenant_id, 'Weekday season pass',   'Mon-Fri only — best value if you can ride midweek',
                22500, CURRENT_DATE, CURRENT_DATE + INTERVAL '8 months', 'days_of_week', ARRAY[1,2,3,4,5], NULL)
        ON CONFLICT DO NOTHING;
    SELECT id INTO v_sp_unlim FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Unlimited season pass';
    SELECT id INTO v_sp_wkdy  FROM season_pass_product WHERE tenant_id = v_tenant_id AND name = 'Weekday season pass';

    -- ── Riders (global users — tenant_id IS NULL for the 'rider' role) ────
    -- Password hash below is the ASP.NET Identity hash of "Password123!" generated
    -- via PasswordHasher<TUser>; cheat: use the pattern, you can reset via /ResetPassword.
    -- For seed, leave password_hash as a placeholder — admins can issue resets.
    INSERT INTO users (id, email, password_hash, first_name, last_name, role, birthdate, emergency_contact_name, emergency_contact_phone)
        VALUES
            (uuid_generate_v4(), 'alex@acme.test',    'seed-no-login', 'Alex',    'Rivera',   'rider', '1992-03-14', 'Jamie Rivera', '719-555-0101'),
            (uuid_generate_v4(), 'bri@acme.test',     'seed-no-login', 'Bri',     'Nakamura', 'rider', '1988-07-22', 'Sam Nakamura', '719-555-0102'),
            (uuid_generate_v4(), 'charlie@acme.test', 'seed-no-login', 'Charlie', 'Okafor',   'rider', '2010-09-05', 'Tomi Okafor',  '719-555-0103'),
            (uuid_generate_v4(), 'dani@acme.test',    'seed-no-login', 'Dani',    'Schmidt',  'rider', '1985-12-30', 'Pat Schmidt',  '719-555-0104'),
            (uuid_generate_v4(), 'evan@acme.test',    'seed-no-login', 'Evan',    'Lee',      'rider', '2008-04-18', 'Min Lee',      '719-555-0105')
        ON CONFLICT DO NOTHING;

    SELECT id INTO v_user_alex    FROM users WHERE email = 'alex@acme.test';
    SELECT id INTO v_user_bri     FROM users WHERE email = 'bri@acme.test';
    SELECT id INTO v_user_charlie FROM users WHERE email = 'charlie@acme.test';
    SELECT id INTO v_user_dani    FROM users WHERE email = 'dani@acme.test';
    SELECT id INTO v_user_evan    FROM users WHERE email = 'evan@acme.test';

    -- ── Upcoming events ───────────────────────────────────────────────────
    -- Open ride day, this Saturday
    INSERT INTO event (id, tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (uuid_generate_v4(), v_tenant_id, v_open_ride, 'Saturday Open Ride',
                'All tracks open. Bring water. [seed]',
                date_trunc('day', now() AT TIME ZONE 'America/Denver') + (((6 - EXTRACT(DOW FROM now())::int + 7) % 7) || ' days')::interval + INTERVAL '9 hours',
                date_trunc('day', now() AT TIME ZONE 'America/Denver') + (((6 - EXTRACT(DOW FROM now())::int + 7) % 7) || ' days')::interval + INTERVAL '17 hours',
                false, 80, 'All Tracks', 'scheduled');

    -- Race series round, ~2 weeks out
    INSERT INTO event (id, tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (uuid_generate_v4(), v_tenant_id, v_race, 'Spring Series Round 3',
                'Class racing across all skill levels. Practice 8-10am, racing 10:30am-3pm. [seed]',
                now() + INTERVAL '14 days' + (TIME '08:00' - LOCALTIME),
                now() + INTERVAL '14 days' + (TIME '15:00' - LOCALTIME),
                false, 120, 'Supercross Track', 'scheduled')
        RETURNING id INTO v_evt_id;

    -- Add ticket tiers for the race event
    INSERT INTO event_ticket_tier (id, tenant_id, event_id, name, price_cents, inventory, sort_order, is_active)
        VALUES
            (uuid_generate_v4(), v_tenant_id, v_evt_id, '[seed] Pro class entry',         7500, 30, 10, true),
            (uuid_generate_v4(), v_tenant_id, v_evt_id, '[seed] Intermediate class entry', 5500, 60, 20, true),
            (uuid_generate_v4(), v_tenant_id, v_evt_id, '[seed] Beginner class entry',     3500, 30, 30, true);

    -- Practice nights, recurring (3 of them)
    INSERT INTO event (id, tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, location_label, status)
        VALUES
            (uuid_generate_v4(), v_tenant_id, v_practice, 'Friday Night Practice',
                'Beginner-to-intermediate practice. [seed]',
                now() + INTERVAL '3 days' + (TIME '17:00' - LOCALTIME),
                now() + INTERVAL '3 days' + (TIME '21:00' - LOCALTIME),
                false, 'MX Track', 'scheduled'),
            (uuid_generate_v4(), v_tenant_id, v_practice, 'Friday Night Practice',
                'Beginner-to-intermediate practice. [seed]',
                now() + INTERVAL '10 days' + (TIME '17:00' - LOCALTIME),
                now() + INTERVAL '10 days' + (TIME '21:00' - LOCALTIME),
                false, 'MX Track', 'scheduled'),
            (uuid_generate_v4(), v_tenant_id, v_practice, 'Friday Night Practice',
                'Beginner-to-intermediate practice. [seed]',
                now() + INTERVAL '17 days' + (TIME '17:00' - LOCALTIME),
                now() + INTERVAL '17 days' + (TIME '21:00' - LOCALTIME),
                false, 'MX Track', 'scheduled');

    -- Beginner lesson clinic, 5 days out
    INSERT INTO event (id, tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity, location_label, status)
        VALUES (uuid_generate_v4(), v_tenant_id, v_lesson, 'Beginner Skills Clinic',
                'Half-day for new riders. Bring your own bike. Includes lunch. [seed]',
                now() + INTERVAL '5 days' + (TIME '09:00' - LOCALTIME),
                now() + INTERVAL '5 days' + (TIME '13:00' - LOCALTIME),
                false, 12, 'Skills Loop', 'scheduled')
        RETURNING id INTO v_evt_id;

    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, is_active)
        VALUES (v_tenant_id, v_evt_id, '[seed] Clinic spot', 12000, 12, 10, true);

    -- ── Blackouts ─────────────────────────────────────────────────────────
    INSERT INTO blackout (tenant_id, starts_at, ends_at, all_day, reason)
        VALUES
            (v_tenant_id,
             date_trunc('day', now()) + INTERVAL '7 days',
             date_trunc('day', now()) + INTERVAL '8 days',
             true, '[seed] Annual track maintenance — all tracks closed'),
            (v_tenant_id,
             date_trunc('day', now()) + INTERVAL '21 days',
             date_trunc('day', now()) + INTERVAL '22 days',
             true, '[seed] Memorial Day — closed');

    -- ── Past purchases (paid) — gives the admin reports something to show ─
    -- A handful of day passes from the last 3 weeks
    INSERT INTO day_pass_purchase
        (tenant_id, purchaser_user_id, product_id, valid_on_date, amount_cents,
         status, purchaser_email, purchaser_name, created_at)
        VALUES
            (v_tenant_id, v_user_alex,    v_dp_adult, CURRENT_DATE - 18, 4500, 'paid', 'alex@acme.test',    'Alex Rivera',    now() - INTERVAL '18 days'),
            (v_tenant_id, v_user_bri,     v_dp_adult, CURRENT_DATE - 14, 4500, 'paid', 'bri@acme.test',     'Bri Nakamura',   now() - INTERVAL '14 days'),
            (v_tenant_id, v_user_charlie, v_dp_late,  CURRENT_DATE - 11, 2500, 'paid', 'charlie@acme.test', 'Charlie Okafor', now() - INTERVAL '11 days'),
            (v_tenant_id, v_user_dani,    v_dp_adult, CURRENT_DATE - 7,  4500, 'paid', 'dani@acme.test',    'Dani Schmidt',   now() - INTERVAL '7 days'),
            (v_tenant_id, v_user_evan,    v_dp_late,  CURRENT_DATE - 4,  2500, 'paid', 'evan@acme.test',    'Evan Lee',       now() - INTERVAL '4 days'),
            (v_tenant_id, v_user_alex,    v_dp_adult, CURRENT_DATE - 2,  4500, 'paid', 'alex@acme.test',    'Alex Rivera',    now() - INTERVAL '2 days'),
            (v_tenant_id, v_user_bri,     v_dp_adult, CURRENT_DATE,      4500, 'paid', 'bri@acme.test',     'Bri Nakamura',   now() - INTERVAL '1 hour');

    -- A couple of season pass purchases
    INSERT INTO season_pass_purchase
        (tenant_id, purchaser_user_id, product_id, amount_cents, status,
         purchaser_email, purchaser_name, valid_from_date, valid_to_date, created_at)
        VALUES
            (v_tenant_id, v_user_alex, v_sp_unlim, 40000, 'paid', 'alex@acme.test', 'Alex Rivera',
                CURRENT_DATE - 20, CURRENT_DATE + INTERVAL '8 months', now() - INTERVAL '20 days'),
            (v_tenant_id, v_user_dani, v_sp_wkdy,  22500, 'paid', 'dani@acme.test', 'Dani Schmidt',
                CURRENT_DATE - 12, CURRENT_DATE + INTERVAL '8 months', now() - INTERVAL '12 days');

    RAISE NOTICE 'Seeded acme tenant id %', v_tenant_id;
END $seed$;

COMMIT;
