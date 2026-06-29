-- Quick-service F&B: turn concessions from a card-only impulse vendor into a counter POS.
-- Workflow: customer orders at the counter, gets an order number, the worker calls the number
-- at pickup. Adds order numbers, structured modifiers, cash + tips, a cook/kitchen queue with
-- per-line + per-station status, and refunds.

-- ── Stations (fryer / grill / drinks ...) ────────────────────────────────────
-- Zero rows = a single default queue; tenants add stations to split the cook screen later.
CREATE TABLE concession_station (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id  uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name       text        NOT NULL,
    sort_order int         NOT NULL DEFAULT 0,
    is_active  boolean     NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_concession_station_tenant ON concession_station (tenant_id, is_active, sort_order);

-- Which station prepares this product. NULL = the default queue.
ALTER TABLE concession_product ADD COLUMN IF NOT EXISTS station_id uuid NULL
    REFERENCES concession_station(id) ON DELETE SET NULL;

-- ── Structured modifiers (groups + options) ──────────────────────────────────
CREATE TABLE concession_modifier_group (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    min_select  int         NOT NULL DEFAULT 0 CHECK (min_select >= 0),
    max_select  int         NULL CHECK (max_select IS NULL OR max_select >= 1),   -- NULL = unlimited
    is_required boolean     NOT NULL DEFAULT false,
    sort_order  int         NOT NULL DEFAULT 0,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_concession_mod_group_tenant ON concession_modifier_group (tenant_id, is_active, sort_order);

CREATE TABLE concession_modifier_option (
    id                uuid    PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id          uuid    NOT NULL REFERENCES concession_modifier_group(id) ON DELETE CASCADE,
    name              text    NOT NULL,
    price_delta_cents int     NOT NULL DEFAULT 0,
    sort_order        int     NOT NULL DEFAULT 0,
    is_active         boolean NOT NULL DEFAULT true
);
CREATE INDEX idx_concession_mod_option_group ON concession_modifier_option (group_id, is_active, sort_order);

-- Which modifier groups apply to a product (ordered).
CREATE TABLE concession_product_modifier_group (
    product_id uuid NOT NULL REFERENCES concession_product(id) ON DELETE CASCADE,
    group_id   uuid NOT NULL REFERENCES concession_modifier_group(id) ON DELETE CASCADE,
    sort_order int  NOT NULL DEFAULT 0,
    PRIMARY KEY (product_id, group_id)
);

-- ── Sale: order number, fulfillment status, tip, payment method, direct-charge snapshot ──
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS order_number int NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS fulfillment_status text NOT NULL DEFAULT 'active'
    CHECK (fulfillment_status IN ('active', 'ready', 'completed'));
-- Backfill: every concession sale that already exists predates the kitchen/cook-screen feature and
-- is long since handed over, so mark them completed. Without this, all historical paid sales would
-- appear on the cook screen (which shows status='paid' AND fulfillment_status <> 'completed').
UPDATE concession_sale SET fulfillment_status = 'completed' WHERE fulfillment_status = 'active';
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS tip_cents int NOT NULL DEFAULT 0 CHECK (tip_cents >= 0);
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS payment_method text NOT NULL DEFAULT 'stripe'
    CHECK (payment_method IN ('stripe', 'stripe_direct', 'cash'));
-- Connected account a direct (own-Stripe) card sale was charged on, so refunds act on the right
-- account regardless of the tenant's current mode (mirrors the other purchase tables).
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;
-- Kitchen reads active (not-yet-completed) orders for the tenant.
CREATE INDEX IF NOT EXISTS idx_concession_sale_fulfillment
    ON concession_sale (tenant_id, fulfillment_status) WHERE fulfillment_status <> 'completed';

-- ── Sale line: station snapshot, prep status, notes ──────────────────────────
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS station_id uuid NULL
    REFERENCES concession_station(id) ON DELETE SET NULL;
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS prep_status text NOT NULL DEFAULT 'queued'
    CHECK (prep_status IN ('queued', 'in_progress', 'ready'));
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS notes text NULL;
CREATE INDEX IF NOT EXISTS idx_concession_sale_line_prep ON concession_sale_line (prep_status);

-- Frozen modifier selections per line (snapshots so catalog edits don't rewrite history).
CREATE TABLE concession_sale_line_modifier (
    id                         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    sale_line_id               uuid NOT NULL REFERENCES concession_sale_line(id) ON DELETE CASCADE,
    modifier_option_id         uuid NULL REFERENCES concession_modifier_option(id) ON DELETE SET NULL,
    group_name_snapshot        text NOT NULL,
    option_name_snapshot       text NOT NULL,
    price_delta_cents_snapshot int  NOT NULL DEFAULT 0
);
CREATE INDEX idx_concession_sale_line_modifier_line ON concession_sale_line_modifier (sale_line_id);

-- ── Per-tenant daily order-number sequence (resets each UTC calendar day) ─────
-- App assigns the next number with an atomic upsert (INSERT ... ON CONFLICT DO UPDATE RETURNING),
-- which is safe under concurrent cashiers.
CREATE TABLE concession_order_counter (
    tenant_id     uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    business_date date NOT NULL,
    last_number   int  NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, business_date)
);
