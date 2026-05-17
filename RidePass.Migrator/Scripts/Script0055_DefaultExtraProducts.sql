-- Seed Camping / Parking / Pit Vehicle as default products for every tenant
-- so the catalog is non-empty the moment a tenant flips Extras on. Tenants are
-- free to edit prices, rename, deactivate, or delete; they can also add their
-- own custom add-ons (RV hookup, locker, etc.).
--
-- Same pattern as the default event types seeded by Script0004 — a trigger on
-- tenant insert plus a one-time backfill for existing rows.

CREATE OR REPLACE FUNCTION seed_default_extra_products()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO event_extra_product
        (tenant_id, name,            kind,           price_cents, requires_waiver, sort_order)
    VALUES
        (NEW.id,    'Camping',       'camping',      2500,        false,           10),
        (NEW.id,    'Parking',       'parking',      1000,        false,           20),
        (NEW.id,    'Pit Vehicle',   'pit_vehicle',  2000,        false,           30);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_tenant_insert_extra_products
    AFTER INSERT ON tenant
    FOR EACH ROW EXECUTE FUNCTION seed_default_extra_products();

-- Backfill existing tenants. The (tenant_id, kind) combination isn't unique by
-- schema (tenants can rename/duplicate later), so guard against accidental
-- re-runs by only inserting where the tenant has zero existing products of
-- that kind.
INSERT INTO event_extra_product (tenant_id, name, kind, price_cents, requires_waiver, sort_order)
SELECT t.id, 'Camping', 'camping', 2500, false, 10
FROM tenant t
WHERE NOT EXISTS (
    SELECT 1 FROM event_extra_product p
    WHERE p.tenant_id = t.id AND p.kind = 'camping'
);

INSERT INTO event_extra_product (tenant_id, name, kind, price_cents, requires_waiver, sort_order)
SELECT t.id, 'Parking', 'parking', 1000, false, 20
FROM tenant t
WHERE NOT EXISTS (
    SELECT 1 FROM event_extra_product p
    WHERE p.tenant_id = t.id AND p.kind = 'parking'
);

INSERT INTO event_extra_product (tenant_id, name, kind, price_cents, requires_waiver, sort_order)
SELECT t.id, 'Pit Vehicle', 'pit_vehicle', 2000, false, 30
FROM tenant t
WHERE NOT EXISTS (
    SELECT 1 FROM event_extra_product p
    WHERE p.tenant_id = t.id AND p.kind = 'pit_vehicle'
);
