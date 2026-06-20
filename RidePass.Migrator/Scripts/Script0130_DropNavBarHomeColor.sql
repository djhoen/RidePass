-- Collapse the nav bar to a single config. We previously let a tenant (and the apex platform
-- branding) set a separate background/text color for the home/landing route vs the rest of the
-- site. That extra knob added confusion for little value, so the home route now uses the same
-- nav_bar_color / nav_bar_text_color as everywhere else and the home-specific columns are dropped.
--
-- Both tenant_branding and platform_branding carried the override (added in Script0092/0093).

ALTER TABLE tenant_branding   DROP COLUMN IF EXISTS nav_bar_home_color;
ALTER TABLE tenant_branding   DROP COLUMN IF EXISTS nav_bar_home_text_color;
ALTER TABLE platform_branding DROP COLUMN IF EXISTS nav_bar_home_color;
ALTER TABLE platform_branding DROP COLUMN IF EXISTS nav_bar_home_text_color;
