-- Per-tenant config for the public home page's "Next Up" events row.
--   home_next_up_title          — heading text (NULL falls back to "Next Up")
--   home_next_up_event_type_ids — whitelist of event types to include in the row.
--                                 NULL or empty = show all types. When populated, only
--                                 events whose event_type_id is in this array surface.

ALTER TABLE tenant
    ADD COLUMN home_next_up_title text NULL,
    ADD COLUMN home_next_up_event_type_ids uuid[] NULL;
