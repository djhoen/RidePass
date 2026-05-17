-- Stripe Connect (Standard) lets tenants connect their own Stripe account so charges
-- land directly in their bank. RidePass takes its service charge as application_fee_amount
-- on each PaymentIntent. Tenants without a connected account stay on the platform
-- Stripe account (the existing flow) — connection is opt-in.
--
-- status values:
--   pending     — onboarding link issued; tenant hasn't completed Stripe's KYC
--   active      — Stripe says the account is ready to charge + payout
--   restricted  — Stripe flagged something (KYC incomplete, capability disabled, etc.)
--                 We fall back to the platform account for charges in this state.

ALTER TABLE tenant
    ADD COLUMN stripe_connect_account_id text NULL,
    ADD COLUMN stripe_connect_status     text NULL
        CHECK (stripe_connect_status IS NULL OR stripe_connect_status IN ('pending','active','restricted'));

CREATE UNIQUE INDEX uk_tenant_stripe_connect
    ON tenant (stripe_connect_account_id)
    WHERE stripe_connect_account_id IS NOT NULL;

-- Connect-routed charges land in the tenant's bank directly via Stripe; the platform
-- reconciliation view only compares against platform Stripe balance_transactions, so
-- Connect ledger rows need their own payment_method to be excluded from that filter.
ALTER TABLE tenant_ledger_entry      DROP CONSTRAINT tenant_ledger_entry_payment_method_check;
ALTER TABLE day_pass_purchase        DROP CONSTRAINT day_pass_purchase_payment_method_check;
ALTER TABLE event_ticket_purchase    DROP CONSTRAINT event_ticket_purchase_payment_method_check;
ALTER TABLE season_pass_purchase     DROP CONSTRAINT season_pass_purchase_payment_method_check;

ALTER TABLE tenant_ledger_entry
    ADD CONSTRAINT tenant_ledger_entry_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect'));
ALTER TABLE day_pass_purchase
    ADD CONSTRAINT day_pass_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect'));
ALTER TABLE event_ticket_purchase
    ADD CONSTRAINT event_ticket_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect'));
ALTER TABLE season_pass_purchase
    ADD CONSTRAINT season_pass_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect'));
