-- Per-tenant kill switch for event subscriptions. Default TRUE to preserve current
-- behavior for tenants that already have subscribers. When off:
--   - new subscribe attempts are rejected
--   - the notifier short-circuits (existing rows persist but receive nothing)
--   - the Calendar page hides the subscribe button
--
-- Existing subscriptions are kept inert rather than deleted so flipping the switch
-- back on resumes service without re-asking riders to opt in.

ALTER TABLE tenant
    ADD COLUMN allow_event_subscriptions boolean NOT NULL DEFAULT true;
