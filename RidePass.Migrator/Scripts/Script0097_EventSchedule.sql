-- Per-event schedule: an ordered list of {time, label} rows shown on the public
-- event landing page (e.g. "7:00 AM" / "Gates open & check-in"). Stored as a
-- jsonb array; an empty array means no schedule section is shown.
--
-- event is a per-tenant table (scoped by event.tenant_id). The column has a
-- sensible default ('[]') so existing events need no backfill.

ALTER TABLE event ADD COLUMN schedule_json jsonb NOT NULL DEFAULT '[]'::jsonb;
