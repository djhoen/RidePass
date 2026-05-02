-- Add 'dispute_fee' as a recognized entry_kind so the chargeback fee Stripe charges RidePass
-- can be passed through to the tenant as a distinct ledger entry, separate from manual adjustments.
ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_entry_kind_check;
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_entry_kind_check
    CHECK (entry_kind IN ('sale', 'refund', 'dispute_loss', 'dispute_fee', 'adjustment'));

-- One dispute_fee per source — webhook retries / multiple charge.dispute.closed events can't double-charge.
CREATE UNIQUE INDEX uk_tenant_ledger_entry_dispute_fee_per_source
    ON tenant_ledger_entry (tenant_id, source_kind, source_id)
    WHERE entry_kind = 'dispute_fee' AND source_kind IS NOT NULL AND source_id IS NOT NULL;
