-- Tripwires over the staff activity log. Steps one to three of this work made staff actions
-- recorded, reviewable, and (optionally) bounded by place and time. None of them tell an owner
-- that something happened; they all require somebody to go and look. This is the part that
-- reaches out.
--
-- Once a day, per tenant, the previous local day's audit_log entries are run through a small set
-- of rules and anything that trips is emailed to the tenant's contact address. The rules live in
-- code (Services/Alerts/StaffAlertRules), not here, because they need to read the jsonb metadata
-- each action wrote and are far easier to unit test as plain logic than as SQL.
--
-- Off by default. A track that has not looked at the Staff Activity screen yet has no basis for
-- setting a sensible refund threshold, and an alert that fires on normal behavior teaches people
-- to ignore alerts.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS staff_alerts_enabled boolean NOT NULL DEFAULT false;

-- One staff member's total refunds in a single local day. 50000 = $500, a deliberately loose
-- starting point: the first alert should be a real outlier, not the busiest Saturday.
ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS staff_alert_refund_cents int NOT NULL DEFAULT 50000;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_staff_alert_refund_cents'
    ) THEN
        ALTER TABLE tenant
            ADD CONSTRAINT chk_tenant_staff_alert_refund_cents
            CHECK (staff_alert_refund_cents > 0);
    END IF;
END $$;

-- One row per tenant per scanned local day, whether or not anything tripped. Two jobs:
--
--   Idempotency. The sweep runs hourly because tenants span timezones and a local day closes at
--   a different UTC moment for each, exactly as the QuickBooks sync does. The unique index is
--   what stops the same day being scanned (and emailed) twice, so a restart mid-sweep or a tick
--   that overlaps a slow run cannot double-send.
--
--   A record that the scan happened. A day with no row was never scanned, which is a different
--   thing from a day that was scanned and found nothing, and only the first is a problem.
CREATE TABLE IF NOT EXISTS staff_alert_scan (
    id             uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id      uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- The tenant-LOCAL calendar day that was scanned, not the UTC day.
    scan_date      date        NOT NULL,
    flagged_count  int         NOT NULL DEFAULT 0,
    -- NULL when nothing tripped (nothing was sent) or when the send failed.
    sent_at        timestamptz NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_staff_alert_scan_tenant_day
    ON staff_alert_scan (tenant_id, scan_date);

-- Sweep hot path: "which day did I last scan for this tenant".
CREATE INDEX IF NOT EXISTS idx_staff_alert_scan_tenant
    ON staff_alert_scan (tenant_id, scan_date DESC);
