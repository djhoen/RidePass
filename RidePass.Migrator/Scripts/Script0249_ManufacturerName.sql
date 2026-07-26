-- Separates "what the manufacturer calls this part" from "what this shop calls it".
--
-- THE RULE THIS EXISTS TO ENFORCE: a product name a tenant typed is that tenant's, and must never
-- be visible to another tenant. Script0248 got this wrong. It contributed shop_product.name into
-- the shared library, which is tenant-authored free text: a shop that names a row "special order
-- for Bob" would have had that string shown at another shop's register.
--
-- So the shared library is now fed from ONE source only, shop_variant.manufacturer_name, which is
-- the manufacturer's own wording for the part. shop_product.name stays what it always was, this
-- shop's own name for the thing, and is now explicitly private: nothing reads it into
-- platform_part. A shop is free to call a part whatever they like without that leaking.
--
-- Manufacturer name lives on the VARIANT rather than the product because that is the granularity
-- a GTIN identifies: one barcode is one size/colour, and platform_part is keyed on GTIN-14.

DO $$
BEGIN
    -- Table-guarded like Script0243/0248: stage journals Script0182_BikeShopSale as run while
    -- shop_variant does not exist there, so a bare ALTER TABLE fails.
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'shop_variant'
    ) THEN
        ALTER TABLE shop_variant
            ADD COLUMN IF NOT EXISTS manufacturer_name text NULL;

        -- Inside the guard because COMMENT ON has no IF EXISTS form, so on stage (where
        -- shop_variant is missing) a bare statement out here would fail the whole script.
        --
        -- Written as a plain statement rather than wrapped in EXECUTE with dollar-tag quoting:
        -- PL/pgSQL runs DDL directly, so the wrapper bought nothing, and DbUp's preprocessor reads
        -- a named dollar-tag as a variable placeholder and aborts the whole migration run with
        -- "Variable c has no value defined". The unnamed tag opening this DO block is fine; a
        -- NAMED one is not. (Note also: never write a dollar-tag inside a comment in here, since
        -- the body is scanned for its closing tag regardless of comments.)
        COMMENT ON COLUMN shop_variant.manufacturer_name IS
            'The manufacturer''s own name for this part. SAFE TO SHARE: the only field fed into the cross-tenant platform_part library. Never put a shop''s own naming here; that belongs on shop_product.name, which is private to the tenant.';
    END IF;
END $$;

-- Any library entry that exists right now was contributed under the old rule, which means its
-- name came from a tenant's shop_product.name. Those are exactly the strings that must not be
-- shared, so they go. This is written to be correct whether or not Script0248 ever reached a real
-- database: on a fresh install it deletes nothing.
--
-- Only 'tenant_confirmed' rows are affected. 'staff' entries are RidePass's own wording and no
-- vendor rows can exist yet (no vendor is wired up). The confirmations cascade, and
-- shop_variant.platform_part_id is ON DELETE SET NULL, so no shop loses a product; the next scan
-- simply re-contributes, this time from manufacturer_name.
DELETE FROM platform_part WHERE source = 'tenant_confirmed';
