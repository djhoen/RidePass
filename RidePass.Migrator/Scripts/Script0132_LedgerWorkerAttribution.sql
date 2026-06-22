-- Worker attribution on the ledger, so the operator app's cash reconciliation can compute
-- each worker's expected cash and refund volume.
--
-- sold_by_user_id is the cashier on a 'sale' row and the refunding staffer on a 'refund'
-- row. Nullable: online / webhook-finalized sales have no cashier, and that's fine because
-- only counter cash and refunds feed reconciliation. Best-effort backfill below covers the
-- main cash path (counter event-ticket sales) from the source purchase's cashier.

ALTER TABLE tenant_ledger_entry
    ADD COLUMN IF NOT EXISTS sold_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

-- Reconciliation reads by (tenant, worker, time); partial keeps it lean since most historical
-- rows have no cashier.
CREATE INDEX IF NOT EXISTS idx_tenant_ledger_entry_worker
    ON tenant_ledger_entry (tenant_id, sold_by_user_id, occurred_at_utc)
    WHERE sold_by_user_id IS NOT NULL;

-- Backfill the main cash path: event-ticket entries inherit the purchase's cashier.
UPDATE tenant_ledger_entry le
   SET sold_by_user_id = etp.sold_by_user_id
  FROM event_ticket_purchase etp
 WHERE le.source_kind = 'event_ticket'
   AND le.source_id = etp.id
   AND le.sold_by_user_id IS NULL
   AND etp.sold_by_user_id IS NOT NULL;
