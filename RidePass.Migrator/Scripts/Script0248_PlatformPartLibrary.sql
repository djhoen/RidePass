-- The shared bike parts library: what a barcode IS, pooled across every shop on the platform.
--
-- WHY THIS TABLE HAS NO tenant_id
-- This is a deliberate, documented exception to the tenant-isolation rule that governs the rest
-- of this schema, and it is safe for exactly one reason: the table holds IDENTITY ONLY. What is
-- this barcode, who makes it, what is it called. There is no price, no cost, no margin, no stock,
-- no supplier, no sales history and no tenant id on the row. Nothing here is competitive
-- information, and nothing here can be traced back to the shop that contributed it.
--
-- That constraint is load-bearing, not stylistic. The moment a price or a cost lands in this
-- table it becomes a cross-tenant leak of the worst kind: shop A reading shop B's margins. If a
-- future change wants to add a money column here, the answer is no; it belongs on shop_variant,
-- which is tenant-scoped.
--
-- HOW IT GETS FILLED
-- Two sources, and the distinction is a licensing one:
--   'tenant_confirmed'  a shop that already has the part in its own catalog scanned it at the
--                       counter. This is RidePass's own data, contributed through use, and
--                       carries no third-party terms at all. It is the primary source.
--   'staff'             entered by RidePass staff.
--   <vendor slug>       cached from an external GTIN lookup vendor (e.g. 'upcitemdb').
--
-- The vendor case is why `source` exists and why it names the vendor rather than just saying
-- 'external'. Go-UPC's terms bar redistribution and require deleting product data on
-- termination; UPCitemdb's are silent on caching and license use "solely for Customer's
-- operations". Naming the vendor per row means honouring either is one statement:
--     DELETE FROM platform_part WHERE source = '<vendor>';
-- and shop_variant.platform_part_id is ON DELETE SET NULL, so purging a vendor's rows can never
-- damage a shop's own catalog. No vendor is wired up yet; the column is here so that when one is,
-- the obligation is already satisfiable.

CREATE TABLE IF NOT EXISTS platform_part (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Always the 14-digit right-justified zero-filled form (Services.BikeShop.Gtin), so a UPC-A,
    -- its EAN-13 spelling and its GTIN-14 spelling are one row rather than three.
    gtin14          char(14)    NOT NULL,
    name            text        NOT NULL,
    brand           text        NULL,
    mpn             text        NULL,
    -- A hint like 'Tires' or 'Drivetrain', NOT a shop_category id: categories are per-tenant and
    -- every shop names them differently. This only seeds the "add product" form.
    category_hint   text        NULL,
    source          text        NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);
-- Note what is NOT a column here: a times_confirmed counter. "How many shops agree" is derived by
-- counting platform_part_confirmation on read, never stored. A denormalized counter cannot be
-- maintained in the same statement that upserts the part (all CTEs in one statement share a
-- snapshot, so an UPDATE cannot see the row a sibling CTE just inserted, and the bump silently
-- does nothing), and maintaining it in a second statement buys drift in exchange for a count that
-- is a primary-key-prefix scan anyway.

CREATE UNIQUE INDEX IF NOT EXISTS uk_platform_part_gtin14 ON platform_part (gtin14);

-- Lets a lookup fall back to brand + manufacturer part number when a shop's label carries the MPN
-- rather than a barcode. Partial: most rows have no MPN and there is no point indexing the nulls.
CREATE INDEX IF NOT EXISTS ix_platform_part_mpn
    ON platform_part (lower(mpn)) WHERE mpn IS NOT NULL;

-- Which tenants have confirmed which part. This table DOES carry tenant_id, and it is the reason
-- "independent shops agree" can mean that rather than "someone scanned a lot": one shop scanning a
-- tube a hundred times is one row. It is never read cross-tenant and never projected into a
-- response; only its COUNT ever leaves this table.
CREATE TABLE IF NOT EXISTS platform_part_confirmation (
    platform_part_id uuid        NOT NULL REFERENCES platform_part (id) ON DELETE CASCADE,
    tenant_id        uuid        NOT NULL REFERENCES tenant (id) ON DELETE CASCADE,
    confirmed_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (platform_part_id, tenant_id)
);

-- The link from a shop's own variant to the shared identity. Price, cost and stock stay entirely
-- on shop_variant: this says "my part is that part", nothing more. ON DELETE SET NULL so purging
-- a vendor's contributed rows for licensing reasons leaves every shop's catalog intact.
--
-- Table-guarded for the same reason Script0243 is: the stage database journals
-- Script0182_BikeShopSale as having run while shop_variant does not actually exist there, so a
-- bare ALTER TABLE fails on stage. ADD COLUMN IF NOT EXISTS covers a missing column, not a
-- missing table.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'shop_variant'
    ) THEN
        ALTER TABLE shop_variant
            ADD COLUMN IF NOT EXISTS platform_part_id uuid NULL
                REFERENCES platform_part (id) ON DELETE SET NULL;

        CREATE INDEX IF NOT EXISTS ix_shop_variant_platform_part
            ON shop_variant (platform_part_id) WHERE platform_part_id IS NOT NULL;
    END IF;
END $$;
