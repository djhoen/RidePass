-- Add-on enhancements:
--   * event_extra_product.expires_at — optional cutoff after which the product
--     stops being sellable (still listed in admin so tenants can re-extend).
--   * event_extra_product.inventory — tenant-wide cap on total units sold across
--     all variants and all events. NULL = unlimited (legacy behaviour).
--   * event_extra_variant.tier and event_extra_variant.description — freeform
--     metadata for tenants who sell tiered SKUs (e.g. Standard / Premium) or
--     need a per-variant blurb beyond the size/color/gender attributes.
--   * Gate Fee — seeded as a default product on new tenants + backfilled for
--     existing tenants, matching the Camping / Parking / Pit Vehicle pattern
--     established in Script0055.

ALTER TABLE event_extra_product
    ADD COLUMN expires_at timestamptz NULL,
    ADD COLUMN inventory  int         NULL CHECK (inventory IS NULL OR inventory >= 0);

ALTER TABLE event_extra_variant
    ADD COLUMN tier        text NULL,
    ADD COLUMN description text NULL;

-- Update the seeding trigger so brand-new tenants also get a Gate Fee.
CREATE OR REPLACE FUNCTION seed_default_extra_products()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO event_extra_product
        (tenant_id, name,            kind,           price_cents, requires_waiver, sort_order)
    VALUES
        (NEW.id,    'Gate Fee',      'gate_fee',     1000,        false,           5),
        (NEW.id,    'Camping',       'camping',      2500,        false,           10),
        (NEW.id,    'Parking',       'parking',      1000,        false,           20),
        (NEW.id,    'Pit Vehicle',   'pit_vehicle',  2000,        false,           30);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Backfill Gate Fee for existing tenants. Idempotent: only inserts where the
-- tenant has no row of kind='gate_fee' yet.
INSERT INTO event_extra_product (tenant_id, name, kind, price_cents, requires_waiver, sort_order)
SELECT t.id, 'Gate Fee', 'gate_fee', 1000, false, 5
FROM tenant t
WHERE NOT EXISTS (
    SELECT 1 FROM event_extra_product p
    WHERE p.tenant_id = t.id AND p.kind = 'gate_fee'
);
