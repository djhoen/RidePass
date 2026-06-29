-- Two more gates on online Food & Beverage ordering, both evaluated in the tenant's timezone and
-- layered on top of the weekly ordering_hours (Script0149):
--   1) ordering_seasons: optional open-season date ranges. NULL/empty = open year-round (current
--      behavior). Otherwise online ordering is only open when today falls inside one of the ranges.
--      Stored as a JSON array of { startDate, endDate } ("yyyy-MM-dd", inclusive).
--   2) require_event_day: when true (the default), online ordering is closed on days that have nothing
--      on the events calendar, so a closed track defaults to closed F&B. Existing tenants backfill to
--      true to match the requested default; an admin can turn it off per tenant.
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS ordering_seasons jsonb NULL;
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS require_event_day boolean NOT NULL DEFAULT true;
