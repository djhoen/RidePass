-- ═══════════════════════════════════════════════════════════════════════════════════════════════
-- HIGHLAND LEDGER TOP-UP: fill every un-posted business day, INCLUDING today-so-far.
--
-- Purpose: the QuickBooks live-sync demo needs PENDING days (activity in the ledger, no success
-- row in qbo_sync_log), and the End of Day report needs TODAY to show a live, in-progress day.
-- The main seed is now()-anchored and tenant-wide-wipes on rerun, so it cannot top up without
-- regenerating history that has already been posted to the sandbox (JEs would then disagree with
-- the DB). This script instead CLONES a recent, fully-populated donor day (same weekday,
-- 7/14/21/28 days back, first with >= 50 sale rows) into each un-posted day: source purchase rows
-- + their ledger rows, new ids/tokens/PaymentIntent ids, timestamps shifted forward, ~10% of
-- tickets and F&B orders randomly dropped so week-over-week totals differ.
--
-- Each day fills INCREMENTALLY by local time-of-day: from the last moment the day already has
-- (00:00 when empty) up to NOW for today, or the full day for past days. So today always reads
-- like a park that has been selling since opening, a rerun later the same day extends it, and
-- tomorrow's run completes the evening before the day can sync. Days with a SUCCESS row in
-- qbo_sync_log are never touched: their journal entry is already in QuickBooks and the DB must
-- keep agreeing with it. (Known cosmetic quirk: a rental cloned into today's afternoon can carry
-- a returned_at later this evening; nothing in the accounting screens shows it.)
--
-- What it clones per day: event_ticket_purchase, concession_sale(+lines), shop_sale(+lines),
-- shop_rental, season_pass_purchase, event_extra_purchase, their entry_kind='sale' ledger rows,
-- and one fresh gift card (v_accounting_entries Part 3 synthesizes its sale row). Donor refunds
-- and refunded/cancelled sources are excluded: a cloned "refund" would collide with the
-- one-refund-per-source unique index and a refunded-status source without its refund row would
-- not reconcile. Gift-card redemptions are excluded too (cloning one would double-draw a card's
-- balance).
--
-- What it deliberately does NOT create: event_ticket child registrations, waiver signatures,
-- payout rows (payout_id is NULLed so the new rows are simply "not yet paid out"), cash sessions.
-- Cloned tickets keep the donor day's tier_id, so they point at last week's event; the accounting
-- view, End of Day report and QBO sync never join to the event and do not care.
--
-- Rerunnable: the time-of-day window is the guard. A completed past day re-fills nothing, a
-- partial today extends to now, and running any later morning fills forward through the new
-- yesterday and starts the new today.
--
-- After filling, the QBO cursor (last_synced_date) is rewound to the last SUCCESS day in
-- qbo_sync_log; the hourly sweep advances the cursor past empty days, and without the rewind the
-- newly-filled days would sit invisibly behind it forever. sync_enabled is NOT touched here:
-- disable it yourself before the demo so the hourly sweep does not post the pending days early.
--
-- Run (stage droplet):
--   scp -i ~/.ssh/ridepass_deploy seed-highland-topup.sql deploy@147.182.247.145:/tmp/
--   ssh -i ~/.ssh/ridepass_deploy deploy@147.182.247.145 \
--     'URL=$(grep -oP "(?<=StageMirror__TargetUrl=).*" /etc/ridepass/staging.env); \
--      psql "$URL" -v ON_ERROR_STOP=1 -f /tmp/seed-highland-topup.sql'
-- ═══════════════════════════════════════════════════════════════════════════════════════════════

DO $hl_topup$
DECLARE
    v_tenant  uuid;
    v_tz      text;
    v_today   date;
    v_first   date;
    v_day     date;
    v_donor   date;
    v_shift   interval;
    v_lo      time;
    v_hi      time;
    v_n       int;
    v_have    int;
    v_led     int;
    v_gc_donor uuid;
BEGIN
    SELECT id, COALESCE(timezone, 'America/New_York') INTO v_tenant, v_tz
      FROM tenant WHERE lower(subdomain) = 'highland';
    IF v_tenant IS NULL THEN
        RAISE EXCEPTION 'tenant "highland" not found';
    END IF;

    v_today := (now() AT TIME ZONE v_tz)::date;

    -- Start the day after the last date already posted to QuickBooks; everything at or before it
    -- is frozen (the DB must keep agreeing with the posted journal entries). If nothing has ever
    -- posted, fall back to the day after the last seeded ledger date, i.e. only add NEW days.
    SELECT max(business_date) + 1 INTO v_first
      FROM qbo_sync_log WHERE tenant_id = v_tenant AND status = 'success';
    IF v_first IS NULL THEN
        SELECT max((occurred_at_utc AT TIME ZONE v_tz)::date) + 1 INTO v_first
          FROM tenant_ledger_entry WHERE tenant_id = v_tenant;
    END IF;
    IF v_first IS NULL THEN
        RAISE EXCEPTION 'highland ledger is empty - run seed-highland.sql first';
    END IF;

    -- A donor gift card to clone one sale of per day (paid, seed-owned, not imported).
    SELECT id INTO v_gc_donor
      FROM gift_card
     WHERE tenant_id = v_tenant AND stripe_payment_intent_id IS NOT NULL
       AND imported_from IS NULL AND status NOT IN ('pending', 'void')
     ORDER BY created_at DESC LIMIT 1;

    FOR v_day IN SELECT g::date FROM generate_series(v_first, v_today, interval '1 day') g LOOP

        -- Belt and braces: never touch a day whose journal entry is already in QuickBooks. The
        -- range above normally starts after the last success, but a 'failed' day inside the range
        -- is fair game (it has not posted; the retry will pick up whatever the day holds).
        CONTINUE WHEN EXISTS (
            SELECT 1 FROM qbo_sync_log
             WHERE tenant_id = v_tenant AND business_date = v_day AND status = 'success');

        -- Incremental window (also the rerun guard): fill from the last moment this day already
        -- has, up to now for today or the whole day for a past day.
        --
        -- A day the year fragment left as a THIN TAIL must not be read as "already covered".
        -- Those days hold a handful of rows, and one of them can sit late in the evening (a
        -- rental return, a work order), which drags the low water mark to 23:51 and collapses
        -- the window to nothing -- so the day stays empty on every rerun and the End of Day
        -- report shows a park that sold $2k on a Saturday. Only a day that ALREADY holds a real
        -- day's worth of sales (the same >= 50 bar the donor test below uses) earns its max
        -- timestamp as the low water mark; anything thinner is treated as empty and filled from
        -- midnight, with the few existing rows simply left in place alongside the clone.
        SELECT count(*) FILTER (WHERE entry_kind = 'sale'),
               COALESCE(max((occurred_at_utc AT TIME ZONE v_tz)::time), '00:00'::time)
          INTO v_have, v_lo
          FROM tenant_ledger_entry
         WHERE tenant_id = v_tenant
           AND (occurred_at_utc AT TIME ZONE v_tz)::date = v_day;
        IF v_have < 50 THEN
            v_lo := '00:00'::time;
        END IF;
        v_hi := CASE WHEN v_day = v_today THEN (now() AT TIME ZONE v_tz)::time
                     ELSE '24:00'::time END;
        CONTINUE WHEN v_lo >= v_hi;

        -- Same-weekday donor with a real day's worth of activity (skips the thin tail days the
        -- year fragment leaves just before its own end date).
        SELECT d INTO v_donor
          FROM (VALUES (v_day - 7), (v_day - 14), (v_day - 21), (v_day - 28)) t(d)
         WHERE (SELECT count(*) FROM tenant_ledger_entry
                 WHERE tenant_id = v_tenant AND entry_kind = 'sale'
                   AND (occurred_at_utc AT TIME ZONE v_tz)::date = t.d) >= 50
         ORDER BY d DESC LIMIT 1;
        IF v_donor IS NULL THEN
            RAISE NOTICE 'day %: no donor with >= 50 sales found, skipped', v_day;
            CONTINUE;
        END IF;
        v_shift := (v_day - v_donor) * interval '1 day';

        -- ── Maps: donor source id -> fresh id, per kind. The random() < 0.9 drops ~10% of
        -- tickets and F&B orders so the cloned day's totals are not a carbon copy of last week.
        DROP TABLE IF EXISTS _m_etp; DROP TABLE IF EXISTS _m_cs; DROP TABLE IF EXISTS _m_csl;
        DROP TABLE IF EXISTS _m_ss;  DROP TABLE IF EXISTS _m_ssl; DROP TABLE IF EXISTS _m_sr;
        DROP TABLE IF EXISTS _m_spp; DROP TABLE IF EXISTS _m_eep; DROP TABLE IF EXISTS _m_all;

        CREATE TEMP TABLE _m_etp AS
        SELECT e.id AS old_id, gen_random_uuid() AS new_id
          FROM tenant_ledger_entry l
          JOIN event_ticket_purchase e ON e.id = l.source_id AND e.tenant_id = l.tenant_id
         WHERE l.tenant_id = v_tenant AND l.entry_kind = 'sale' AND l.source_kind = 'event_ticket'
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::date = v_donor
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time > v_lo
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time <= v_hi
           AND e.status NOT IN ('refunded', 'cancelled')
           AND random() < 0.9;

        CREATE TEMP TABLE _m_cs AS
        SELECT c.id AS old_id, gen_random_uuid() AS new_id
          FROM tenant_ledger_entry l
          JOIN concession_sale c ON c.id = l.source_id AND c.tenant_id = l.tenant_id
         WHERE l.tenant_id = v_tenant AND l.entry_kind = 'sale' AND l.source_kind = 'concession'
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::date = v_donor
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time > v_lo
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time <= v_hi
           AND c.status NOT IN ('refunded', 'void')
           AND random() < 0.9;

        CREATE TEMP TABLE _m_csl AS
        SELECT cl.id AS old_id, gen_random_uuid() AS new_id, cl.sale_id AS old_sale_id
          FROM concession_sale_line cl JOIN _m_cs m ON m.old_id = cl.sale_id;

        CREATE TEMP TABLE _m_ss AS
        SELECT s.id AS old_id, gen_random_uuid() AS new_id
          FROM tenant_ledger_entry l
          JOIN shop_sale s ON s.id = l.source_id AND s.tenant_id = l.tenant_id
         WHERE l.tenant_id = v_tenant AND l.entry_kind = 'sale' AND l.source_kind = 'shop_sale'
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::date = v_donor
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time > v_lo
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time <= v_hi
           AND s.refunded_at IS NULL;

        CREATE TEMP TABLE _m_ssl AS
        SELECT sl.id AS old_id, gen_random_uuid() AS new_id, sl.sale_id AS old_sale_id
          FROM shop_sale_line sl JOIN _m_ss m ON m.old_id = sl.sale_id;

        CREATE TEMP TABLE _m_sr AS
        SELECT r.id AS old_id, gen_random_uuid() AS new_id
          FROM tenant_ledger_entry l
          JOIN shop_rental r ON r.id = l.source_id AND r.tenant_id = l.tenant_id
         WHERE l.tenant_id = v_tenant AND l.entry_kind = 'sale' AND l.source_kind = 'shop_rental'
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::date = v_donor
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time > v_lo
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time <= v_hi;

        CREATE TEMP TABLE _m_spp AS
        SELECT p.id AS old_id, gen_random_uuid() AS new_id
          FROM tenant_ledger_entry l
          JOIN season_pass_purchase p ON p.id = l.source_id AND p.tenant_id = l.tenant_id
         WHERE l.tenant_id = v_tenant AND l.entry_kind = 'sale' AND l.source_kind = 'season_pass'
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::date = v_donor
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time > v_lo
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time <= v_hi
           AND p.status NOT IN ('refunded', 'cancelled');

        CREATE TEMP TABLE _m_eep AS
        SELECT x.id AS old_id, gen_random_uuid() AS new_id
          FROM tenant_ledger_entry l
          JOIN event_extra_purchase x ON x.id = l.source_id AND x.tenant_id = l.tenant_id
         WHERE l.tenant_id = v_tenant AND l.entry_kind = 'sale' AND l.source_kind = 'extras'
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::date = v_donor
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time > v_lo
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time <= v_hi
           AND x.status NOT IN ('refunded', 'cancelled');

        CREATE TEMP TABLE _m_all AS
              SELECT 'event_ticket' AS kind, old_id, new_id FROM _m_etp
        UNION SELECT 'concession',          old_id, new_id FROM _m_cs
        UNION SELECT 'shop_sale',           old_id, new_id FROM _m_ss
        UNION SELECT 'shop_rental',         old_id, new_id FROM _m_sr
        UNION SELECT 'season_pass',         old_id, new_id FROM _m_spp
        UNION SELECT 'extras',              old_id, new_id FROM _m_eep;

        -- ── Source clones. jsonb round-trip copies every column as-is and only the listed keys
        -- are overridden, so this survives additive schema drift without a 40-column INSERT list.
        -- The fake PaymentIntent id is derived from the NEW id with the same expression the
        -- ledger clone uses below, so source and ledger stay consistent; cash rows keep NULL.

        INSERT INTO event_ticket_purchase
        SELECT (jsonb_populate_record(NULL::event_ticket_purchase, to_jsonb(e) || jsonb_build_object(
                   'id', m.new_id,
                   'redemption_token', gen_random_uuid()::text,
                   'stripe_payment_intent_id', CASE WHEN e.stripe_payment_intent_id IS NULL THEN NULL
                        ELSE 'pi_3HL' || substr(md5(m.new_id::text), 1, 21) END,
                   'created_at', e.created_at + v_shift,
                   'updated_at', least(e.updated_at + v_shift, now()),
                   'redeemed_at_utc', e.redeemed_at_utc + v_shift,
                   'waiver_signed_at', e.waiver_signed_at + v_shift,
                   'applied_reward_redemption_id', NULL,
                   'applied_season_pass_purchase_id', NULL))).*
          FROM event_ticket_purchase e JOIN _m_etp m ON m.old_id = e.id;

        INSERT INTO concession_sale
        SELECT (jsonb_populate_record(NULL::concession_sale, to_jsonb(c) || jsonb_build_object(
                   'id', m.new_id,
                   'stripe_payment_intent_id', CASE WHEN c.stripe_payment_intent_id IS NULL THEN NULL
                        ELSE 'pi_3HL' || substr(md5(m.new_id::text), 1, 21) END,
                   'created_at', c.created_at + v_shift,
                   'paid_at', c.paid_at + v_shift,
                   'ready_notified_at', c.ready_notified_at + v_shift,
                   'ready_at', c.ready_at + v_shift,
                   'completed_at', c.completed_at + v_shift,
                   'reward_redemption_id', NULL))).*
          FROM concession_sale c JOIN _m_cs m ON m.old_id = c.id;

        INSERT INTO concession_sale_line
        SELECT (jsonb_populate_record(NULL::concession_sale_line, to_jsonb(cl) || jsonb_build_object(
                   'id', ml.new_id,
                   'sale_id', ms.new_id,
                   'parent_line_id', mp.new_id))).*   -- NULL when the line has no combo parent
          FROM concession_sale_line cl
          JOIN _m_csl ml ON ml.old_id = cl.id
          JOIN _m_cs  ms ON ms.old_id = cl.sale_id
          LEFT JOIN _m_csl mp ON mp.old_id = cl.parent_line_id;

        INSERT INTO shop_sale
        SELECT (jsonb_populate_record(NULL::shop_sale, to_jsonb(s) || jsonb_build_object(
                   'id', m.new_id,
                   'receipt_token', gen_random_uuid()::text,
                   'stripe_payment_intent_id', CASE WHEN s.stripe_payment_intent_id IS NULL THEN NULL
                        ELSE 'pi_3HL' || substr(md5(m.new_id::text), 1, 21) END,
                   'created_at', s.created_at + v_shift,
                   'updated_at', least(s.updated_at + v_shift, now()),
                   'picked_up_at', s.picked_up_at + v_shift,
                   'refunded_at', NULL,
                   'refund_note', NULL,
                   'work_order_id', NULL,
                   'gift_card_id', NULL,
                   'gift_card_applied_cents', 0))).*
          FROM shop_sale s JOIN _m_ss m ON m.old_id = s.id;

        INSERT INTO shop_sale_line
        SELECT (jsonb_populate_record(NULL::shop_sale_line, to_jsonb(sl) || jsonb_build_object(
                   'id', ml.new_id,
                   'sale_id', ms.new_id,
                   'created_at', sl.created_at + v_shift))).*
          FROM shop_sale_line sl
          JOIN _m_ssl ml ON ml.old_id = sl.id
          JOIN _m_ss  ms ON ms.old_id = sl.sale_id;

        INSERT INTO shop_rental
        SELECT (jsonb_populate_record(NULL::shop_rental, to_jsonb(r) || jsonb_build_object(
                   'id', m.new_id,
                   'receipt_token', gen_random_uuid()::text,
                   'signature_request_token', gen_random_uuid()::text,   -- NOT NULL + unique
                   'signature_request_sent_at', NULL,
                   'stripe_payment_intent_id', CASE WHEN r.stripe_payment_intent_id IS NULL THEN NULL
                        ELSE 'pi_3HL' || substr(md5(m.new_id::text), 1, 21) END,
                   'starts_at', r.starts_at + v_shift,
                   'ends_at', r.ends_at + v_shift,
                   'checked_out_at', r.checked_out_at + v_shift,
                   'returned_at', r.returned_at + v_shift,
                   'created_at', r.created_at + v_shift,
                   'updated_at', least(r.updated_at + v_shift, now())))).*
          FROM shop_rental r JOIN _m_sr m ON m.old_id = r.id;

        INSERT INTO season_pass_purchase
        SELECT (jsonb_populate_record(NULL::season_pass_purchase, to_jsonb(p) || jsonb_build_object(
                   'id', m.new_id,
                   'redemption_token', gen_random_uuid()::text,
                   'stripe_payment_intent_id', CASE WHEN p.stripe_payment_intent_id IS NULL THEN NULL
                        ELSE 'pi_3HL' || substr(md5(m.new_id::text), 1, 21) END,
                   'created_at', p.created_at + v_shift,
                   'updated_at', least(p.updated_at + v_shift, now()),
                   'upgraded_from_purchase_id', NULL))).*
          FROM season_pass_purchase p JOIN _m_spp m ON m.old_id = p.id;

        INSERT INTO event_extra_purchase
        SELECT (jsonb_populate_record(NULL::event_extra_purchase, to_jsonb(x) || jsonb_build_object(
                   'id', m.new_id,
                   'redemption_token', gen_random_uuid()::text,
                   'stripe_payment_intent_id', CASE WHEN x.stripe_payment_intent_id IS NULL THEN NULL
                        ELSE 'pi_3HL' || substr(md5(m.new_id::text), 1, 21) END,
                   'created_at', x.created_at + v_shift,
                   'updated_at', least(x.updated_at + v_shift, now()),
                   'redeemed_at_utc', x.redeemed_at_utc + v_shift))).*
          FROM event_extra_purchase x JOIN _m_eep m ON m.old_id = x.id;

        -- One gift card sold mid-morning: v_accounting_entries Part 3 synthesizes its sale row,
        -- so the day's journal entry gets a Gift Card Liability credit line. Balance = face value
        -- (never redeemed), so no redemption bookkeeping is owed anywhere. Only when 10:30 falls
        -- inside this run's window and the day does not already have one (incremental reruns).
        IF v_gc_donor IS NOT NULL AND v_lo < '10:30'::time AND v_hi >= '10:30'::time
           AND NOT EXISTS (SELECT 1 FROM gift_card
                            WHERE tenant_id = v_tenant
                              AND (created_at AT TIME ZONE v_tz)::date = v_day) THEN
            INSERT INTO gift_card
            SELECT (jsonb_populate_record(NULL::gift_card, to_jsonb(g) || jsonb_build_object(
                       'id', nid.id,
                       'code', 'GIFT-HL' || upper(substr(md5(nid.id::text), 1, 8)),
                       'stripe_payment_intent_id', 'pi_3HL' || substr(md5(nid.id::text), 1, 21),
                       'status', 'active',
                       'balance_cents', g.initial_amount_cents,
                       'delivery_status', 'delivered',
                       'scheduled_delivery_at_utc', NULL,
                       'created_at', (v_day::timestamp + interval '10 hours 30 minutes') AT TIME ZONE v_tz,
                       'delivered_at_utc', (v_day::timestamp + interval '10 hours 31 minutes') AT TIME ZONE v_tz,
                       'updated_at', (v_day::timestamp + interval '10 hours 31 minutes') AT TIME ZONE v_tz))).*
              FROM gift_card g CROSS JOIN (SELECT gen_random_uuid() AS id) nid
             WHERE g.id = v_gc_donor;
        END IF;

        -- ── Ledger clones: the donor day's 'sale' rows, retargeted at the cloned sources.
        -- payout_id is NULLed (these rows are awaiting the next payout, which is realistic);
        -- the PaymentIntent id is rebuilt with the same expression the sources used above.
        INSERT INTO tenant_ledger_entry
        SELECT (jsonb_populate_record(NULL::tenant_ledger_entry, to_jsonb(l) || jsonb_build_object(
                   'id', gen_random_uuid(),
                   'source_id', u.new_id,
                   'occurred_at_utc', l.occurred_at_utc + v_shift,
                   'created_at', least(l.created_at + v_shift, now()),
                   'stripe_payment_intent_id', CASE WHEN l.stripe_payment_intent_id IS NULL THEN NULL
                        ELSE 'pi_3HL' || substr(md5(u.new_id::text), 1, 21) END,
                   'payout_id', NULL))).*
          FROM tenant_ledger_entry l
          JOIN _m_all u ON u.kind = l.source_kind AND u.old_id = l.source_id
         WHERE l.tenant_id = v_tenant AND l.entry_kind = 'sale'
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::date = v_donor
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time > v_lo
           AND (l.occurred_at_utc AT TIME ZONE v_tz)::time <= v_hi;

        GET DIAGNOSTICS v_led = ROW_COUNT;
        SELECT count(*) INTO v_n FROM _m_all;
        RAISE NOTICE 'day % (% -> %) <- donor %: % sources cloned, % ledger rows',
            v_day, v_lo, v_hi, v_donor, v_n, v_led;
    END LOOP;

    -- ── Rewind the QBO cursor to the last day actually posted. The hourly sweep advances
    -- last_synced_date to "yesterday" even across empty days, so without this the days filled
    -- above would sit behind the cursor and never be offered to a sync.
    UPDATE tenant_quickbooks_connection c
       SET last_synced_date = s.maxd
      FROM (SELECT max(business_date) AS maxd
              FROM qbo_sync_log
             WHERE tenant_id = v_tenant AND status = 'success') s
     WHERE c.tenant_id = v_tenant AND s.maxd IS NOT NULL
       AND (c.last_synced_date IS NULL OR c.last_synced_date > s.maxd);

    RAISE NOTICE 'QBO cursor: %', (SELECT last_synced_date FROM tenant_quickbooks_connection WHERE tenant_id = v_tenant);
END $hl_topup$;

-- Post-run visibility: what the next "Sync now" will offer.
SELECT business_date, count(*) AS entries, round(sum(gross_cents) / 100.0, 2) AS gross
  FROM v_accounting_entries
 WHERE tenant_id = (SELECT id FROM tenant WHERE lower(subdomain) = 'highland')
   AND business_date > (SELECT coalesce(max(business_date), '1900-01-01')
                          FROM qbo_sync_log
                         WHERE tenant_id = (SELECT id FROM tenant WHERE lower(subdomain) = 'highland')
                           AND status = 'success')
 GROUP BY business_date ORDER BY business_date;
