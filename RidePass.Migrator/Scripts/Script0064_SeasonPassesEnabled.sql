-- Season-pass feature toggle.
--
-- Default true so existing tenants keep selling season passes. When off:
--   - top-nav "Season Passes" link, hero CTA, and "See Season Passes" buttons hide
--   - SeasonPassController.Buy / Reserve reject new purchases / reservations
--   - admin pages stay reachable so the tenant can configure passes before flipping on

ALTER TABLE tenant
    ADD COLUMN season_passes_enabled boolean NOT NULL DEFAULT true;
