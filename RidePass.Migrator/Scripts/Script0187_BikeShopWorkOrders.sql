-- Bike shop, Phase 4: repair work orders.
--
-- The service bench: a work order accrues labor lines and parts consumed from inventory, then
-- bills out through the normal shop_sale path at pickup so payment, tax, order numbers, ledger,
-- and refunds all ride the one register flow. Design per docs/bike-shop.md plus the Lightspeed
-- parity additions: an 'estimate' status (quote before committing), technician assignment, and an
-- awaiting_parts stage (the special-order case: job blocked until parts arrive on a PO).
--
-- Parts consume stock the moment they're added to the job (reason 'repair_consume'), not at
-- pickup, so on-hand reflects what's physically on the bench. Removing a line reverses with a
-- positive repair_consume movement. Because the parts were already consumed, the bill-out sale
-- must NOT deplete again: shop_sale.work_order_id marks such sales and DepleteForSale skips them.
--
-- The subject is either the shop's own unit (subject_item_id -> shop_item, e.g. servicing the
-- rental fleet) or the customer's own bike (free-text customer_bike_desc). Both first-class.
--
-- Additive + idempotent.

CREATE TABLE IF NOT EXISTS shop_work_order (
    id                    uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id             uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    customer_user_id      uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    customer_name         text        NOT NULL,
    customer_phone        text        NULL,
    customer_email        text        NULL,
    -- Fleet service: the shop's own serialized unit. RESTRICT would block retiring bikes with
    -- history; SET NULL keeps the order and loses only the link.
    subject_item_id       uuid        NULL REFERENCES shop_item(id) ON DELETE SET NULL,
    -- Customer's own bike, free text ("2022 Trek Fuel EX 8, black").
    customer_bike_desc    text        NULL,
    -- estimate       = quote only; nothing committed, parts NOT consumed
    -- intake         = accepted, queued
    -- awaiting_parts = blocked on parts (special order on a PO)
    -- in_progress    = on the bench
    -- ready          = done, awaiting pickup/payment
    -- picked_up      = billed out (sale_id set) and collected
    -- cancelled      = abandoned; any consumed parts must be reversed by the app
    status                text        NOT NULL DEFAULT 'intake'
                          CHECK (status IN ('estimate','intake','awaiting_parts','in_progress','ready','picked_up','cancelled')),
    assigned_tech_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    intake_notes          text        NULL,
    promised_at           date        NULL,   -- when the customer was told it'd be done
    -- The bill-out sale, created at pickup. SET NULL so deleting neither strands the other.
    sale_id               uuid        NULL REFERENCES shop_sale(id) ON DELETE SET NULL,
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_work_order_tenant ON shop_work_order (tenant_id, status, created_at);
CREATE INDEX IF NOT EXISTS idx_shop_work_order_tech
    ON shop_work_order (assigned_tech_user_id) WHERE assigned_tech_user_id IS NOT NULL;

DROP TRIGGER IF EXISTS trg_shop_work_order_updated_at ON shop_work_order;
CREATE TRIGGER trg_shop_work_order_updated_at BEFORE UPDATE ON shop_work_order
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_work_order_line (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    work_order_id  uuid        NOT NULL REFERENCES shop_work_order(id) ON DELETE CASCADE,
    line_kind      text        NOT NULL CHECK (line_kind IN ('labor','part')),
    -- labor: description required, variant NULL. part: variant required, description optional note.
    description    text        NULL,
    variant_id     uuid        NULL REFERENCES shop_variant(id) ON DELETE RESTRICT,
    quantity       int         NOT NULL DEFAULT 1 CHECK (quantity > 0),
    unit_price_cents int       NOT NULL CHECK (unit_price_cents >= 0),
    -- Whether this part's stock has been consumed (false while the order is an estimate, or after
    -- a cancel reversal). Labor lines stay false.
    consumed       boolean     NOT NULL DEFAULT false,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_shop_wo_line_shape CHECK (
        (line_kind = 'labor' AND description IS NOT NULL AND variant_id IS NULL)
        OR (line_kind = 'part' AND variant_id IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_shop_wo_line_wo ON shop_work_order_line (work_order_id);

-- Bill-out marker: a sale born from a work order must not deplete stock again (its parts were
-- consumed when added to the job). DepleteForSale checks this column and no-ops.
ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS work_order_id uuid NULL;

-- Labor lines on a bill-out sale have no catalog variant, so the column loosens from NOT NULL.
-- Ordinary register lines still always carry one (enforced in code); depletion is skipped for
-- work-order sales entirely, so a NULL variant can never reach the stock path.
ALTER TABLE shop_sale_line ALTER COLUMN variant_id DROP NOT NULL;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_shop_sale_work_order') THEN
        ALTER TABLE shop_sale
            ADD CONSTRAINT fk_shop_sale_work_order
            FOREIGN KEY (work_order_id) REFERENCES shop_work_order(id) ON DELETE SET NULL;
    END IF;
END $$;
