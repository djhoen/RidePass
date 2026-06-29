-- Track whether a tenant has loaded the editable starter F&B catalog, so the "Load starter content"
-- button can hide once it's been used. NULL = never seeded; set to now() on the first seed and kept
-- as-is on any re-seed.
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS seeded_at timestamptz NULL;
