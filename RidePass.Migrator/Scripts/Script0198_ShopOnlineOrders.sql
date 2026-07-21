-- Shop ecommerce: riders buy from the public catalog page and pick up in store. Online buys
-- ride the existing shop_sale machinery end to end (PI, finalizer depletion, order number,
-- ledger, store credit); the only new state is which channel sold it and when the goods were
-- actually handed over.
--
-- Additive + idempotent.

ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS order_channel text NOT NULL DEFAULT 'counter'
    CHECK (order_channel IN ('counter', 'online'));
-- Stamped by staff when an online order is collected; counter sales never set it.
ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS picked_up_at timestamptz NULL;
CREATE INDEX IF NOT EXISTS idx_shop_sale_awaiting_pickup
    ON shop_sale (tenant_id, created_at DESC)
    WHERE order_channel = 'online' AND picked_up_at IS NULL;
