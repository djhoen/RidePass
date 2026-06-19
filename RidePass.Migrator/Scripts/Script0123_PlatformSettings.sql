-- Global (non-tenant) platform settings: a simple key/value store for odds-and-ends
-- super-admin configuration that isn't tied to a single tenant. Globally scoped on
-- purpose (like tenant / users / super_admin), so no tenant_id.
CREATE TABLE IF NOT EXISTS platform_setting (
    key        text PRIMARY KEY,
    value      text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

-- Origins that may embed ANY tenant's widgets (our own first-party properties),
-- so we don't have to add them to every tenant's allow-list. Newline-separated.
INSERT INTO platform_setting (key, value)
VALUES (
    'embed_global_allowed_origins',
    E'https://ridepass.io\nhttps://www.ridepass.io\nhttps://loampassmx.com\nhttps://www.loampassmx.com'
)
ON CONFLICT (key) DO NOTHING;
