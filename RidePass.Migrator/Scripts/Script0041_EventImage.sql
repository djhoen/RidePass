-- Per-event cover image. Optional; when null, the public home page falls back to
-- the event type's default image, then to a flat colored card if neither is set.

ALTER TABLE event
    ADD COLUMN image_url text NULL;
