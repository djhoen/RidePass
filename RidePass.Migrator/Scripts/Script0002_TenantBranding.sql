-- Tenant branding: one row per tenant, auto-created via trigger.

CREATE TABLE tenant_branding (
    tenant_id          uuid        PRIMARY KEY REFERENCES tenant(id) ON DELETE CASCADE,
    primary_color      text        NOT NULL DEFAULT '#1976D2',
    secondary_color    text        NOT NULL DEFAULT '#424242',
    accent_color       text        NOT NULL DEFAULT '#82B1FF',
    tagline            text        NULL,
    theme_mode         text        NOT NULL DEFAULT 'light' CHECK (theme_mode IN ('light','dark')),
    logo_url           text        NULL,
    favicon_url        text        NULL,
    hero_image_url     text        NULL,
    secondary_hero_url text        NULL,
    updated_at         timestamptz NOT NULL DEFAULT now()
);

CREATE TRIGGER trg_tenant_branding_updated_at
    BEFORE UPDATE ON tenant_branding
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Auto-create default branding row when a tenant is inserted.
CREATE OR REPLACE FUNCTION ensure_tenant_branding()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO tenant_branding (tenant_id) VALUES (NEW.id)
    ON CONFLICT (tenant_id) DO NOTHING;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_tenant_insert_branding
    AFTER INSERT ON tenant
    FOR EACH ROW EXECUTE FUNCTION ensure_tenant_branding();

-- Backfill existing tenants.
INSERT INTO tenant_branding (tenant_id)
SELECT t.id FROM tenant t
WHERE NOT EXISTS (SELECT 1 FROM tenant_branding tb WHERE tb.tenant_id = t.id);
