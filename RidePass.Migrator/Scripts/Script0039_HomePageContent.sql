-- Public-facing home page content: a tenant-edited "About" section, hours of
-- operation, daily open/closed status, contact + social links, and a refund-policy
-- block for the footer. Plus two image collections (general photo gallery, and
-- track-layout graphics with descriptions) and a default cover image for each
-- event type so the events row on the home page has visuals even when individual
-- events don't carry their own photo.

ALTER TABLE tenant
    ADD COLUMN about_html               text        NULL,
    ADD COLUMN hours_json               jsonb       NOT NULL DEFAULT '{}'::jsonb,
    -- daily_status_open: NULL = not posted today; true = open; false = closed.
    -- daily_status_message is short free text shown alongside ("muddy after rain", etc.).
    -- daily_status_updated_at is used to fade/expire the badge after ~24h on the home page.
    ADD COLUMN daily_status_open        boolean     NULL,
    ADD COLUMN daily_status_message     text        NULL,
    ADD COLUMN daily_status_updated_at  timestamptz NULL,
    ADD COLUMN contact_email            text        NULL,
    ADD COLUMN social_facebook_url      text        NULL,
    ADD COLUMN social_instagram_url     text        NULL,
    ADD COLUMN social_tiktok_url        text        NULL,
    ADD COLUMN social_youtube_url       text        NULL,
    ADD COLUMN refund_policy_html       text        NULL;

-- Default image for event-type cards. Color column already exists (Script0004); when an
-- event itself has no image, the home page falls back to event_type.image_url, then to
-- a flat colored card using event_type.color.
ALTER TABLE tenant_event_type
    ADD COLUMN image_url text NULL;

-- General photo gallery shown on the home page.
CREATE TABLE tenant_gallery_image (
    id          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    image_url   text        NOT NULL,
    caption     text        NULL,
    sort_order  int         NOT NULL DEFAULT 100,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_tenant_gallery_image_tenant ON tenant_gallery_image (tenant_id, sort_order);

-- Track layout / section diagrams. Distinct from the photo gallery: these are
-- annotated images of the actual track with a human-readable description of the
-- section ("Pro Loop — jumps, advanced riders only").
CREATE TABLE tenant_track_graphic (
    id           uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id    uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    image_url    text        NOT NULL,
    title        text        NULL,
    description  text        NULL,
    sort_order   int         NOT NULL DEFAULT 100,
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_tenant_track_graphic_tenant ON tenant_track_graphic (tenant_id, sort_order);
