-- Lesson date changes (Highland go-live list): a venue can sell lessons on "rebook only" terms.
--
-- tenant.lesson_rebook_only: a rider can't cancel a lesson for a refund; they can move it to another
-- date of the same lesson (same event type, a group with the same name) up to
-- lesson_change_cutoff_hours before it starts, and not at all after that. Staff can still move or
-- cancel any booking. Off (the default): lessons follow the venue's normal cancellation policy.
--
-- event.weather_cancelled_at: staff cancelled the session for weather. Every rider on it is emailed a
-- link to rebook another date or take a refund, whatever the cutoff says (the venue cancelled, not
-- the rider). weather_cancelled_by_user_id is who did it; a refund the rider takes is recorded as
-- cancelled by that person.
--
-- lesson_rebook: one row per date change, for the booking's history and the audit trail.
--
-- Rerunnable: ADD COLUMN / CREATE TABLE / CREATE INDEX IF NOT EXISTS, constraints re-created.
ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS lesson_rebook_only boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS lesson_change_cutoff_hours int NOT NULL DEFAULT 72;
ALTER TABLE tenant DROP CONSTRAINT IF EXISTS chk_tenant_lesson_change_cutoff_hours;
ALTER TABLE tenant ADD CONSTRAINT chk_tenant_lesson_change_cutoff_hours
    CHECK (lesson_change_cutoff_hours BETWEEN 0 AND 720);

ALTER TABLE event
    ADD COLUMN IF NOT EXISTS weather_cancelled_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS weather_cancelled_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS lesson_rebook (
    id                 uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    ticket_id          uuid        NOT NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE,
    from_tier_id       uuid        NULL REFERENCES event_ticket_tier(id) ON DELETE SET NULL,
    to_tier_id         uuid        NULL REFERENCES event_ticket_tier(id) ON DELETE SET NULL,
    -- 'rider' (inside the window), 'weather' (the session was cancelled for weather), 'staff'.
    source             text        NOT NULL,
    by_user_id         uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE lesson_rebook DROP CONSTRAINT IF EXISTS chk_lesson_rebook_source;
ALTER TABLE lesson_rebook ADD CONSTRAINT chk_lesson_rebook_source
    CHECK (source IN ('rider', 'weather', 'staff'));
CREATE INDEX IF NOT EXISTS idx_lesson_rebook_ticket ON lesson_rebook (tenant_id, ticket_id, created_at);
