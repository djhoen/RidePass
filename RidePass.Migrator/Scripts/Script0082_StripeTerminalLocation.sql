-- Per-tenant Stripe Terminal Location id. Used by the RidePassCashier mobile
-- app's tap-to-pay flow: the Stripe Terminal SDK requires both a connection
-- token and a location id, and PaymentIntents created with
-- payment_method_types=['card_present'] must reference a Location so the
-- Stripe dashboard can group card-present sales by physical site.
--
-- Lazily provisioned the first time a cashier opens the app at a tenant —
-- CounterController.EnsureTerminalLocation creates the Stripe Location using
-- the tenant's display_name + address fields and writes the returned id back
-- here, so subsequent connections reuse it instead of re-creating.

ALTER TABLE tenant
    ADD COLUMN stripe_terminal_location_id text NULL;
