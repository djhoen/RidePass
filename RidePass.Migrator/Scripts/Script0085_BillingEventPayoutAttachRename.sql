-- Rename columns to reflect the netting model: SMS costs are settled by
-- attaching tenant_billing_event rows as negative tenant_ledger_entry rows
-- (which the monthly payout drafter then sweeps into the payout's
-- total_adjustment_cents). Stripe Meters is NOT the integration path.
--
-- Safe to rename + retype: tenant_billing_event was added in Script0084
-- and no production rows exist yet (the StatusCallback webhook just landed
-- and hasn't received traffic).

ALTER TABLE tenant_billing_event
    RENAME COLUMN pushed_to_stripe_at_utc TO pushed_to_payout_at_utc;

ALTER TABLE tenant_billing_event
    RENAME COLUMN stripe_meter_event_id TO payout_entry_id;

-- Was text (Stripe meter_event id). Now uuid (the tenant_ledger_entry.id we
-- inserted on settle). Empty column → safe to retype with USING NULL.
ALTER TABLE tenant_billing_event
    ALTER COLUMN payout_entry_id TYPE uuid USING NULL;

-- Index predicate auto-tracks the renamed column; rename the index for clarity.
ALTER INDEX ix_tenant_billing_event_pending_push
    RENAME TO ix_tenant_billing_event_pending_attach;
