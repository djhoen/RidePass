-- Tenant-defined discounts a staff member applies at any counter: "Military 10%", "VMBA member",
-- "$2 off". These already existed for Food & Beverage only (concession_discount_preset,
-- Script0160) and the model there was right; it was just locked to one surface. A track that
-- honours a military discount honours it on a gate ticket and a set of grips too, and having to
-- define it three times in three screens is how the three drift apart.
--
-- Two things the tenant controls that the F&B version decided globally:
--
--   surfaces          Where the discount may be applied. A track that gives VMBA members money
--                     off retail but not off race entry can say exactly that.
--   requires_manager  Whether applying it needs a manager PIN. F&B hardcoded this by category
--                     (comps and manual amounts always required one, presets never did). Making
--                     it per-discount lets a track wave through "Military 10%" while still
--                     gating "Employee 50%", which is the distinction they actually care about.
--
-- Surface names are the ledger's own source_kind values, deliberately. A discount's surface can
-- then be compared straight against what a sale books itself as, with no translation table to
-- keep in step.

CREATE TABLE IF NOT EXISTS discount_preset (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name              text        NOT NULL,
    -- 'percent' stores basis points (1000 = 10%); 'amount' stores cents. Same convention as
    -- coupon, season_pass_benefit and concession_discount_preset, so the math stays integer-only.
    kind              text        NOT NULL DEFAULT 'percent',
    value             int         NOT NULL DEFAULT 0,
    surfaces          text[]      NOT NULL DEFAULT '{}',
    requires_manager  boolean     NOT NULL DEFAULT false,
    is_active         boolean     NOT NULL DEFAULT true,
    sort_order        int         NOT NULL DEFAULT 0,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_discount_preset_tenant ON discount_preset (tenant_id, sort_order, name);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_discount_preset_kind') THEN
        ALTER TABLE discount_preset ADD CONSTRAINT chk_discount_preset_kind
            CHECK (kind IN ('percent', 'amount'));
    END IF;

    -- A zero discount is a mistake, not a configuration, and a percent over 100% would hand money
    -- back rather than take it off.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_discount_preset_value') THEN
        ALTER TABLE discount_preset ADD CONSTRAINT chk_discount_preset_value
            CHECK (value > 0 AND (kind <> 'percent' OR value <= 10000));
    END IF;

    -- Applicable to nothing would render nowhere and read as a silent failure to the admin who
    -- just created it.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_discount_preset_has_surface') THEN
        ALTER TABLE discount_preset ADD CONSTRAINT chk_discount_preset_has_surface
            CHECK (COALESCE(array_length(surfaces, 1), 0) >= 1);
    END IF;

    -- Constrained rather than free text so a typo can't quietly create a surface nothing reads.
    -- Adding a surface later means extending this list in a new migration, which is the point:
    -- it forces the code that honours it to be written at the same time.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_discount_preset_surfaces') THEN
        ALTER TABLE discount_preset ADD CONSTRAINT chk_discount_preset_surfaces
            CHECK (surfaces <@ ARRAY[
                'event_ticket', 'extras', 'season_pass', 'membership',
                'concession', 'shop_sale', 'shop_rental'
            ]::text[]);
    END IF;
END $$;

-- Carry the F&B presets over so a track that already configured them keeps them, scoped to the
-- surface they were built for. Keyed on (tenant, name) so a re-run adds nothing; the old table is
-- deliberately left in place until the F&B POS reads from here, and gets dropped in a later
-- migration rather than in the same breath as the code change.
INSERT INTO discount_preset (tenant_id, name, kind, value, surfaces, is_active, sort_order, created_at)
SELECT c.tenant_id, c.name, c.kind, c.value, ARRAY['concession']::text[], c.is_active, c.sort_order, c.created_at
FROM concession_discount_preset c
WHERE c.value > 0
  AND NOT EXISTS (
      SELECT 1 FROM discount_preset d
      WHERE d.tenant_id = c.tenant_id AND lower(d.name) = lower(c.name)
  );
