-- Tracks whether a tenant has had its demo/seed data populated (stage + local only), so the
-- super-admin "Populate Seed Data" button can hide once it's been used. NULL = never seeded; set to
-- now() on the first populate. Mirrors concession_menu_settings.seeded_at.
-- Additive and rerunnable.

ALTER TABLE tenant ADD COLUMN IF NOT EXISTS seed_data_populated_at timestamptz NULL;
