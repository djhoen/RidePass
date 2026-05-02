-- Idempotency for dispute_loss ledger entries: a given source purchase can only have one
-- dispute_loss entry. Webhook retries / multiple charge.dispute.closed events for the same
-- chargeback won't double-write.
CREATE UNIQUE INDEX uk_tenant_ledger_entry_dispute_loss_per_source
    ON tenant_ledger_entry (tenant_id, source_kind, source_id)
    WHERE entry_kind = 'dispute_loss' AND source_kind IS NOT NULL AND source_id IS NOT NULL;
