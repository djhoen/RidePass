-- Lessons step 6: per-day attendance, which is what makes multi-day camps checkable-in.
--
-- See docs/lessons.md. A camp is one event spanning several days sold as a single ticket, and
-- the date window at the gate already spans the whole event (todayInTenant between the event's
-- start and end date). The blocker was that redemption is a ONE-SHOT status flip with a single
-- redeemed_at_utc: scanning on day 1 sets status = 'redeemed' and day 2 is refused with
-- "Already redeemed."
--
-- This adds a row per (ticket, local date). Single-day events are untouched: they keep using the
-- status flip exactly as before and never write here.

CREATE TABLE IF NOT EXISTS event_ticket_attendance (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    ticket_id     uuid        NOT NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE,
    -- The tenant-LOCAL date, not a timestamp: "was this rider here on day 2" is a calendar
    -- question, and a track near midnight UTC would otherwise split one day in two.
    on_date       date        NOT NULL,
    checked_in_at timestamptz NOT NULL DEFAULT now(),
    by_user_id    uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

-- One check-in per ticket per day. The gate relies on this: a duplicate scan raises 23505 and
-- is reported as "already checked in today" rather than silently double-counting attendance.
CREATE UNIQUE INDEX IF NOT EXISTS uk_event_ticket_attendance_day
    ON event_ticket_attendance (ticket_id, on_date);

-- Roster hot path: everyone present on a given day of a camp.
CREATE INDEX IF NOT EXISTS idx_event_ticket_attendance_tenant_date
    ON event_ticket_attendance (tenant_id, on_date);
