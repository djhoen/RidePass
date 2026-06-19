-- Tenant deployment model (super-admin controlled):
--   hosted        — public face is {subdomain}.ridepass.io (default; today's behavior)
--   custom_domain — the track's own domain (www.xyzTrack.com) points at the RidePass site
--   embedded      — the track keeps their own website and embeds RidePass widgets
-- custom_domain + embed_* hold the concrete config the modes use; client_type is the label.
-- Discovery listing is still gated by the existing is_published flag.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS client_type text NOT NULL DEFAULT 'hosted'
    CHECK (client_type IN ('hosted', 'custom_domain', 'embedded'));
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS custom_domain text NULL;
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS embed_enabled boolean NOT NULL DEFAULT false;
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS embed_allowed_origins text[] NULL;

-- One tenant per custom domain (host -> tenant resolution lands in a later phase).
CREATE UNIQUE INDEX IF NOT EXISTS uk_tenant_custom_domain
    ON tenant (LOWER(custom_domain)) WHERE custom_domain IS NOT NULL;
