-- Track leads — prospective track operators who submit the "For Tracks"
-- marketing page lead form on the apex domain. A submission stores one row
-- here and fans a notification (in-app + email) out to every super admin so
-- sales can follow up.
--
-- This table carries NO tenant_id BY DESIGN. A lead is a not-yet-customer
-- track owner; there is no tenant to scope it to (mirrors platform_branding /
-- platform_testimonial in Script0091). The /tenant-audit skill will flag the
-- absence of a tenant_id predicate here; that is the intended exception.
--
-- status drives a simple sales pipeline:
--   new       -> just submitted, nobody has reached out
--   contacted -> a super admin has followed up
--   closed    -> won, lost, or spam; out of the pipeline
--
-- ip_address / user_agent are captured for abuse review only (the form is
-- public and anonymous).

CREATE TABLE track_lead (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    contact_name    text        NOT NULL,
    track_name      text        NOT NULL,
    email           text        NOT NULL,
    phone           text        NULL,
    message         text        NULL CHECK (message IS NULL OR length(message) <= 4000),
    status          text        NOT NULL DEFAULT 'new'
                                CHECK (status IN ('new','contacted','closed')),
    ip_address      text        NULL,
    user_agent      text        NULL,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    updated_at_utc  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_track_lead_status_created
    ON track_lead (status, created_at_utc DESC);
