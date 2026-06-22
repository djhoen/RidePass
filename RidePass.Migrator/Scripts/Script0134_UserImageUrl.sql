-- Profile photo for a user. Stored as the public URL returned by IImageStorage (absolute for
-- Spaces, app-relative for local disk). Nullable: existing accounts have no photo. Global
-- accounts (riders / super admins, tenant_id NULL) use it too, so it lives on users directly.

ALTER TABLE users
    ADD COLUMN image_url text NULL;
