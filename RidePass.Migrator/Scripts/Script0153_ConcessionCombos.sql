-- Combo meals: a combo is a concession_product (is_combo = true) whose price_cents is the bundle base
-- price. It has "slots" (choose-one groups like "Choose a side" / "Choose a drink"); each slot option
-- points at a real component product (+ optional variant for a "Large" upgrade) with an upcharge.
-- When sold, the combo is one parent sale line carrying the price, plus $0 child lines for each chosen
-- component so the cook screen routes them to stations and recipes deplete inventory normally.

ALTER TABLE concession_product ADD COLUMN is_combo boolean NOT NULL DEFAULT false;

-- Parent/child + combo marker on sale lines. Children reference their combo parent; the parent line is
-- the priced container (is_combo = true) and is not itself a cook task.
ALTER TABLE concession_sale_line ADD COLUMN parent_line_id uuid NULL
    REFERENCES concession_sale_line(id) ON DELETE CASCADE;
ALTER TABLE concession_sale_line ADD COLUMN is_combo boolean NOT NULL DEFAULT false;
CREATE INDEX idx_concession_sale_line_parent ON concession_sale_line (parent_line_id);

-- A choose-one (or choose-N) group within a combo, e.g. "Choose a side". Scoped via the combo product
-- (tenant-scoped), like the other concession product joins.
CREATE TABLE concession_combo_slot (
    id          uuid    PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id  uuid    NOT NULL REFERENCES concession_product(id) ON DELETE CASCADE,  -- the combo
    name        text    NOT NULL,
    is_required boolean NOT NULL DEFAULT true,
    sort_order  int     NOT NULL DEFAULT 0
);
CREATE INDEX idx_concession_combo_slot_product ON concession_combo_slot (product_id);

-- A candidate choice in a slot: a component product (+ optional variant for size), with an upcharge
-- (e.g. large fry/drink = +$0.79) and a default flag.
CREATE TABLE concession_combo_slot_option (
    id                  uuid    PRIMARY KEY DEFAULT gen_random_uuid(),
    slot_id             uuid    NOT NULL REFERENCES concession_combo_slot(id) ON DELETE CASCADE,
    component_product_id uuid   NOT NULL REFERENCES concession_product(id) ON DELETE CASCADE,
    variant_id          uuid    NULL REFERENCES concession_variant(id) ON DELETE CASCADE,
    price_delta_cents   int     NOT NULL DEFAULT 0,
    is_default          boolean NOT NULL DEFAULT false,
    sort_order          int     NOT NULL DEFAULT 0
);
CREATE INDEX idx_concession_combo_slot_option_slot ON concession_combo_slot_option (slot_id);
