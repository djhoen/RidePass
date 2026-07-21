-- Bike shop, Phase 1: catalog + inventory + purchasing foundation.
--
-- The unified bike shop (retail sales + rentals + repairs) is built on one shared catalog and one
-- shared inventory, with the three transaction types layered on top in later migrations. See
-- docs/bike-shop.md for the full model. This script lays only the foundation every transaction type
-- draws from: what you offer (category/product/variant), what you physically have (pool stock +
-- serialized items), the audit trail of every stock change, and how stock comes in (suppliers +
-- purchase orders).
--
-- No transaction tables yet (sale / rental / repair) and no rental migration yet: the existing
-- rental_* tables are untouched here and get absorbed in Phase 3.
--
-- Hard boundary (docs/bike-shop.md): shop_* is its own catalog. Nothing here references, and nothing
-- else should reference, concession_* or event_extra_* — bike shop stock must never surface as an
-- F&B option or an event add-on, or vice versa. The only cross-surface thing is a season pass
-- benefit, which discounts at each surface's own checkout without mixing catalogs.
--
-- Additive and idempotent throughout: new tables + one nullable-with-default tenant flag, all
-- guarded, so re-running is a no-op and the deployed app is unaffected until a tenant turns the
-- shop on.

-- ── Feature flag ─────────────────────────────────────────────────────────────
-- Opt-in like every other tenant surface (gift cards, rentals, concessions). Separate from the
-- legacy rentals_enabled flag, which stays until rentals are re-homed in Phase 3.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS bike_shop_enabled boolean NOT NULL DEFAULT false;

-- ── Categories (departments) ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shop_category (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    -- Optional one-level nesting (Bikes > Mountain). Self-FK sets null so deleting a parent
    -- orphans children to top level rather than cascading a whole department away.
    parent_id   uuid        NULL REFERENCES shop_category(id) ON DELETE SET NULL,
    sort_order  int         NOT NULL DEFAULT 100,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_category_tenant ON shop_category (tenant_id, is_active, sort_order);

DROP TRIGGER IF EXISTS trg_shop_category_updated_at ON shop_category;
CREATE TRIGGER trg_shop_category_updated_at BEFORE UPDATE ON shop_category
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Suppliers ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shop_supplier (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name          text        NOT NULL,
    contact_name  text        NULL,
    email         text        NULL,
    phone         text        NULL,
    notes         text        NULL,
    is_active     boolean     NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_supplier_tenant ON shop_supplier (tenant_id, is_active);

DROP TRIGGER IF EXISTS trg_shop_supplier_updated_at ON shop_supplier;
CREATE TRIGGER trg_shop_supplier_updated_at BEFORE UPDATE ON shop_supplier
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Products (catalog entry) ─────────────────────────────────────────────────
-- The marketing object. Price and stock live on the variant below, not here. A product may be
-- sellable, rentable, or both; a repair part is simply a sellable product consumed on a work order.
CREATE TABLE IF NOT EXISTS shop_product (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    category_id   uuid        NULL REFERENCES shop_category(id) ON DELETE SET NULL,
    supplier_id   uuid        NULL REFERENCES shop_supplier(id) ON DELETE SET NULL,
    name          text        NOT NULL,
    description   text        NULL,
    brand         text        NULL,
    image_url     text        NULL,
    is_sellable   boolean     NOT NULL DEFAULT true,
    is_rentable   boolean     NOT NULL DEFAULT false,
    is_active     boolean     NOT NULL DEFAULT true,
    sort_order    int         NOT NULL DEFAULT 100,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    -- A product no one can do anything with is a data-entry mistake, not a valid state.
    CONSTRAINT chk_shop_product_usable CHECK (is_sellable OR is_rentable)
);
-- tax_category_id is added with the retail-sale migration (tax only matters at checkout), so the
-- shop_tax_category table and its FK land together rather than leaving a dangling column here.
CREATE INDEX IF NOT EXISTS idx_shop_product_tenant   ON shop_product (tenant_id, is_active, sort_order);
CREATE INDEX IF NOT EXISTS idx_shop_product_category ON shop_product (category_id) WHERE category_id IS NOT NULL;

DROP TRIGGER IF EXISTS trg_shop_product_updated_at ON shop_product;
CREATE TRIGGER trg_shop_product_updated_at BEFORE UPDATE ON shop_product
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Variants (the SKU) ───────────────────────────────────────────────────────
-- Where price, cost, and stock live. Every product has at least one variant (a default). A variant
-- that is sellable carries sale_price_cents; a rentable one carries daily_rate_cents + deposit_cents;
-- a variant can be both.
CREATE TABLE IF NOT EXISTS shop_variant (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    product_id        uuid        NOT NULL REFERENCES shop_product(id) ON DELETE CASCADE,
    sku               text        NULL,
    barcode           text        NULL,
    -- Attribute columns, frozen onto sale/rental lines at purchase (per event_extra_variant), so
    -- later catalog edits never rewrite what a receipt said.
    size              text        NULL,
    color             text        NULL,
    gender            text        NULL,
    sale_price_cents  int         NULL CHECK (sale_price_cents IS NULL OR sale_price_cents >= 0),
    daily_rate_cents  int         NULL CHECK (daily_rate_cents IS NULL OR daily_rate_cents >= 0),
    deposit_cents     int         NOT NULL DEFAULT 0 CHECK (deposit_cents >= 0),
    -- Last cost paid, updated on receiving. Feeds margin and COGS.
    cost_cents        int         NULL CHECK (cost_cents IS NULL OR cost_cents >= 0),
    -- 'pool'       = fungible units counted by stock_on_hand (helmets, tubes, apparel, parts).
    -- 'serialized' = distinct units tracked as shop_item rows (bikes); availability is the count of
    --                available items, and stock_on_hand is left at 0 for these.
    tracking_kind     text        NOT NULL DEFAULT 'pool' CHECK (tracking_kind IN ('pool','serialized')),
    stock_on_hand     int         NOT NULL DEFAULT 0,
    is_active         boolean     NOT NULL DEFAULT true,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_variant_product ON shop_variant (product_id, is_active);
CREATE INDEX IF NOT EXISTS idx_shop_variant_tenant  ON shop_variant (tenant_id);
-- SKU and barcode are scanned at the register and when receiving, so they must resolve to one
-- variant. Unique per tenant, case-insensitive for SKU, only where present.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_variant_sku
    ON shop_variant (tenant_id, lower(sku)) WHERE sku IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_variant_barcode
    ON shop_variant (tenant_id, barcode) WHERE barcode IS NOT NULL;
-- One variant per attribute combination within a product (COALESCE so NULLs don't read as distinct).
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_variant_attrs
    ON shop_variant (product_id, COALESCE(size,''), COALESCE(color,''), COALESCE(gender,''));

DROP TRIGGER IF EXISTS trg_shop_variant_updated_at ON shop_variant;
CREATE TRIGGER trg_shop_variant_updated_at BEFORE UPDATE ON shop_variant
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Serialized items (distinct physical units) ───────────────────────────────
-- One row per physical unit of a serialized variant. The neutral successor to rental_item, with a
-- status spanning the whole life: sellable, rentable, or out of service.
CREATE TABLE IF NOT EXISTS shop_item (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    variant_id          uuid        NOT NULL REFERENCES shop_variant(id) ON DELETE CASCADE,
    label               text        NOT NULL,   -- "Trek Fuel #3"
    serial              text        NULL,
    notes               text        NULL,
    -- 'available'  = in stock, sellable/rentable
    -- 'rented_out' = currently on a rental
    -- 'sold'       = sold retail (kept for history; ON DELETE RESTRICT from sale lines later)
    -- 'maintenance'= temporarily out of service
    -- 'retired'    = permanently out, retained for history
    status              text        NOT NULL DEFAULT 'available'
                        CHECK (status IN ('available','rented_out','sold','maintenance','retired')),
    acquired_cost_cents int         NULL CHECK (acquired_cost_cents IS NULL OR acquired_cost_cents >= 0),
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_item_variant ON shop_item (variant_id, status);
-- Tenant-scoped list ("all my serialized units"): the serial unique index is partial, so a
-- serial-less item wouldn't be covered by it.
CREATE INDEX IF NOT EXISTS idx_shop_item_tenant  ON shop_item (tenant_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_item_serial
    ON shop_item (tenant_id, lower(serial)) WHERE serial IS NOT NULL;

DROP TRIGGER IF EXISTS trg_shop_item_updated_at ON shop_item;
CREATE TRIGGER trg_shop_item_updated_at BEFORE UPDATE ON shop_item
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Stock movement ledger (append-only audit) ────────────────────────────────
-- Every change to stock writes a row here. stock_on_hand on the variant is a cached read value kept
-- in lockstep; this is the source of truth for "why is it 3, not 5?". No updated_at, no trigger:
-- movements are immutable facts, corrected by a new offsetting movement, never edited.
CREATE TABLE IF NOT EXISTS shop_stock_movement (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    variant_id         uuid        NOT NULL REFERENCES shop_variant(id) ON DELETE CASCADE,
    item_id            uuid        NULL REFERENCES shop_item(id) ON DELETE SET NULL,
    delta              int         NOT NULL CHECK (delta <> 0),  -- signed: +received, -sold/-rented
    reason             text        NOT NULL CHECK (reason IN
                           ('receive','sale','rental_out','rental_return','repair_consume',
                            'adjustment','stocktake','transfer')),
    -- What caused it, for drill-down. reference_kind e.g. 'purchase_order','shop_sale','shop_rental',
    -- 'shop_work_order'. Untyped id because the referent differs per kind.
    reference_kind     text        NULL,
    reference_id       uuid        NULL,
    unit_cost_cents    int         NULL CHECK (unit_cost_cents IS NULL OR unit_cost_cents >= 0),
    note               text        NULL,
    created_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_stock_movement_variant ON shop_stock_movement (tenant_id, variant_id, created_at);
CREATE INDEX IF NOT EXISTS idx_shop_stock_movement_ref     ON shop_stock_movement (reference_kind, reference_id)
    WHERE reference_id IS NOT NULL;

-- ── Purchase orders + lines (receiving) ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS shop_purchase_order (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    supplier_id        uuid        NULL REFERENCES shop_supplier(id) ON DELETE SET NULL,
    reference          text        NULL,   -- vendor PO number / internal ref
    -- open      = draft, editable
    -- ordered   = sent to supplier
    -- partial   = some lines received
    -- received  = fully received
    -- cancelled = abandoned
    status             text        NOT NULL DEFAULT 'open'
                       CHECK (status IN ('open','ordered','partial','received','cancelled')),
    notes              text        NULL,
    ordered_at         timestamptz NULL,
    expected_at        date        NULL,
    received_at        timestamptz NULL,
    created_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_purchase_order_tenant ON shop_purchase_order (tenant_id, status);

DROP TRIGGER IF EXISTS trg_shop_purchase_order_updated_at ON shop_purchase_order;
CREATE TRIGGER trg_shop_purchase_order_updated_at BEFORE UPDATE ON shop_purchase_order
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_po_line (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    po_id              uuid        NOT NULL REFERENCES shop_purchase_order(id) ON DELETE CASCADE,
    -- RESTRICT: a variant that appears on a PO can be deactivated but not deleted out from under it,
    -- so receiving history stays intact.
    variant_id         uuid        NOT NULL REFERENCES shop_variant(id) ON DELETE RESTRICT,
    quantity_ordered   int         NOT NULL CHECK (quantity_ordered > 0),
    quantity_received  int         NOT NULL DEFAULT 0 CHECK (quantity_received >= 0),
    unit_cost_cents    int         NOT NULL CHECK (unit_cost_cents >= 0),
    created_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_po_line_po      ON shop_po_line (po_id);
CREATE INDEX IF NOT EXISTS idx_shop_po_line_variant ON shop_po_line (variant_id);
