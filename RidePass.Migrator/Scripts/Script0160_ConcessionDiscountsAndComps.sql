-- Discounts and comps for the Food & Beverage POS. Cashiers need to knock money off an order every
-- day: a named preset ("10% off"), an arbitrary percent/dollar, a member perk for Season Pass and
-- LoamPass holders, or a full/partial comp ("Rider comp", "Employee meal", "Manager comp"). Comps and
-- arbitrary manual discounts are gated behind a manager PIN (see Script0161) and have to show up on a
-- void/comp report, so the discount is snapshotted on the sale (kind, label, comp reason, and the
-- manager who authorized it) alongside the existing tax snapshots.
--
-- The money math stays line-based so per-line tax snapshots remain exact: a discount reduces a line's
-- line_total_cents and its tax is recomputed on the net. An order-level discount is allocated across
-- the taxable lines. concession_sale.subtotal_cents keeps its meaning (GROSS, pre-discount) and the new
-- discount_cents holds the total taken off, so subtotal - discount + tax (+ tip) still reconciles.
--
-- Idempotent (rerunnable) and backwards-compatible: every column is additive and defaults to today's
-- behavior (no discount, member perks off, manual discounts requiring a manager), so existing receipts
-- and totals are unchanged until a tenant configures something or a cashier applies a discount.

-- Tenant-defined discount presets the POS shows as one-tap buttons (e.g. "Military 10%", "$2 off").
-- kind = 'percent' stores value in basis points (1000 = 10%); kind = 'amount' stores value in cents.
CREATE TABLE IF NOT EXISTS concession_discount_preset (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    kind        text        NOT NULL DEFAULT 'percent',  -- 'percent' | 'amount'
    value       int         NOT NULL DEFAULT 0,          -- bps when percent, cents when amount
    is_active   boolean     NOT NULL DEFAULT true,
    sort_order  int         NOT NULL DEFAULT 0,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_concession_discount_preset_tenant ON concession_discount_preset (tenant_id);

-- Tenant-defined comp reasons. default_kind = 'full' comps the whole price; 'percent'/'amount' set a
-- default partial value (bps / cents) the cashier can still apply as-is. Comps always require a manager.
CREATE TABLE IF NOT EXISTS concession_comp_reason (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name          text        NOT NULL,
    default_kind  text        NOT NULL DEFAULT 'full',   -- 'full' | 'percent' | 'amount'
    default_value int         NOT NULL DEFAULT 0,         -- bps when percent, cents when amount, ignored for full
    is_active     boolean     NOT NULL DEFAULT true,
    sort_order    int         NOT NULL DEFAULT 0,
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_concession_comp_reason_tenant ON concession_comp_reason (tenant_id);

-- Member-perk discount config + the manual-discount manager gate, on the per-tenant settings row.
-- *_kind = 'percent' (value in bps) or 'amount' (value in cents). Perks are off until a tenant turns
-- them on. require_manager_for_manual_discount defaults true so an arbitrary discount needs a PIN.
ALTER TABLE concession_menu_settings
    ADD COLUMN IF NOT EXISTS season_pass_discount_enabled boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS season_pass_discount_kind    text    NOT NULL DEFAULT 'percent',
    ADD COLUMN IF NOT EXISTS season_pass_discount_value   int     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS loampass_discount_enabled    boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS loampass_discount_kind       text    NOT NULL DEFAULT 'percent',
    ADD COLUMN IF NOT EXISTS loampass_discount_value      int     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS require_manager_for_manual_discount boolean NOT NULL DEFAULT true;

-- Discount snapshot on the order. subtotal_cents stays GROSS (pre-discount); discount_cents is the total
-- knocked off; tax_cents/total_cents already reflect the net. discount_kind/label describe what was
-- applied for receipts + reporting; comp_* and authorized_by_* capture the manager-approved comp trail.
ALTER TABLE concession_sale
    ADD COLUMN IF NOT EXISTS discount_cents       int  NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_kind        text NULL,
    ADD COLUMN IF NOT EXISTS discount_label       text NULL,
    ADD COLUMN IF NOT EXISTS comp_reason_id       uuid NULL REFERENCES concession_comp_reason(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS comp_reason_label    text NULL,
    ADD COLUMN IF NOT EXISTS authorized_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS authorized_by_name   text NULL;
-- Reporting reads comps by date; index the comped sales.
CREATE INDEX IF NOT EXISTS idx_concession_sale_comp
    ON concession_sale (tenant_id, created_at) WHERE comp_reason_id IS NOT NULL;

-- Per-line discount snapshot. line_total_cents holds the NET (after discount); discount_cents is what
-- was taken off that line (its own line discount plus any allocated share of an order-level discount).
ALTER TABLE concession_sale_line
    ADD COLUMN IF NOT EXISTS discount_cents int  NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_kind  text NULL,
    ADD COLUMN IF NOT EXISTS discount_label text NULL;
