-- Direct charges on a tenant's own Stripe account (Connect direct charges + application fee).
--
-- Until now every charge ran on the platform Stripe account (RidePass is merchant of record),
-- the platform/tenant split was internal ledger math, and tenants were paid out monthly via
-- Stripe Transfer to a Connect account used only as a payout rail. That aggregation model is
-- not compliant for large tenants: Visa/Mastercard require any sub-merchant exceeding $1M/yr
-- to transact on its own merchant account. This adds a per-tenant 'direct' charge mode where
-- the charge runs on the tenant's own connected account with an application_fee_amount = our
-- service fee (that is how RidePass still gets paid), the tenant is merchant of record, and
-- there is no platform-side payout.

-- Per-tenant charge mode. 'platform' = today's behavior (charge on platform account, internal
-- split, monthly payout). 'direct' = charge on tenant's own connected account with app fee.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS stripe_charge_mode text NOT NULL DEFAULT 'platform'
    CHECK (stripe_charge_mode IN ('platform', 'direct'));

-- Snapshot of the connected account a purchase was actually charged on, written at charge time
-- when the tenant is in 'direct' mode. Refunds, the finalizer, and the reconciler read this
-- snapshot instead of re-deriving from the tenant's current mode, so a later mode flip / account
-- disconnect can never point a historical refund or status read at the wrong account.
ALTER TABLE event_ticket_purchase ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;
-- Extras and memberships can be bundled onto the same PaymentIntent as event tickets (one cart),
-- so they ride the same direct charge and need the same snapshot for correct refunds.
ALTER TABLE event_extra_purchase  ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;
ALTER TABLE membership_purchase   ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;

-- Allow 'stripe_direct' as a payment method everywhere a sale is recorded. Direct-charge sales
-- are recorded with this method so they are excluded from the platform-payout filter (the tenant
-- already holds the funds) and from Stripe-balance reconciliation (the charge is on their account,
-- not ours). Preserves the values from Script0036 / Script0110.
-- NOTE: the day-pass subsystem (pass_purchase / pass_product) was hard-dropped in Script0118, so it
-- is intentionally NOT touched here. Earlier drafts altered pass_purchase, which fails post-0118.
ALTER TABLE tenant_ledger_entry   DROP CONSTRAINT IF EXISTS tenant_ledger_entry_payment_method_check;
ALTER TABLE event_ticket_purchase DROP CONSTRAINT IF EXISTS event_ticket_purchase_payment_method_check;
ALTER TABLE season_pass_purchase  DROP CONSTRAINT IF EXISTS season_pass_purchase_payment_method_check;

ALTER TABLE tenant_ledger_entry   ADD CONSTRAINT tenant_ledger_entry_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits', 'stripe_direct'));
ALTER TABLE event_ticket_purchase ADD CONSTRAINT event_ticket_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits', 'stripe_direct'));
ALTER TABLE season_pass_purchase  ADD CONSTRAINT season_pass_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits', 'stripe_direct'));
