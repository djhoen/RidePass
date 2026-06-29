-- Combo meals reworked into a shared, tenant-level "make it a combo" upgrade with size tiers and
-- difference-priced substitutions. Any item flagged combo_available can be upgraded at the counter or
-- online: the customer picks a size tier (Regular/Large/XL...) and one option per slot (side, drink).
-- Premium substitutions are charged the price difference; cheaper subs never discount the combo.

-- Per-item flag: this entree can be made a combo. Replaces the old standalone is_combo product type.
ALTER TABLE concession_product ADD COLUMN combo_available boolean NOT NULL DEFAULT false;
ALTER TABLE concession_product DROP COLUMN is_combo;

-- The old per-product combo tables (Script0153) are superseded by the tenant-level definition below.
DROP TABLE IF EXISTS concession_combo_slot_option;
DROP TABLE IF EXISTS concession_combo_slot;

-- Size tiers for the combo (Regular/Large/XL...). price_cents is the upcharge added to the entree.
-- size_label matches a component variant's size so the side/drink resolve to that size at the tier.
CREATE TABLE concession_combo_tier (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text NOT NULL,
    size_label  text NULL,
    price_cents int  NOT NULL DEFAULT 0,
    sort_order  int  NOT NULL DEFAULT 0
);
CREATE INDEX idx_concession_combo_tier_tenant ON concession_combo_tier (tenant_id);

-- Choose-one slots in the combo (Side, Drink). Tenant-level, shared by every combo-available item.
CREATE TABLE concession_combo_slot (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text NOT NULL,
    is_required boolean NOT NULL DEFAULT true,
    sort_order  int  NOT NULL DEFAULT 0
);
CREATE INDEX idx_concession_combo_slot_tenant ON concession_combo_slot (tenant_id);

-- Candidate components in a slot. The included (is_default) option is covered by the tier price;
-- others are charged max(0, their price - the included price) at the chosen tier size.
CREATE TABLE concession_combo_slot_option (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    slot_id              uuid NOT NULL REFERENCES concession_combo_slot(id) ON DELETE CASCADE,
    component_product_id uuid NOT NULL REFERENCES concession_product(id) ON DELETE CASCADE,
    is_default           boolean NOT NULL DEFAULT false,
    sort_order           int  NOT NULL DEFAULT 0
);
CREATE INDEX idx_concession_combo_slot_option_slot ON concession_combo_slot_option (slot_id);

-- Snapshot the chosen tier name on the parent (entree) line for receipts / kitchen display.
ALTER TABLE concession_sale_line ADD COLUMN combo_tier text NULL;
