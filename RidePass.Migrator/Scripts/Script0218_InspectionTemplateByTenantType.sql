-- Inspection checklists must match the machine: MX vs MTB.
--
-- Script0217 seeded ONE bicycle checklist (spoke tension, bar tape, quick releases) for every
-- tenant regardless of tenant_type. That is wrong for a motocross track, where the equivalent list
-- is engine oil, air filter, fork seals and sprockets. This corrects the default per tenant_type.
--
-- Safety: a template that has already been USED is left alone. Rewriting the items under a
-- recorded inspection would be a lie about what was checked — and although results snapshot their
-- own labels (so history itself survives), silently swapping a shop's live checklist out from under
-- them is not ours to do. Only untouched seeds are corrected.

-- ── Motocross: replace the bicycle seed with a dirt-bike checklist ───────────
WITH untouched AS (
    SELECT tpl.id
    FROM shop_inspection_template tpl
    JOIN tenant t ON t.id = tpl.tenant_id
    WHERE tpl.is_default
      AND t.tenant_type = 'motocross'
      AND tpl.name = 'Standard bike inspection'          -- still the untouched seed name
      AND NOT EXISTS (SELECT 1 FROM shop_inspection i WHERE i.template_id = tpl.id)
)
DELETE FROM shop_inspection_template_item i
USING untouched u
WHERE i.template_id = u.id;

UPDATE shop_inspection_template tpl
SET name = 'Standard MX inspection'
FROM tenant t
WHERE t.id = tpl.tenant_id
  AND tpl.is_default
  AND t.tenant_type = 'motocross'
  AND tpl.name = 'Standard bike inspection'
  AND NOT EXISTS (SELECT 1 FROM shop_inspection i WHERE i.template_id = tpl.id);

INSERT INTO shop_inspection_template_item (template_id, group_label, label, sort_order)
SELECT tpl.id, v.group_label, v.label, v.sort_order
FROM shop_inspection_template tpl
JOIN tenant t ON t.id = tpl.tenant_id
CROSS JOIN (VALUES
    ('Engine',          'Engine oil level and condition',   10),
    ('Engine',          'Oil filter',                       20),
    ('Engine',          'Air filter',                       30),
    ('Engine',          'Coolant level',                    40),
    ('Engine',          'Radiators and hoses',              50),
    ('Engine',          'Spark plug',                       60),
    ('Engine',          'Valve clearance',                  70),
    ('Engine',          'Top-end hours',                    80),
    ('Engine',          'Exhaust / silencer packing',       90),
    ('Drivetrain',      'Chain wear and tension',          110),
    ('Drivetrain',      'Front and rear sprockets',        120),
    ('Drivetrain',      'Chain slider and rollers',        130),
    ('Drivetrain',      'Clutch free play and plates',     140),
    ('Suspension',      'Fork seals and oil',              210),
    ('Suspension',      'Fork action',                     220),
    ('Suspension',      'Shock seals and action',          230),
    ('Suspension',      'Linkage bearings',                240),
    ('Suspension',      'Swingarm bearings',               250),
    ('Suspension',      'Race sag',                        260),
    ('Brakes',          'Front and rear pads',             310),
    ('Brakes',          'Rotors',                          320),
    ('Brakes',          'Fluid and lines',                 330),
    ('Wheels & tires',  'Tire wear and pressure',          410),
    ('Wheels & tires',  'Spoke tension',                   420),
    ('Wheels & tires',  'Rim condition',                   430),
    ('Wheels & tires',  'Wheel bearings',                  440),
    ('Controls',        'Throttle action and cable',       510),
    ('Controls',        'Clutch lever and cable',          520),
    ('Controls',        'Grips and bar mounts',            530),
    ('Chassis',         'Frame and subframe',              610),
    ('Chassis',         'Steering head bearings',          620),
    ('Chassis',         'Footpegs and shifter',            630),
    ('Chassis',         'Bolt torque',                     640)
) AS v(group_label, label, sort_order)
WHERE tpl.is_default
  AND t.tenant_type = 'motocross'
  AND tpl.name = 'Standard MX inspection'
  AND NOT EXISTS (SELECT 1 FROM shop_inspection_template_item i WHERE i.template_id = tpl.id);


-- ── Mountain bike: the Script0217 seed was already right, just name it clearly ──
UPDATE shop_inspection_template tpl
SET name = 'Standard MTB inspection'
FROM tenant t
WHERE t.id = tpl.tenant_id
  AND tpl.is_default
  AND t.tenant_type = 'mountain_bike'
  AND tpl.name = 'Standard bike inspection';
