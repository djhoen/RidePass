-- Sales tax for Food & Beverage: per-tenant tax categories (rate in basis points), a per-item
-- category, a tenant-level tax-inclusive pricing flag, and tax snapshots on sales + sale lines so
-- historical receipts/reports stay correct when rates change.
--
-- Idempotent (rerunnable) and backwards-compatible: every addition defaults to today's behavior
-- (0% tax, tax added on top), so existing all-in pricing is unchanged until a tenant sets a rate.

-- Tenant-scoped tax categories (e.g. "Prepared food" 8.25%, "Packaged" 2.9%, "Exempt" 0%).
CREATE TABLE IF NOT EXISTS concession_tax_category (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    rate_bps    int         NOT NULL DEFAULT 0,      -- basis points: 825 = 8.25%
    is_default  boolean     NOT NULL DEFAULT false,
    sort_order  int         NOT NULL DEFAULT 0,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_concession_tax_category_tenant ON concession_tax_category (tenant_id);
-- At most one default category per tenant.
CREATE UNIQUE INDEX IF NOT EXISTS ux_concession_tax_category_default
    ON concession_tax_category (tenant_id) WHERE is_default;

-- Per-item tax category (NULL = use the tenant default category).
ALTER TABLE concession_product
    ADD COLUMN IF NOT EXISTS tax_category_id uuid REFERENCES concession_tax_category(id) ON DELETE SET NULL;

-- Tenant-level: prices already include tax (back it out) vs add tax on top (default = on top).
ALTER TABLE concession_menu_settings
    ADD COLUMN IF NOT EXISTS prices_include_tax boolean NOT NULL DEFAULT false;

-- Tax snapshots on the sale + lines, frozen at checkout so later rate changes don't rewrite history.
ALTER TABLE concession_sale      ADD COLUMN IF NOT EXISTS tax_cents          int     NOT NULL DEFAULT 0;
ALTER TABLE concession_sale      ADD COLUMN IF NOT EXISTS prices_include_tax boolean NOT NULL DEFAULT false;
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS tax_cents          int     NOT NULL DEFAULT 0;
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS tax_rate_bps       int     NOT NULL DEFAULT 0;

-- Seed a default 0% category for tenants already using F&B (have a product or menu-settings row), so
-- the tax UI shows one editable row. 0% keeps today's all-in totals unchanged until a rate is set.
INSERT INTO concession_tax_category (tenant_id, name, rate_bps, is_default, sort_order)
SELECT t.id, 'Sales tax', 0, true, 0
FROM tenant t
WHERE (EXISTS (SELECT 1 FROM concession_product p WHERE p.tenant_id = t.id)
       OR EXISTS (SELECT 1 FROM concession_menu_settings m WHERE m.tenant_id = t.id))
  AND NOT EXISTS (SELECT 1 FROM concession_tax_category c WHERE c.tenant_id = t.id);
