-- Per-tenant and platform-level nav bar color. Admins can paint the top app
-- bar a custom hex, with a separate value for the home/landing page so the
-- apex landing can use one accent while interior pages use a different one.
--
-- Column meanings:
--   nav_bar_color        NULL = fall back to theme primary at render time
--   nav_bar_home_color   Override for the home/landing route. NULL = use
--                        the rest-of-site nav_bar_color.

ALTER TABLE tenant_branding
    ADD COLUMN nav_bar_color      text NULL,
    ADD COLUMN nav_bar_home_color text NULL;

ALTER TABLE platform_branding
    ADD COLUMN nav_bar_color      text NULL,
    ADD COLUMN nav_bar_home_color text NULL;
