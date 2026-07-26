-- One comparable key for a product barcode, so the register can actually match a scan.
--
-- The bug this fixes is live. BikeShopRegister matched a scanned code with string equality against
-- shop_variant.barcode, so the SAME physical part stored as a 12-digit UPC-A did not match when a
-- scanner emitted the 13-digit EAN form with a leading zero, which many scanners do. A US pack
-- (UPC-A), the identical European pack (EAN-13), a small item (EAN-8) and a case label (GTIN-14)
-- were four different products as far as the till was concerned.
--
-- GS1's guidance is to store every identifier right-justified and zero-filled to 14 digits, and
-- that is what gtin14 holds. barcode keeps whatever was typed or imported, because that is what
-- prints on the shop's own label; gtin14 is the derived key everything matches on.
--
-- The runtime owner of this column is Services.BikeShop.Gtin (with GtinTests pinning it). The SQL
-- function below exists only to backfill rows that predate the column, and is DROPPED at the end
-- so it cannot drift away from the C# over time. Two implementations of a check digit that both
-- live forever is how a barcode silently starts resolving to the wrong part.
--
-- Additive and rerunnable.

ALTER TABLE shop_variant ADD COLUMN IF NOT EXISTS gtin14 text NULL;

-- ── One-shot backfill helper (dropped below) ─────────────────────────────────
-- Mirrors Gtin.Normalize: strip separators, accept only GS1 widths, verify the mod-10 check
-- digit, pad left to 14. Returns NULL for anything that is not a valid GTIN (a shop's own SKU
-- like "BIKE-250F", or a mistyped number), because a bad match at a register sells the customer
-- something they did not pick up.
CREATE OR REPLACE FUNCTION pg_temp_gtin14(raw text) RETURNS text AS $$
DECLARE
    d text;
    total int := 0;
    w int := 3;
    i int;
BEGIN
    IF raw IS NULL THEN RETURN NULL; END IF;
    -- Letters mean it is a SKU, not a GTIN: reject rather than stripping down to the digits.
    IF raw ~ '[^0-9 \-_\t]' THEN RETURN NULL; END IF;
    d := regexp_replace(raw, '[^0-9]', '', 'g');
    IF length(d) NOT IN (8, 12, 13, 14) THEN RETURN NULL; END IF;

    -- Weights alternate 3,1 anchored on the RIGHT, which is why zero-padding afterwards never
    -- invalidates the code.
    FOR i IN REVERSE (length(d) - 1)..1 LOOP
        total := total + (substr(d, i, 1))::int * w;
        w := CASE WHEN w = 3 THEN 1 ELSE 3 END;
    END LOOP;

    IF ((10 - (total % 10)) % 10) <> (substr(d, length(d), 1))::int THEN RETURN NULL; END IF;
    RETURN lpad(d, 14, '0');
END;
$$ LANGUAGE plpgsql IMMUTABLE;

UPDATE shop_variant
SET gtin14 = pg_temp_gtin14(barcode)
WHERE barcode IS NOT NULL AND gtin14 IS NULL;

-- Two barcodes that differ as strings can normalise to the SAME key, which is the whole point,
-- but it also means existing data may now collide within a tenant. Keep the oldest row's key and
-- clear the rest rather than failing the deploy: the unique index below then always builds, and
-- the cleared rows are visible in the shop as "no barcode" for a human to sort out.
DO $$
DECLARE
    cleared int;
BEGIN
    WITH ranked AS (
        SELECT id, row_number() OVER (PARTITION BY tenant_id, gtin14 ORDER BY created_at, id) AS rn
        FROM shop_variant
        WHERE gtin14 IS NOT NULL
    )
    UPDATE shop_variant v SET gtin14 = NULL
    FROM ranked r
    WHERE v.id = r.id AND r.rn > 1;

    GET DIAGNOSTICS cleared = ROW_COUNT;
    IF cleared > 0 THEN
        RAISE NOTICE 'Script0245: % variant(s) shared a barcode with another product in the same '
                     'tenant; kept the oldest and cleared the rest. Re-enter those barcodes.', cleared;
    END IF;
END $$;

DROP FUNCTION IF EXISTS pg_temp_gtin14(text);

-- The match index, and the guarantee that one tenant cannot have two products on one barcode.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_variant_gtin14
    ON shop_variant (tenant_id, gtin14) WHERE gtin14 IS NOT NULL;
