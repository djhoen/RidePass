-- Cash sales at the counter: tenant collects the rider's payment directly. The platform
-- still records the service charge as ridepass_cut (it's owed by the tenant). Because the
-- tenant already pocketed the gross, net_to_tenant on these rows is negative — it reduces
-- the tenant's available balance against future card-sale net-positives. Reconciliation
-- compares Stripe balance_transactions only against payment_method='stripe' rows.
--
-- 'voucher' tags the $0 voucher fast-path entries that bypass Stripe entirely.

ALTER TABLE tenant_ledger_entry
    ADD COLUMN payment_method text NOT NULL DEFAULT 'stripe'
    CHECK (payment_method IN ('stripe', 'cash', 'voucher'));

ALTER TABLE day_pass_purchase
    ADD COLUMN payment_method text NOT NULL DEFAULT 'stripe'
    CHECK (payment_method IN ('stripe', 'cash', 'voucher'));

ALTER TABLE event_ticket_purchase
    ADD COLUMN payment_method text NOT NULL DEFAULT 'stripe'
    CHECK (payment_method IN ('stripe', 'cash', 'voucher'));
