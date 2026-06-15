-- Email campaign sends are billed by deducting from the tenant's payout, not by a separate
-- Stripe charge: SendCampaignHandler writes a negative tenant_ledger_entry
-- (entry_kind='email_charge') that MonthlyPayoutDrafter already nets into the payout
-- (it sums every entry_kind <> 'sale' into total_adjustment_cents).
--
-- This also closes a latent gap: SmsBillingPayoutAttacher writes entry_kind='sms_charge'
-- with source_kind='tenant_billing_event', and NEITHER was permitted by the CHECK
-- constraints, so the SMS billing sweep would have thrown the first time it ran (it never
-- has, no SMS has been billed yet). Allow both kinds here so SMS billing works when it goes live.

ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_entry_kind_check;
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_entry_kind_check
    CHECK (entry_kind IN
        ('sale', 'refund', 'dispute_loss', 'dispute_fee', 'adjustment', 'sms_charge', 'email_charge'));

ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
    CHECK (source_kind IS NULL OR source_kind IN
        ('pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras', 'concession',
         'tenant_billing_event', 'email_campaign'));

-- Idempotency: at most one email charge per campaign, so a handler retry can't double-bill.
CREATE UNIQUE INDEX uk_ledger_email_charge
    ON tenant_ledger_entry (tenant_id, source_kind, source_id)
    WHERE entry_kind = 'email_charge' AND source_id IS NOT NULL;
