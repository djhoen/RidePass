-- Per-tenant "Custom Pages". A tenant admin authors simple content pages (draft/published)
-- reachable at a root-level clean URL: {subdomain}.ridepass.io/{slug}. Two tenants may use
-- the same slug independently since slug uniqueness is scoped by tenant_id. A page may also
-- optionally appear as a top-level nav link (show_in_nav + nav_label + sort_order), mirroring
-- how blog_post drives the Blog nav link.

CREATE TABLE IF NOT EXISTS tenant_page (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    title          text        NOT NULL,
    slug           text        NOT NULL,
    body_html      text        NULL,
    hero_image_url text        NULL,
    status         text        NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'published')),
    show_in_nav    boolean     NOT NULL DEFAULT false,
    nav_label      text        NULL,
    sort_order     int         NOT NULL DEFAULT 0,
    published_at   timestamptz NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);

-- Slug is the public URL key; unique per tenant and case-insensitive so /My-Page and
-- /my-page can't resolve to two different rows. Two different tenants may reuse the
-- same slug independently since the index leads with tenant_id.
CREATE UNIQUE INDEX IF NOT EXISTS uk_tenant_page_tenant_slug ON tenant_page (tenant_id, lower(slug));
-- Admin list / public lookups: a tenant's pages by status, in sort order.
CREATE INDEX IF NOT EXISTS idx_tenant_page_tenant_status ON tenant_page (tenant_id, status, sort_order);
-- Nav rendering: a tenant's published, nav-visible pages, in sort order.
CREATE INDEX IF NOT EXISTS idx_tenant_page_tenant_nav ON tenant_page (tenant_id, show_in_nav, sort_order) WHERE show_in_nav;

DROP TRIGGER IF EXISTS trg_tenant_page_updated_at ON tenant_page;
CREATE TRIGGER trg_tenant_page_updated_at
    BEFORE UPDATE ON tenant_page
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
