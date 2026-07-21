-- Two customer touches the shop currently never makes: "your bike is ready" and, months later,
-- "time for a service". From the Lightspeed DMS comparison in docs/bike-shop.md, where a text
-- fires on Ready to Cashier and a 90-day service reminder follows.
--
-- Both are stamped on the work order rather than tracked in a separate queue: a work order is
-- already the unit of work, and a stamp makes "send exactly once" a single UPDATE ... WHERE
-- column IS NULL rather than a job table needing its own dedupe.

ALTER TABLE shop_work_order
    -- Set the first time the order reaches 'ready', so the notice is sent once even if staff
    -- bounce the status back and forth.
    ADD COLUMN IF NOT EXISTS ready_notified_at    timestamptz NULL,
    -- Set at pickup to (now + the tenant's reminder interval). NULL means never remind: either
    -- the job never got picked up, or the tenant turned reminders off at the time.
    ADD COLUMN IF NOT EXISTS service_reminder_at  timestamptz NULL,
    ADD COLUMN IF NOT EXISTS reminder_sent_at     timestamptz NULL;

-- The sweep's hot path: reminders that are due and not yet sent. Partial so it stays small.
CREATE INDEX IF NOT EXISTS idx_shop_wo_reminder_due
    ON shop_work_order (service_reminder_at)
    WHERE service_reminder_at IS NOT NULL AND reminder_sent_at IS NULL;

-- Per-tenant reminder policy. 0 = off, which is the default: a track should opt in to emailing
-- its customers months later rather than discover it did.
ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS shop_service_reminder_days int NOT NULL DEFAULT 0
        CHECK (shop_service_reminder_days >= 0 AND shop_service_reminder_days <= 730);
