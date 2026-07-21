-- Store credit phase 2: the F&B POS takes credit as a tender, same shape as the shop register
-- (Script0193): total_cents stays the full order value, the money path (cash or card-present PI)
-- collects total minus credit, and the ledger entry books only what was actually collected.
-- The redeem entry references 'concession_sale' so a failed payment or a refund hands it back.
--
-- Additive + idempotent.

ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS credit_applied_cents int NOT NULL DEFAULT 0;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS credit_account_id uuid NULL REFERENCES tenant_credit_account(id) ON DELETE SET NULL;
