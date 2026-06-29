-- F&B ingredient inventory: stockable goods, per-product recipes (bill of materials), and stock takes
-- with variance vs theoretical usage depleted from sales.

-- Stockable goods (buns, patties, cups, candy bars). on_hand is the theoretical quantity, depleted by
-- sales via recipes and reconciled by stock takes.
CREATE TABLE concession_inventory_item (
    id          uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid          NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text          NOT NULL,
    unit        text          NOT NULL DEFAULT 'each',
    cost_cents  int           NOT NULL DEFAULT 0,            -- cost per unit
    on_hand     numeric(12,3) NOT NULL DEFAULT 0,
    is_active   boolean       NOT NULL DEFAULT true,
    created_at  timestamptz   NOT NULL DEFAULT now(),
    updated_at  timestamptz   NOT NULL DEFAULT now()
);
CREATE INDEX idx_concession_inventory_item_tenant ON concession_inventory_item (tenant_id);

-- Recipe: one unit of a product consumes `quantity` of an inventory item. Scoped via the product
-- (tenant-scoped), like the other concession product joins.
CREATE TABLE concession_recipe_item (
    product_id        uuid          NOT NULL REFERENCES concession_product(id) ON DELETE CASCADE,
    inventory_item_id uuid          NOT NULL REFERENCES concession_inventory_item(id) ON DELETE CASCADE,
    quantity          numeric(12,3) NOT NULL DEFAULT 0,
    PRIMARY KEY (product_id, inventory_item_id)
);
CREATE INDEX idx_concession_recipe_item_product ON concession_recipe_item (product_id);
CREATE INDEX idx_concession_recipe_item_item ON concession_recipe_item (inventory_item_id);

-- A stock take (physical count). Each line snapshots expected (theoretical) vs counted (actual).
CREATE TABLE concession_inventory_count (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    counted_by  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    note        text        NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_concession_inventory_count_tenant ON concession_inventory_count (tenant_id, created_at DESC);

CREATE TABLE concession_inventory_count_line (
    id                uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    count_id          uuid          NOT NULL REFERENCES concession_inventory_count(id) ON DELETE CASCADE,
    inventory_item_id uuid          NOT NULL REFERENCES concession_inventory_item(id) ON DELETE CASCADE,
    name_snapshot     text          NOT NULL,
    unit_snapshot     text          NOT NULL,
    unit_cost_cents   int           NOT NULL,
    expected_qty      numeric(12,3) NOT NULL,   -- theoretical on-hand at count time
    counted_qty       numeric(12,3) NOT NULL    -- physically counted; variance = counted - expected
);
CREATE INDEX idx_concession_inventory_count_line_count ON concession_inventory_count_line (count_id);
