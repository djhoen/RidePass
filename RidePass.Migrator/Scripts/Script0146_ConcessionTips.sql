-- Per-tenant "accept tips" toggle for Food & Beverage. Default OFF: a track opts in to tipping.
-- Lives on the per-tenant F&B settings row alongside the menu-board styling.
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS tips_enabled boolean NOT NULL DEFAULT false;
