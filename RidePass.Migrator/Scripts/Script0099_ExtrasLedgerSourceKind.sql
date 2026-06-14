-- Allow 'extras' as a tenant_ledger_entry source_kind so gate-fee / add-on sales
-- (including the entire spectator Gate Fee flow) record a sale ledger row and count
-- toward tenant balance + payouts. Previously extras wrote no ledger entry at all,
-- so that revenue silently never reached the tenant's payout. 'rental' was already
-- permitted but the finalizer wasn't writing it; the code fix is separate.
ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
    CHECK (source_kind IS NULL OR source_kind IN
        ('pass','event_ticket','season_pass','rental','membership','extras'));
