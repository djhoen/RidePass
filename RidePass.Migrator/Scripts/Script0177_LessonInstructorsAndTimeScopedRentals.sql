-- Lessons: instructors + time-scoped bike rentals.
--
-- A "lesson" stays what it already is: an `event` whose tenant_event_type.code =
-- 'lesson'. It keeps ticket tiers, waivers, extras and the existing checkout. This
-- script adds the three things lessons were missing:
--
--   1. instructor            — a real per-tenant entity you assign to a lesson.
--                              An instructor must never hold two overlapping
--                              lessons; that check is enforced in the API against
--                              idx_event_instructor_instructor (see EventController).
--   2. event_rental_eligibility — which rental products (bikes) may be booked as
--                              part of a given lesson, with an optional per-lesson
--                              price override. Mirrors event_extra_eligibility.
--   3. A precise reservation window on rental_purchase.
--
-- ── Why rental_purchase needs a window ───────────────────────────────────────
-- Rentals were day-granular (start_date/end_date `date` + daily_rate_cents). A
-- 10am-12pm lesson booking a bike would consume that bike for the whole day, so a
-- track running three lessons a day could only ever rent each bike once.
--
-- We add starts_at/ends_at (timestamptz, HALF-OPEN: [starts_at, ends_at)) and make
-- them the single source of truth for every availability check. Both booking paths
-- write them, so a lesson rental and a walk-up day rental contend for the same
-- units — which is the whole point: a bike reserved for a lesson is invisible to
-- every other process for exactly that window, and no longer.
--
-- Half-open is deliberate: a lesson ending at 12:00 and one starting at 12:00 do
-- NOT collide, whereas the old inclusive `start <= to AND end >= from` would have
-- said they did.
--
-- ── Backwards compatibility ──────────────────────────────────────────────────
-- start_date/end_date are KEPT and still populated — reports, the counter list and
-- v_recent_sales read them. The rental_purchase_sync_window trigger derives
-- whichever side of the pair the writer omitted, in tenant-local time. That means
-- the currently-deployed app (which knows only about dates) keeps working
-- unchanged during rollout, which is what lets us tighten starts_at/ends_at to
-- NOT NULL in this same script rather than deferring to a later release.
-- Dropping the date columns, if ever, is a separate contract step.

-- ── 1. Instructors ───────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS instructor (
    id          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    email       text        NULL,
    phone       text        NULL,
    bio         text        NULL,
    image_url   text        NULL,
    is_active   boolean     NOT NULL DEFAULT true,
    sort_order  int         NOT NULL DEFAULT 100,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_instructor_tenant
    ON instructor (tenant_id, is_active, sort_order);

DROP TRIGGER IF EXISTS trg_instructor_updated_at ON instructor;
CREATE TRIGGER trg_instructor_updated_at
    BEFORE UPDATE ON instructor
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- Assignment join. ON DELETE RESTRICT on the instructor side so an instructor with
-- lessons on the books can't be deleted out from under them (the API soft-disables
-- via is_active instead).
CREATE TABLE IF NOT EXISTS event_instructor (
    event_id      uuid NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    instructor_id uuid NOT NULL REFERENCES instructor(id) ON DELETE RESTRICT,
    PRIMARY KEY (event_id, instructor_id)
);

-- Hot path: "does this instructor already have an event overlapping [x, y)?"
CREATE INDEX IF NOT EXISTS idx_event_instructor_instructor
    ON event_instructor (instructor_id);


-- ── 2. Which bikes are rentable as part of which lesson ──────────────────────
-- Mirrors event_extra_eligibility. price_cents_override NULL = charge the product's
-- daily_rate_cents. Lessons are sub-day, so the override is how a tenant prices a
-- "bike for this 2-hour lesson" differently from a full-day walk-up rental.
CREATE TABLE IF NOT EXISTS event_rental_eligibility (
    event_id            uuid NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    product_id          uuid NOT NULL REFERENCES rental_product(id) ON DELETE CASCADE,
    price_cents_override int NULL CHECK (price_cents_override IS NULL OR price_cents_override >= 0),
    PRIMARY KEY (event_id, product_id)
);

CREATE INDEX IF NOT EXISTS idx_event_rental_eligibility_product
    ON event_rental_eligibility (product_id);


-- ── 3. Precise reservation window on rental_purchase ─────────────────────────
ALTER TABLE rental_purchase
    ADD COLUMN IF NOT EXISTS starts_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS ends_at   timestamptz NULL,
    -- Set when this rental was booked as part of a lesson checkout. Lets the admin
    -- lesson view show who's on a bike, and the counter see it alongside the ticket.
    ADD COLUMN IF NOT EXISTS event_id  uuid NULL REFERENCES event(id) ON DELETE SET NULL;


-- Keeps the date pair and the window pair in agreement regardless of which one the
-- writer supplied. Runs BEFORE the NOT NULL / CHECK constraints are evaluated, so
-- it can legally fill in a column the writer left NULL.
CREATE OR REPLACE FUNCTION rental_purchase_sync_window()
RETURNS TRIGGER AS $$
DECLARE
    tz text;
BEGIN
    SELECT timezone INTO tz FROM tenant WHERE id = NEW.tenant_id;
    IF tz IS NULL OR tz = '' THEN
        tz := 'UTC';
    END IF;

    -- Date-only writer (the pre-lessons app): derive the window from the inclusive
    -- date range, midnight-to-midnight in tenant-local time.
    IF NEW.starts_at IS NULL AND NEW.start_date IS NOT NULL THEN
        NEW.starts_at := (NEW.start_date::timestamp AT TIME ZONE tz);
    END IF;
    IF NEW.ends_at IS NULL AND NEW.end_date IS NOT NULL THEN
        NEW.ends_at := ((NEW.end_date + 1)::timestamp AT TIME ZONE tz);
    END IF;

    -- Window writer (lesson checkout): derive the legacy date range so reports and
    -- the counter list keep working. ends_at is exclusive, so step back before
    -- taking the date or a 10:00-12:00 booking would look like it ran to the 12th.
    IF NEW.start_date IS NULL AND NEW.starts_at IS NOT NULL THEN
        NEW.start_date := ((NEW.starts_at AT TIME ZONE tz))::date;
    END IF;
    IF NEW.end_date IS NULL AND NEW.ends_at IS NOT NULL THEN
        NEW.end_date := ((NEW.ends_at AT TIME ZONE tz) - interval '1 microsecond')::date;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_rental_purchase_sync_window ON rental_purchase;
CREATE TRIGGER trg_rental_purchase_sync_window
    BEFORE INSERT OR UPDATE ON rental_purchase
    FOR EACH ROW EXECUTE FUNCTION rental_purchase_sync_window();


-- Backfill existing rows. Idempotent via the IS NULL guard.
UPDATE rental_purchase rp
SET starts_at = (rp.start_date::timestamp AT TIME ZONE COALESCE(NULLIF(t.timezone, ''), 'UTC')),
    ends_at   = ((rp.end_date + 1)::timestamp AT TIME ZONE COALESCE(NULLIF(t.timezone, ''), 'UTC'))
FROM tenant t
WHERE t.id = rp.tenant_id
  AND (rp.starts_at IS NULL OR rp.ends_at IS NULL);


-- Safe to tighten now: everything is backfilled and the trigger guarantees future
-- inserts get a window even from a writer that only knows about dates.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'rental_purchase' AND column_name = 'starts_at'
          AND is_nullable = 'YES'
    ) THEN
        ALTER TABLE rental_purchase ALTER COLUMN starts_at SET NOT NULL;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'rental_purchase' AND column_name = 'ends_at'
          AND is_nullable = 'YES'
    ) THEN
        ALTER TABLE rental_purchase ALTER COLUMN ends_at SET NOT NULL;
    END IF;
END $$;


DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_rental_purchase_window'
    ) THEN
        ALTER TABLE rental_purchase
            ADD CONSTRAINT chk_rental_purchase_window CHECK (ends_at > starts_at);
    END IF;
END $$;


-- Availability hot path is now (product, status, window) rather than (product,
-- status, dates). The old idx_rental_purchase_product_window stays for the date
-- columns the reports still filter on.
CREATE INDEX IF NOT EXISTS idx_rental_purchase_product_time_window
    ON rental_purchase (product_id, status, starts_at, ends_at);

CREATE INDEX IF NOT EXISTS idx_rental_purchase_event
    ON rental_purchase (event_id) WHERE event_id IS NOT NULL;
