-- Tenant-customizable work-order statuses (colour-coded, reorderable, notify-on-entry), replacing
-- the hard-coded set on the UI. IMPORTANT: the seven canonical codes stay the behavioural backbone
-- that all automation reads and writes (parts consume on leaving 'estimate', reverse on 'cancelled',
-- terminal on 'picked_up', auto-advance from 'awaiting_parts', ready-notice on 'ready'). This table
-- customizes their LABEL, COLOUR, ORDER and notify flag, and lets a tenant add extra 'open' working
-- stages. It does not let a tenant remove or repurpose a built-in behaviour, so inventory logic is
-- untouched.
--
--   behavior: estimate | open | ready | done | cancelled  (maps a status to its system meaning)
--   is_builtin: the seven seeded rows; their code + behaviour are fixed, only presentation edits
--   is_default: the status a new work order starts in (exactly one)
--
-- Additive and rerunnable.

CREATE TABLE IF NOT EXISTS shop_work_order_status (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid NOT NULL REFERENCES tenant(id),
    code            text NOT NULL,
    name            text NOT NULL,
    color           text NOT NULL DEFAULT 'grey',
    behavior        text NOT NULL DEFAULT 'open'
                        CHECK (behavior IN ('estimate','open','ready','done','cancelled')),
    notify_customer boolean NOT NULL DEFAULT false,
    sort_order      int NOT NULL DEFAULT 100,
    is_builtin      boolean NOT NULL DEFAULT false,
    is_active       boolean NOT NULL DEFAULT true,
    is_default      boolean NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

-- Code is the value stored in shop_work_order.status; unique per tenant, case-insensitive.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_wo_status_code
    ON shop_work_order_status (tenant_id, lower(code));

-- Seed the seven built-ins for every existing tenant. New tenants get them lazily in code.
INSERT INTO shop_work_order_status
    (tenant_id, code, name, color, behavior, notify_customer, sort_order, is_builtin, is_default)
SELECT t.id, s.code, s.name, s.color, s.behavior, s.notify_customer, s.sort_order, true, s.is_default
FROM tenant t
CROSS JOIN (VALUES
    ('estimate',       'Estimate',         'grey',      'estimate',  false, 10, false),
    ('intake',         'Intake',           'blue-grey', 'open',      false, 20, true),
    ('awaiting_parts', 'Awaiting parts',   'warning',   'open',      false, 30, false),
    ('in_progress',    'In progress',      'indigo',    'open',      false, 40, false),
    ('ready',          'Ready for pickup', 'success',   'ready',     true,  50, false),
    ('picked_up',      'Picked up',        'primary',   'done',      false, 60, false),
    ('cancelled',      'Cancelled',        'error',     'cancelled', false, 70, false)
) AS s(code, name, color, behavior, notify_customer, sort_order, is_default)
ON CONFLICT (tenant_id, lower(code)) DO NOTHING;
