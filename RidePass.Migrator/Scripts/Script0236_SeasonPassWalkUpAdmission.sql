-- A walk-up track (tenant.season_pass_admission_type_id = 2, see Script0235) can open the
-- lift on a day with nothing on the events calendar at all. Today that rider cannot be
-- admitted: season_pass_reservation.event_id has been NOT NULL since Script0035, so an
-- admission has nowhere to live without an event to hang it on, and the scanner dead-ends
-- with "No event is running today at this track". That is the gap this migration closes.
--
-- The fix is to let a reservation row anchor to EITHER an event or a calendar date, never
-- neither. An event-anchored row is exactly what it has always been and keeps event_id
-- set with check_in_date NULL. A no-event walk-up row inverts that: event_id NULL, and
-- check_in_date holding the tenant-local calendar date the rider was admitted, computed
-- server-side from the tenant's timezone rather than UTC "today" (a track in Denver
-- admitting someone at 7pm must not land on tomorrow's date).
--
-- The alternative considered and rejected was a synthetic "operating day" event row per
-- open day. It would have kept event_id NOT NULL, but at the cost of polluting the public
-- calendar, the reports, and the capacity logic with rows that are not events.
--
-- No backfill is needed. Every row the app has ever written has event_id set, because the
-- only path that creates one requires an EventId (SeasonPassGateRedeemRequest marks it
-- [Required]), so all existing data already satisfies the new CHECK.

ALTER TABLE season_pass_reservation
    ALTER COLUMN event_id DROP NOT NULL;

ALTER TABLE season_pass_reservation
    ADD COLUMN IF NOT EXISTS check_in_date date NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_season_pass_reservation_anchor'
    ) THEN
        ALTER TABLE season_pass_reservation
            ADD CONSTRAINT chk_season_pass_reservation_anchor
            CHECK (event_id IS NOT NULL OR check_in_date IS NOT NULL);
    END IF;
END $$;

-- The existing UNIQUE (season_pass_purchase_id, event_id) from Script0035 stays as it is
-- and keeps protecting event-anchored rows. It cannot protect the no-event rows: Postgres
-- treats every NULL in a unique key as distinct, so two walk-up admissions for the same
-- pass would never collide on it and a rider could be admitted twice on one day, burning
-- two ride credits. This partial index is the replacement rule for that case. One rider,
-- one admission, per tenant-local calendar day. A raced second insert that slips past the
-- controller's pre-check hits this and gets turned into the idempotent
-- already-admitted response rather than a second burn.
CREATE UNIQUE INDEX IF NOT EXISTS uk_season_pass_reservation_walkup
    ON season_pass_reservation (season_pass_purchase_id, check_in_date)
    WHERE event_id IS NULL;
