-- Customizable concession menu board: tenant-defined categories, per-product carousel opt-in, and
-- per-tenant board styling (logo + colors + carousel settings).

-- 1) Tenant-defined categories replace the fixed food/drink/swag/other enum for grouping + display.
CREATE TABLE concession_category (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    sort_order  int         NOT NULL DEFAULT 0,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_concession_category_tenant ON concession_category (tenant_id, sort_order);

-- 2) Products reference a category; carousel opt-in defaults true (only actually shows when the item
--    has an image and isn't sold out, enforced in the read path). The legacy `category` text column is
--    left in place but no longer written; category_id is the source of truth.
ALTER TABLE concession_product ADD COLUMN IF NOT EXISTS category_id uuid NULL REFERENCES concession_category(id) ON DELETE SET NULL;
ALTER TABLE concession_product ADD COLUMN IF NOT EXISTS show_in_carousel boolean NOT NULL DEFAULT true;

-- 3) Per-tenant menu board styling. NULL colors / logo fall back to the tenant's brand in the UI.
CREATE TABLE concession_menu_settings (
    tenant_id        uuid        PRIMARY KEY REFERENCES tenant(id) ON DELETE CASCADE,
    logo_url         text        NULL,
    background_color text        NULL,
    text_color       text        NULL,
    accent_color     text        NULL,
    show_carousel    boolean     NOT NULL DEFAULT true,
    carousel_seconds int         NOT NULL DEFAULT 5,
    updated_at       timestamptz NOT NULL DEFAULT now()
);

-- 4) Backfill: turn each tenant's existing distinct product categories into category rows, then link
--    every product to its matching category, so nothing loses its grouping. Sort the four legacy values
--    in their familiar order; any others fall after.
INSERT INTO concession_category (tenant_id, name, sort_order)
SELECT d.tenant_id, INITCAP(d.category),
       CASE d.category WHEN 'food' THEN 0 WHEN 'drink' THEN 1 WHEN 'swag' THEN 2 WHEN 'other' THEN 3 ELSE 4 END
FROM (SELECT DISTINCT tenant_id, category FROM concession_product WHERE category IS NOT NULL) d;

UPDATE concession_product p
SET category_id = c.id
FROM concession_category c
WHERE c.tenant_id = p.tenant_id AND c.name = INITCAP(p.category);
