-- Refund ledger rows must be idempotent per purchase, like 'sale', 'dispute_loss',
-- 'dispute_fee', and 'email_charge' already are.
--
-- PurchaseController.Refund inserts a negative 'refund' entry and already catches the
-- 23505 unique violation, but there was no partial unique index for entry_kind='refund'
-- for that catch to fire on. So a replayed/concurrent refund (the Stripe refund itself is
-- idempotent via its key, but the ledger insert was not) could write a second negative row
-- and double-debit the tenant's balance. This adds the missing index.

-- Remove any duplicate refund rows that already slipped in, keeping the earliest per source,
-- so the unique index can be built. Each removed row was an erroneous double-debit, so deleting
-- it restores the tenant's balance to the correct single refund.
DELETE FROM tenant_ledger_entry t
USING (
    SELECT id,
           row_number() OVER (
               PARTITION BY tenant_id, source_kind, source_id
               ORDER BY occurred_at_utc, id
           ) AS rn
    FROM tenant_ledger_entry
    WHERE entry_kind = 'refund' AND source_kind IS NOT NULL AND source_id IS NOT NULL
) d
WHERE t.id = d.id AND d.rn > 1;

-- One 'refund' entry per source purchase (mirrors uk_tenant_ledger_entry_sale_per_source).
-- A purchase can only be refunded once (the Refund endpoint requires status='paid' and flips
-- it away), so this never blocks a legitimate refund.
CREATE UNIQUE INDEX IF NOT EXISTS uk_tenant_ledger_entry_refund_per_source
    ON tenant_ledger_entry (tenant_id, source_kind, source_id)
    WHERE entry_kind = 'refund' AND source_kind IS NOT NULL AND source_id IS NOT NULL;
