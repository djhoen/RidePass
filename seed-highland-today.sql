-- ═══════════════════════════════════════════════════════════════════════════════════════════════
-- HIGHLAND "DEMO DAY IS TODAY": a real riding day on TODAY, mid-operation.
--
-- Why this exists: seed-highland.sql's $hl_upcoming$ fragment builds the busy demo day on
-- TOMORROW, because it was written the evening before the first demo. When the seed is instead
-- run ON the morning of a demo, today ends up with revenue but no ROSTER: seed-highland-topup.sql
-- clones a donor day's sales into today, and those cloned tickets deliberately keep the donor's
-- tier_id, so they point at last week's event. The money is right while Compliance Today, the gate
-- scanner and the event roster are all empty, which is exactly the screen a day-in-the-life demo
-- opens on.
--
-- So this script gives today its own Open Riding event and fills it the way a park looks in the
-- afternoon: most of the day's riders already scanned in, a tail still to arrive, waivers signed
-- for nearly everyone and a deterministic handful missing so the compliance screen has real rows.
--
-- Run AFTER seed-highland.sql and seed-highland-topup.sql:
--   scp -i ~/.ssh/ridepass_deploy seed-highland-today.sql deploy@147.182.247.145:~/
--   ssh  -i ~/.ssh/ridepass_deploy deploy@147.182.247.145 '~/mkpsql.sh -f ~/seed-highland-today.sql'
--
-- Rerunnable: owns everything through the '.today.hl@highland.test' purchaser marker and the
-- event it creates, and wipes both on entry. Safe to run repeatedly through the day -- a later
-- run re-derives check-ins against the new "now", so the roster keeps filling as the day goes on.
--
-- Deliberately NOT touched: the ledger. These tickets are roster/compliance props for the gate,
-- and today's gate revenue is already supplied by the top-up clone. Writing tenant_ledger_entry
-- rows here would double-count today against a day the top-up has already balanced.
-- ═══════════════════════════════════════════════════════════════════════════════════════════════

DO $hl_today$
DECLARE
    v_tenant_id uuid;
    v_open_ride uuid;
    v_waiver_id uuid;
    v_today     date;
    v_now       time;
    v_evt       uuid;
    v_title     text;
    v_tix       int;
    v_in        int;
    v_sigs      int;
    v_first constant text[] := ARRAY['Avery','Blake','Casey','Devon','Emerson','Finley','Gray','Harper',
        'Indie','Jules','Kai','Logan','Marlow','Nico','Oakley','Parker','Quinn','Reese','Sawyer','Tatum',
        'Uma','Vaughn','Wren','Xavier','Yara','Zane','Micah','Lena','Theo','Sasha','Colby','Dana'];
    v_last constant text[] := ARRAY['Abbott','Barnes','Cortez','Dalton','Ellison','Fleming','Garner','Hayes',
        'Ibarra','Jennings','Keller','Lawson','Merritt','Nolan','Osborne','Pratt','Quimby','Rowe','Sutton','Tran',
        'Underwood','Vasquez','Whitaker','Xu','York','Zimmerman','Calloway','Drummond','Eastman','Forsythe','Granger','Holloway'];
    v_kid constant text[] := ARRAY['Riley','Rowan','Milo','Piper','Ellis','June','Cass','Arlo',
        'Wren','Sage','Beck','Nova','Emery','Iris','Rhys','Juno'];
BEGIN
    SELECT id INTO v_tenant_id FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant_id IS NULL THEN RAISE EXCEPTION 'tenant "highland" not found'; END IF;

    SELECT id INTO v_open_ride FROM tenant_event_type
     WHERE tenant_id = v_tenant_id AND code = 'open_ride';
    IF v_open_ride IS NULL THEN
        RAISE EXCEPTION 'open_ride event type missing - run seed-highland.sql first';
    END IF;

    SELECT id INTO v_waiver_id FROM tenant_waiver
     WHERE tenant_id = v_tenant_id AND is_active ORDER BY version DESC LIMIT 1;
    IF v_waiver_id IS NULL THEN
        RAISE EXCEPTION 'tenant waiver missing - run seed-highland.sql first';
    END IF;

    PERFORM setseed(0.42);
    v_today := (now() AT TIME ZONE 'America/New_York')::date;
    v_now   := (now() AT TIME ZONE 'America/New_York')::time;
    -- The event is named for the weekday it actually falls on, so the roster header never reads
    -- "Friday Open Riding" on a Thursday.
    v_title := to_char(v_today, 'FMDay') || ' Open Riding';

    -- ── Wipe (children first), by this script's own markers ─────────────────
    DELETE FROM rider_waiver_signature
     WHERE tenant_id = v_tenant_id AND signer_email LIKE '%.today.hl@highland.test';
    DELETE FROM event_ticket_purchase
     WHERE tenant_id = v_tenant_id AND purchaser_email LIKE '%.today.hl@highland.test';
    DELETE FROM season_pass_reservation r
     USING event e
     WHERE r.event_id = e.id AND e.tenant_id = v_tenant_id AND e.title = v_title;
    DELETE FROM event WHERE tenant_id = v_tenant_id AND title = v_title;

    -- ── Today's riding day (midweek pricing mirrors the $hl_upcoming$ tiers) ─
    INSERT INTO event (tenant_id, event_type_id, title, description, starts_at, ends_at,
                       all_day, capacity, location_label, status)
        VALUES (v_tenant_id, v_open_ride, v_title,
                'Lift-served trails open, all skill levels. Full-day, happy-hour, and junior tickets.',
                (v_today + TIME '09:00') AT TIME ZONE 'America/New_York',
                (v_today + TIME '17:00') AT TIME ZONE 'America/New_York',
                false, 700, 'Lift Base Area', 'scheduled')
        RETURNING id INTO v_evt;

    INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, kind, audience)
        VALUES
            (v_tenant_id, v_evt, 'Full Day Lift Ticket',          6800, 500, 10, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt, 'Junior Lift Ticket (7-14)',     3400, 200, 20, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt, 'Happy Hour Ticket (2pm-Close)', 4500, 300, 30, 'gate_fee', 'rider'),
            (v_tenant_id, v_evt, 'Junior Happy Hour (7-14)',      2500, 150, 40, 'gate_fee', 'rider');

    -- ── The day's tickets ───────────────────────────────────────────────────
    -- Bought in the days before (advance) or at the window this morning (walk-up). Happy-hour
    -- tiers mostly sell at the window, which is why their sale times are pinned to today.
    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at)
    SELECT v_tenant_id, x.tier_id, x.price_cents, 'paid',
           lower(x.fn || '.' || x.ln || '.' || x.g || '.today.hl@highland.test'),
           x.fn || ' ' || x.ln,
           CASE WHEN x.walkup THEN 'cash' ELSE 'stripe' END,
           x.created_at, x.created_at
    FROM (
        SELECT tt.id AS tier_id, tt.price_cents, g.g,
               v_first[1 + floor(random() * 32)::int] AS fn,
               v_last [1 + floor(random() * 32)::int] AS ln,
               w.walkup,
               CASE
                   -- Walk-up: bought at the window today, from opening up to now.
                   WHEN w.walkup THEN
                       (v_today + TIME '08:45') AT TIME ZONE 'America/New_York'
                       + (random() * GREATEST(EXTRACT(EPOCH FROM (v_now - TIME '08:45')), 60)) * INTERVAL '1 second'
                   -- Advance: bought over the previous week.
                   ELSE now() - (1 + floor(random() * 7)) * INTERVAL '1 day'
                                 + (floor(random() * 700) * INTERVAL '1 minute')
               END AS created_at
        FROM event_ticket_tier tt
        CROSS JOIN LATERAL generate_series(1,
            CASE tt.name
                WHEN 'Full Day Lift Ticket'          THEN 78
                WHEN 'Junior Lift Ticket (7-14)'     THEN 24
                WHEN 'Happy Hour Ticket (2pm-Close)' THEN 31
                WHEN 'Junior Happy Hour (7-14)'      THEN 11
                ELSE 0
            END) g(g)
        CROSS JOIN LATERAL (SELECT
            -- Roughly 45% of full-day and most happy-hour tickets are sold at the window
            -- ('cash' is the seed's tender for a window sale; 'terminal' is not a valid
            -- payment_method on event_ticket_purchase).
            CASE WHEN tt.name LIKE 'Happy Hour%' OR tt.name LIKE 'Junior Happy%'
                 THEN random() < 0.80 ELSE random() < 0.45 END AS walkup) w
        WHERE tt.event_id = v_evt
    ) x;

    -- Deterministic "Missing waiver" rows for the Compliance Today screen. The waiver pass below
    -- skips 'nowaiver%' addresses, so these two are always, reproducibly, the unsigned ones.
    INSERT INTO event_ticket_purchase
        (tenant_id, tier_id, amount_cents, status, purchaser_email, purchaser_name,
         payment_method, created_at, updated_at)
    SELECT v_tenant_id, tt.id, tt.price_cents, 'paid', x.email, x.pname, 'stripe',
           now() - INTERVAL '2 days', now() - INTERVAL '2 days'
    FROM event_ticket_tier tt
    JOIN (VALUES ('nowaiver.today1.today.hl@highland.test', 'Harlan Voss'),
                 ('nowaiver.today2.today.hl@highland.test', 'Marisol Pike')) AS x(email, pname) ON true
    WHERE tt.event_id = v_evt AND tt.name = 'Full Day Lift Ticket';

    SELECT count(*) INTO v_tix FROM event_ticket_purchase
     WHERE tenant_id = v_tenant_id AND purchaser_email LIKE '%.today.hl@highland.test';

    -- ── Waivers: one signature per person, ~96% signed ──────────────────────
    CREATE TEMP TABLE _hl_tdy ON COMMIT DROP AS
    SELECT lower(etp.purchaser_email) AS email,
           gen_random_uuid() AS sig_id,
           max(etp.purchaser_name) AS pname,
           min(etp.created_at) AS first_at,
           bool_or(tt.name LIKE 'Junior%') AS is_minor,
           row_number() OVER () AS rn
    FROM event_ticket_purchase etp
    JOIN event_ticket_tier tt ON tt.id = etp.tier_id
    WHERE etp.tenant_id = v_tenant_id
      AND etp.purchaser_email LIKE '%.today.hl@highland.test'
    GROUP BY lower(etp.purchaser_email);

    ALTER TABLE _hl_tdy ADD COLUMN signed boolean,
        ADD COLUMN rider_fn text, ADD COLUMN rider_ln text, ADD COLUMN birthdate date;
    UPDATE _hl_tdy SET
        signed = (random() >= 0.04) AND email NOT LIKE 'nowaiver%',
        rider_fn = CASE WHEN is_minor THEN v_kid[1 + rn % 16] ELSE split_part(pname, ' ', 1) END,
        rider_ln = split_part(pname, ' ', 2),
        birthdate = CASE WHEN is_minor
                         THEN CURRENT_DATE - ((2920 + (rn * 53) % 2190))::int
                         ELSE CURRENT_DATE - ((6570 + (rn * 131) % 14600))::int
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
    FROM _hl_tdy r
    WHERE r.signed;

    GET DIAGNOSTICS v_sigs = ROW_COUNT;

    UPDATE event_ticket_purchase etp SET
        rider_first_name = r.rider_fn,
        rider_last_name  = r.rider_ln,
        rider_birthdate  = r.birthdate,
        parent_guardian_name = CASE WHEN r.is_minor THEN r.pname END,
        registration_complete = r.signed,
        waiver_id = CASE WHEN r.signed THEN v_waiver_id END,
        waiver_signed_at = CASE WHEN r.signed THEN etp.created_at + INTERVAL '2 minutes' END,
        waiver_signature_id = CASE WHEN r.signed THEN r.sig_id END
    FROM _hl_tdy r
    WHERE lower(etp.purchaser_email) = r.email
      AND etp.tenant_id = v_tenant_id;

    -- ── Check-ins: the park at this hour ────────────────────────────────────
    -- Full-day riders scan in from the 09:00 lift opening; happy-hour tickets are not valid until
    -- 14:00, so they only scan once that window is open. An unsigned waiver never scans -- that is
    -- the point of the compliance screen -- and a tail of each tier is still to arrive.
    WITH pick AS (
        SELECT etp.id,
               tt.name AS tier_name,
               -- The earliest this ticket could have been scanned. A walk-up cannot come through
               -- the gate before the window sold it, so its own sale time is the floor; a ticket
               -- bought on an earlier day is bounded only by the lift opening. Drawing the scan
               -- time from the gate opening alone lets a rider who bought at 14:30 scan in at
               -- 09:30, which reads on the roster as a check-in that predates its own sale.
               CASE
                   WHEN (etp.created_at AT TIME ZONE 'America/New_York')::date = v_today
                       THEN GREATEST(g.gate_open, (etp.created_at AT TIME ZONE 'America/New_York')::time)
                   ELSE g.gate_open
               END AS scan_from
          FROM event_ticket_purchase etp
          JOIN event_ticket_tier tt ON tt.id = etp.tier_id
          CROSS JOIN LATERAL (SELECT CASE WHEN tt.name LIKE '%Happy%'
                                          THEN TIME '14:00' ELSE TIME '09:00' END AS gate_open) g
         WHERE tt.event_id = v_evt
           AND etp.tenant_id = v_tenant_id
           AND etp.status = 'paid'
           AND etp.registration_complete
    )
    UPDATE event_ticket_purchase etp SET
        status = 'redeemed',
        redeemed_at_utc = (v_today + p.scan_from) AT TIME ZONE 'America/New_York'
                          + (random() * EXTRACT(EPOCH FROM (v_now - p.scan_from))) * INTERVAL '1 second',
        updated_at = now()
    FROM pick p
    WHERE etp.id = p.id
      AND p.scan_from < v_now                 -- gate is open AND the sale has already happened
      AND random() < CASE WHEN p.tier_name LIKE '%Happy%' THEN 0.55 ELSE 0.82 END;

    SELECT count(*) INTO v_in FROM event_ticket_purchase etp
     JOIN event_ticket_tier tt ON tt.id = etp.tier_id
    WHERE tt.event_id = v_evt AND etp.status = 'redeemed';

    -- ── Season-pass holders riding today ────────────────────────────────────
    -- Pass holders reserve, then scan at the gate like everyone else.
    INSERT INTO season_pass_reservation (season_pass_purchase_id, event_id, status, reserved_at, checked_in_at)
    SELECT sp.id, v_evt,
           CASE WHEN random() < 0.75 THEN 'checked_in' ELSE 'reserved' END,
           now() - (floor(random() * 4) + 1) * INTERVAL '1 day',
           NULL
    FROM season_pass_purchase sp
    WHERE sp.tenant_id = v_tenant_id AND sp.status = 'paid'
      AND sp.purchaser_email LIKE '%.hl@highland.test'
      AND sp.valid_to_date >= v_today
    ORDER BY sp.created_at DESC
    LIMIT 34;

    -- Stamp the scan time on the ones that came through the gate.
    UPDATE season_pass_reservation r SET
        checked_in_at = (v_today + TIME '09:00') AT TIME ZONE 'America/New_York'
                        + (random() * GREATEST(EXTRACT(EPOCH FROM (v_now - TIME '09:00')), 60)) * INTERVAL '1 second'
    WHERE r.event_id = v_evt AND r.status = 'checked_in';

    RAISE NOTICE 'Highland TODAY seed (%): "%" -- % tickets, % checked in, % waiver signatures, % pass reservations',
        v_today, v_title, v_tix, v_in, v_sigs,
        (SELECT count(*) FROM season_pass_reservation WHERE event_id = v_evt);
END $hl_today$;
