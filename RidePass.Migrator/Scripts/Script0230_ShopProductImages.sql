-- Multiple photos per shop product, so the storefront can open a product into a detail
-- gallery instead of the single card thumbnail.
--
-- COVER vs GALLERY. shop_product.image_url stays exactly what it is: the ONE cover image
-- used by the catalog grid, the admin list thumbnail, the register, and the CSV import.
-- This table holds the ADDITIONAL photos. Deliberately additive: every existing reader
-- keeps working untouched, and a product with no gallery rows behaves exactly as before.
--
-- The rules that follow from that split (mirrored in BikeShopRepository + ShopStore):
--   * the storefront detail gallery renders dedupe([cover, ...gallery]), so a tenant who
--     also uploaded the cover into the gallery does not see it twice;
--   * "Make cover" COPIES a gallery row's url onto shop_product.image_url and leaves the
--     row in place, so one blob can legitimately be referenced twice. Deleting a gallery
--     row therefore has to check for other references before deleting the blob;
--   * when image_url is null the catalog falls back to the first gallery photo, so
--     clearing the cover never blanks a card.
--
-- NO BACKFILL, on purpose: copying existing image_url values in would create rows whose
-- blob is co-owned by the cover for zero benefit (the detail view already renders the
-- cover first). Do not "fix" this later.
--
-- Additive and rerunnable. The 12-photos-per-product cap is enforced in the controller,
-- not here: a CHECK cannot count sibling rows and a trigger would ambush bulk imports.

CREATE TABLE IF NOT EXISTS shop_product_image (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    product_id  uuid        NOT NULL REFERENCES shop_product(id) ON DELETE CASCADE,
    image_url   text        NOT NULL,
    caption     text        NULL,
    sort_order  int         NOT NULL DEFAULT 100,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- tenant_id leads so the index also serves tenant-scoped reads, matching blog_post_image.
CREATE INDEX IF NOT EXISTS idx_shop_product_image_tenant_product
    ON shop_product_image (tenant_id, product_id, sort_order);
