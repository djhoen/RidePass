-- Per-tenant blog. A tenant authors posts (draft/published), can feature exactly one
-- published post on their public home page, and toggles the public Blog nav link +
-- /Blog routes via tenant.blog_enabled (default OFF, like gift cards / concessions).
-- Each post has one main image (blog_post.main_image_url) plus an ordered set of
-- additional images in blog_post_image.

ALTER TABLE tenant ADD COLUMN IF NOT EXISTS blog_enabled boolean NOT NULL DEFAULT false;

CREATE TABLE blog_post (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    title          text        NOT NULL,
    slug           text        NOT NULL,
    excerpt        text        NULL,
    body_html      text        NULL,
    main_image_url text        NULL,
    status         text        NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'published')),
    is_featured    boolean     NOT NULL DEFAULT false,
    published_at   timestamptz NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);

-- Slug is the public URL key; unique per tenant and case-insensitive so /Blog/My-Post
-- and /blog/my-post can't resolve to two different rows.
CREATE UNIQUE INDEX uk_blog_post_tenant_slug ON blog_post (tenant_id, lower(slug));
-- Public list query: a tenant's published posts, newest first.
CREATE INDEX idx_blog_post_tenant_status ON blog_post (tenant_id, status, published_at DESC);
-- At most one featured post per tenant (the single home-page feature slot).
CREATE UNIQUE INDEX uk_blog_post_one_featured ON blog_post (tenant_id) WHERE is_featured;

CREATE TRIGGER trg_blog_post_updated_at
    BEFORE UPDATE ON blog_post
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Additional ("several other") images for a post. The main image lives on
-- blog_post.main_image_url; these are the gallery shown on the post page, ordered by
-- sort_order. tenant_id is denormalized so every read scopes by tenant without a join.
CREATE TABLE blog_post_image (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    blog_post_id uuid        NOT NULL REFERENCES blog_post(id) ON DELETE CASCADE,
    tenant_id    uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    image_url    text        NOT NULL,
    caption      text        NULL,
    sort_order   int         NOT NULL DEFAULT 0,
    created_at   timestamptz NOT NULL DEFAULT now()
);
-- Leads with tenant_id so the index also satisfies tenant-scoped reads; blog_post_id
-- + sort_order serve the per-post gallery fetch (always filtered by tenant_id too).
CREATE INDEX idx_blog_post_image_tenant_post ON blog_post_image (tenant_id, blog_post_id, sort_order);
