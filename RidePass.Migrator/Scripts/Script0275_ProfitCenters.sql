-- Configurable PROFIT CENTERS: the per-tenant version of the static QboDepartments rollup.
--
-- QboDepartments hardcodes how the fine-grained QuickBooks revenue slots group into business
-- units (Tickets & Passes, Training Center, Food & Beverage, Bike Shop, Other). Highland's ask
-- ("we report on four departments, named OUR way") makes that grouping tenant configuration:
-- a track names its own centers and decides which revenue streams land in each. Reports and the
-- QuickBooks account-mapping screen then group by the tenant's centers; the journal entry itself
-- still posts per revenue slot, so nothing about the money path changes shape.
--
-- No backfill on purpose. Zero rows in profit_center means "use the built-in departments", which
-- is exactly today's behavior for every existing tenant. A tenant opts in from the new
-- Admin -> Settings -> Profit Centers page (or seeds the defaults from there and renames them).
--
-- profit_center_revenue_key carries no CHECK against the known revenue keys: the valid set is
-- QboAccountKeys.All, which grows with the code, and the controller validates against it on
-- write. A stale key row (from a build that knew a key this one doesn't) is harmless: resolution
-- falls back to the built-in department for any key without a live assignment.

CREATE TABLE IF NOT EXISTS profit_center (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id  uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name       text        NOT NULL,
    sort_order int         NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_profit_center_tenant_name ON profit_center (tenant_id, lower(name));
CREATE INDEX IF NOT EXISTS idx_profit_center_tenant ON profit_center (tenant_id, sort_order);

-- One row per (tenant, revenue slot) naming the center that slot's money reports under. A slot
-- with no row falls back to the built-in department. ON DELETE CASCADE from profit_center: when a
-- center is deleted its slots become unassigned (fall back), never orphaned.
CREATE TABLE IF NOT EXISTS profit_center_revenue_key (
    tenant_id        uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    revenue_key      text NOT NULL,
    profit_center_id uuid NOT NULL REFERENCES profit_center(id) ON DELETE CASCADE,
    PRIMARY KEY (tenant_id, revenue_key)
);
CREATE INDEX IF NOT EXISTS idx_profit_center_revenue_key_center ON profit_center_revenue_key (profit_center_id);
