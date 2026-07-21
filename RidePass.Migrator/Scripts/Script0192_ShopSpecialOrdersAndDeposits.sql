-- Bike shop: special orders + repair deposits (the last big Lightspeed parity gaps).
--
-- Special orders: a work-order part line can be placed on a supplier purchase order
-- (po_line_id). When that PO line is received, the app stamps arrived_at on the linked
-- work-order lines, consumes the parts for committed jobs, advances an awaiting_parts
-- order, and emails the customer that their parts are in. A work order holding only
-- ordered parts (no labor) IS a standalone special order.
--
-- Deposits: a repair can take an up-front deposit. Staff email the customer a payment
-- link; deposit_request_token resolves the order publicly (unauthenticated, tenant
-- subdomain + token). The captured deposit books its own 'shop_wo_deposit' ledger entry
-- when paid, and bill-out credits it against the sale (shop_sale.deposit_applied_cents),
-- so the sale's ledger entry records only the remainder collected at pickup.
--
-- Additive + idempotent.

ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_cents            int         NOT NULL DEFAULT 0 CHECK (deposit_cents >= 0);
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_pi_id            text        NULL;
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_paid_at          timestamptz NULL;
-- 'stripe' | 'stripe_direct' | 'cash' (cash deposits are recorded at the counter)
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_payment_method   text        NULL;
-- Direct-charge tenants collect on their own Stripe account; a refund must go back
-- through that same account, so remember which one captured the deposit.
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_stripe_account_id text       NULL;
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_request_token    uuid        NOT NULL DEFAULT gen_random_uuid();
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_request_sent_at  timestamptz NULL;
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_refunded_at      timestamptz NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_wo_deposit_token ON shop_work_order (deposit_request_token);

-- Special-order linkage: the supplier PO line this part is riding on, stamped when it lands.
-- SET NULL so deleting a PO doesn't take the work-order line with it (the part still exists).
ALTER TABLE shop_work_order_line ADD COLUMN IF NOT EXISTS po_line_id uuid        NULL REFERENCES shop_po_line(id) ON DELETE SET NULL;
ALTER TABLE shop_work_order_line ADD COLUMN IF NOT EXISTS arrived_at timestamptz NULL;
CREATE INDEX IF NOT EXISTS idx_shop_wo_line_po_line
    ON shop_work_order_line (po_line_id) WHERE po_line_id IS NOT NULL;

-- Bill-out credit: how much of the sale's total was prepaid as the work order's deposit.
-- total_cents stays the full value of the job; the payment (cash or PI) collects the remainder.
ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS deposit_applied_cents int NOT NULL DEFAULT 0;

-- Ledger kind for the captured deposit (its own entry so the (source_kind, source_id)
-- sale-entry unique index never collides with the bill-out sale's entry).
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_source_kind_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
        CHECK (source_kind IS NULL OR source_kind IN (
            'pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras',
            'concession', 'tenant_billing_event', 'email_campaign', 'rental_deposit',
            'shop_sale', 'shop_rental', 'shop_rental_deposit', 'shop_wo_deposit'
        ));
END $$;
