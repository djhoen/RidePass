-- "Finish your registration" follow-up: a marker so the reminder worker emails a
-- purchaser at most once per incomplete order. NULL = not yet reminded.
ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS registration_reminder_sent_at timestamptz NULL;

-- Partial index for the sweep: only paid, not-yet-complete, not-yet-reminded rows,
-- ordered by age (the worker filters created_at <= now() - 1h).
CREATE INDEX IF NOT EXISTS idx_event_ticket_reg_reminder
    ON event_ticket_purchase (created_at)
    WHERE status = 'paid' AND registration_complete = false AND registration_reminder_sent_at IS NULL;
