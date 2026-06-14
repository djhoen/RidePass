-- Foreground (text + icon) color for the top app bar. Mirrors the same
-- home / rest-of-site split as nav_bar_color from Script0092 so admins can
-- pair a dark background with white text on interior pages but use a light
-- background with dark text over the apex hero, etc.
--
-- NULL falls back to white at render time so existing rows keep the prior
-- light-on-color look. The home-only column is nullable and inherits from
-- the rest-of-site value when blank.

ALTER TABLE tenant_branding
    ADD COLUMN nav_bar_text_color      text NULL,
    ADD COLUMN nav_bar_home_text_color text NULL;

ALTER TABLE platform_branding
    ADD COLUMN nav_bar_text_color      text NULL,
    ADD COLUMN nav_bar_home_text_color text NULL;
