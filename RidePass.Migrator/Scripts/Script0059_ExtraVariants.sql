-- Add-on variants — size / color / gender per SKU.
--
-- A tenant can sell merch (t-shirts, hats, etc.) as add-ons. Each variant is
-- one buyable SKU under a parent extra-product. All three attributes are
-- nullable, so a product can use just one (size-only shirts) or a combo. Price
-- and image are nullable per variant — null means "inherit from the product".
--
-- Inventory is tenant-wide here (one count of physical stock, regardless of
-- which event it sells at). The existing event_extra_eligibility.inventory
-- still applies to non-variant products as a per-event cap.

CREATE TABLE event_extra_variant (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id  uuid NOT NULL REFERENCES event_extra_product(id) ON DELETE CASCADE,

    -- All three optional. The picker hides any column that's null across the
    -- whole product (e.g. shirts using only size + gender, no color).
    size        text NULL,
    color       text NULL,
    gender      text NULL,             -- 'mens' | 'womens' | 'unisex' | 'youth' (free text — front-end normalises)

    sku         text NULL,             -- optional tenant-side SKU for inventory systems
    price_cents int  NULL CHECK (price_cents IS NULL OR price_cents >= 0),  -- null = inherit product.price_cents
    inventory   int  NULL CHECK (inventory IS NULL OR inventory >= 0),       -- null = unlimited
    image_url   text NULL,             -- null = inherit product.image_url

    sort_order  int  NOT NULL DEFAULT 100,
    is_active   boolean NOT NULL DEFAULT true,

    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

-- Prevent duplicate (size, color, gender) tuples within a product. COALESCE
-- folds NULLs to '' so two rows with size='M', color=NULL on the same product
-- are caught by the index.
CREATE UNIQUE INDEX idx_extra_variant_unique_attrs
    ON event_extra_variant (product_id,
        COALESCE(size, ''), COALESCE(color, ''), COALESCE(gender, ''));

CREATE INDEX idx_extra_variant_product ON event_extra_variant (product_id);

CREATE TRIGGER trg_event_extra_variant_updated_at
    BEFORE UPDATE ON event_extra_variant
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Per-purchase variant link + frozen attributes. Frozen so historical reads
-- are stable even if the tenant later edits or deletes the variant. RESTRICT
-- on variant deletes is a UX nudge — a variant that's been sold can be
-- de-activated but not removed outright.
ALTER TABLE event_extra_purchase
    ADD COLUMN variant_id uuid NULL REFERENCES event_extra_variant(id) ON DELETE RESTRICT,
    ADD COLUMN size_at_purchase   text NULL,
    ADD COLUMN color_at_purchase  text NULL,
    ADD COLUMN gender_at_purchase text NULL;

CREATE INDEX idx_extra_purchase_variant ON event_extra_purchase (variant_id) WHERE variant_id IS NOT NULL;
