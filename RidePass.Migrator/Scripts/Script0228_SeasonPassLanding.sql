-- Per-product season pass LANDING PAGES (Highland-style /3ridepass/ marketing page):
--   slug               tenant-unique URL segment under /SeasonPasses/{slug}
--   hero_image_url     uploaded hero (IImageStorage kind 'passes'); relative or Spaces URL
--   landing_html       Tiptap rich body, stored raw and DOMPurify-sanitized at render
--                      (same contract as tenant_page.body_html / blog)
--   landing_published  draft gate; the public endpoint 404s unpublished landings
--
-- Additive, rerunnable, backwards-compatible: NULL slug / false published means
-- "no landing page", which is correct for every existing product.

ALTER TABLE season_pass_product
    ADD COLUMN IF NOT EXISTS slug text NULL,
    ADD COLUMN IF NOT EXISTS hero_image_url text NULL,
    ADD COLUMN IF NOT EXISTS landing_html text NULL,
    ADD COLUMN IF NOT EXISTS landing_published boolean NOT NULL DEFAULT false;

-- Case-insensitive per-tenant slug uniqueness, same shape as uk_tenant_page_tenant_slug.
CREATE UNIQUE INDEX IF NOT EXISTS uk_season_pass_product_tenant_slug
    ON season_pass_product (tenant_id, lower(slug))
    WHERE slug IS NOT NULL;
