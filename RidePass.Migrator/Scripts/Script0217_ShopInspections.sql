-- Multi-point inspection.
--
-- A graded, component-by-component check of a bike, attached to the BIKE (not just the ticket) so
-- grading history accrues per machine across visits. This is the feature Ascend built its service
-- module around, and it does three jobs at once: quality control, a trust artifact the customer can
-- read, and the shop's best upsell surface (a yellow "monitor" item today is next visit's sale).
--
-- Three tables:
--   shop_inspection_template       the checklist definition (what we check)
--   shop_inspection_template_item  its rows, grouped ("Drivetrain" > "Chain wear")
--   shop_inspection / _result      a performed inspection and its per-item grades
--
-- Results SNAPSHOT the group and item labels rather than only pointing at the template row. Editing
-- or deleting a checklist item later must never rewrite what a past inspection said the mechanic
-- checked — same principle as frozen prices on a sale line.

-- ── Template ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shop_inspection_template (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    is_default  boolean     NOT NULL DEFAULT false,
    is_active   boolean     NOT NULL DEFAULT true,
    sort_order  int         NOT NULL DEFAULT 100,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_insp_template_tenant
    ON shop_inspection_template (tenant_id, is_active, sort_order);
-- At most one default per tenant, so "which checklist do I start with" always has one answer.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_insp_template_default
    ON shop_inspection_template (tenant_id) WHERE is_default;

DROP TRIGGER IF EXISTS trg_shop_insp_template_updated_at ON shop_inspection_template;
CREATE TRIGGER trg_shop_insp_template_updated_at BEFORE UPDATE ON shop_inspection_template
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_inspection_template_item (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    template_id uuid        NOT NULL REFERENCES shop_inspection_template(id) ON DELETE CASCADE,
    group_label text        NOT NULL,      -- "Drivetrain"
    label       text        NOT NULL,      -- "Chain wear"
    sort_order  int         NOT NULL DEFAULT 100,
    is_active   boolean     NOT NULL DEFAULT true
);
CREATE INDEX IF NOT EXISTS idx_shop_insp_template_item
    ON shop_inspection_template_item (template_id, sort_order);

-- ── A performed inspection ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shop_inspection (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- The bike is the anchor: history accrues per machine, not per ticket.
    customer_bike_id    uuid        NOT NULL REFERENCES shop_customer_bike(id) ON DELETE CASCADE,
    -- The job it was done on, when there was one. SET NULL so deleting a work order never
    -- destroys the inspection record of what the bike looked like.
    work_order_id       uuid        NULL REFERENCES shop_work_order(id) ON DELETE SET NULL,
    template_id         uuid        NULL REFERENCES shop_inspection_template(id) ON DELETE SET NULL,
    performed_by_user_id uuid       NULL REFERENCES users(id) ON DELETE SET NULL,
    -- draft = mechanic still working through it; complete = ready to show the customer.
    status              text        NOT NULL DEFAULT 'draft'
                                    CHECK (status IN ('draft','complete')),
    performed_at        timestamptz NOT NULL DEFAULT now(),
    -- Defaulted to +6 months by the API, matching the industry convention.
    next_service_date   date        NULL,
    summary_notes       text        NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_inspection_bike
    ON shop_inspection (customer_bike_id, performed_at DESC);
CREATE INDEX IF NOT EXISTS idx_shop_inspection_wo
    ON shop_inspection (work_order_id) WHERE work_order_id IS NOT NULL;

DROP TRIGGER IF EXISTS trg_shop_inspection_updated_at ON shop_inspection;
CREATE TRIGGER trg_shop_inspection_updated_at BEFORE UPDATE ON shop_inspection
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_inspection_result (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    inspection_id    uuid NOT NULL REFERENCES shop_inspection(id) ON DELETE CASCADE,
    -- Nullable: the item may have been deleted from the template since, and a mechanic can add an
    -- ad-hoc row for something the checklist doesn't cover.
    template_item_id uuid NULL REFERENCES shop_inspection_template_item(id) ON DELETE SET NULL,
    -- Snapshotted so later template edits can't rewrite history.
    group_label      text NOT NULL,
    label            text NOT NULL,
    -- The colour scale every shop already speaks: green / yellow / red, plus not-applicable.
    --   good      = fine, no action
    --   monitor   = wearing, watch it (this is the upsell pipeline)
    --   attention = needs work now
    --   na        = doesn't apply to this bike
    rating           text NOT NULL DEFAULT 'na'
                     CHECK (rating IN ('good','monitor','attention','na')),
    notes            text NULL,
    sort_order       int  NOT NULL DEFAULT 100
);
CREATE INDEX IF NOT EXISTS idx_shop_inspection_result
    ON shop_inspection_result (inspection_id, sort_order);


-- ── Seed a default checklist for every existing tenant ──────────────────────
-- Guarded so a re-run doesn't add a second copy. New tenants get one lazily the first time they
-- open the inspection screen (the API creates it), which avoids a tenant-insert trigger for a
-- feature most tenants never turn on.
INSERT INTO shop_inspection_template (tenant_id, name, is_default)
SELECT t.id, 'Standard bike inspection', true
FROM tenant t
WHERE NOT EXISTS (
    SELECT 1 FROM shop_inspection_template x WHERE x.tenant_id = t.id AND x.is_default
);

INSERT INTO shop_inspection_template_item (template_id, group_label, label, sort_order)
SELECT tpl.id, v.group_label, v.label, v.sort_order
FROM shop_inspection_template tpl
CROSS JOIN (VALUES
    ('Wheels & tires', 'Tire wear and pressure',        10),
    ('Wheels & tires', 'Wheel true',                    20),
    ('Wheels & tires', 'Spoke tension',                 30),
    ('Wheels & tires', 'Hub bearings',                  40),
    ('Wheels & tires', 'Rim / rotor wear',              50),
    ('Drivetrain',     'Chain wear',                   110),
    ('Drivetrain',     'Cassette and chainrings',      120),
    ('Drivetrain',     'Derailleur adjustment',        130),
    ('Drivetrain',     'Shift cables and housing',     140),
    ('Drivetrain',     'Bottom bracket',               150),
    ('Brakes',         'Pad wear',                     210),
    ('Brakes',         'Lever feel / reach',           220),
    ('Brakes',         'Cables, hoses, fluid',         230),
    ('Frame & fork',   'Frame condition',              310),
    ('Frame & fork',   'Headset',                      320),
    ('Frame & fork',   'Fork / suspension',            330),
    ('Frame & fork',   'Pivot bearings',               340),
    ('Contact points', 'Saddle and seatpost',          410),
    ('Contact points', 'Handlebar and stem',           420),
    ('Contact points', 'Grips / bar tape',             430),
    ('Contact points', 'Pedals',                       440),
    ('Safety',         'Quick releases / thru-axles',  510),
    ('Safety',         'Bolt torque',                  520),
    ('Safety',         'Lights and reflectors',        530)
) AS v(group_label, label, sort_order)
WHERE tpl.is_default
  AND NOT EXISTS (
    SELECT 1 FROM shop_inspection_template_item i WHERE i.template_id = tpl.id
);
