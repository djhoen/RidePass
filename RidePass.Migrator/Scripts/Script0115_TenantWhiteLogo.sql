-- Optional white logo variant for tenants. Overlaid bottom-right on event card
-- photos on the apex (ridepass.io) Upcoming Events + /Events cards, where a color
-- logo can wash out against a photo. Edited in tenant Settings -> Branding.
ALTER TABLE tenant_branding ADD COLUMN IF NOT EXISTS logo_white_url text NULL;
