-- Platform (apex / super-admin) nav logo for ridepass.io. Singleton platform_branding row.
-- Parallels the per-tenant logo; rendered in the nav bar on the apex domain.
ALTER TABLE platform_branding ADD COLUMN IF NOT EXISTS logo_url text NULL;
