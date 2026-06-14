-- is_published gates whether a tenant appears in PUBLIC discovery: the apex
-- "Tracks near you" map, featured tracks, /Discover search, and the apex events
-- feed. It does NOT affect subdomain resolution — admins must still be able to
-- reach their own site (tenant.ridepass.io) to set it up before going public.
--
-- New tenants default to NOT published (hidden until a super admin marks them
-- ready). Existing tenants are backfilled to published so nothing currently
-- live disappears from discovery when this ships.
--
-- tenant is a globally-scoped table; the backfill UPDATE intentionally spans
-- all rows (publishing every pre-existing tenant). The /tenant-audit skill
-- flags the missing tenant_id predicate as the intended exception here.

ALTER TABLE tenant ADD COLUMN is_published boolean NOT NULL DEFAULT false;

UPDATE tenant SET is_published = true;
