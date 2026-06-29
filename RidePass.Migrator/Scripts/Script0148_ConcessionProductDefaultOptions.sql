-- Per-product default modifier selections (e.g. a cheeseburger comes with lettuce + tomato by default).
-- Pure join, scoped via concession_product (which is tenant-scoped), mirroring concession_product_modifier_group.
CREATE TABLE concession_product_default_option (
    product_id         uuid NOT NULL REFERENCES concession_product(id) ON DELETE CASCADE,
    modifier_option_id uuid NOT NULL REFERENCES concession_modifier_option(id) ON DELETE CASCADE,
    PRIMARY KEY (product_id, modifier_option_id)
);
CREATE INDEX idx_concession_product_default_option_product ON concession_product_default_option (product_id);
