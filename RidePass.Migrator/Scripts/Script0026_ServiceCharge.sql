-- Flat per-tenant service charge replaces the tiered fee schedule for new sales.
-- Defaults: 3% (300 bps), no monthly cap. The tiered fee_schedule / fee_tier tables
-- are kept for historical ledger entries (immutable record) but no longer drive pricing.
-- Each item (day pass product, ticket tier) carries the share of the service charge
-- that the rider pays as a separate line item; the rest comes out of the tenant's net.

ALTER TABLE tenant
    ADD COLUMN service_charge_bps int NOT NULL DEFAULT 300,
    ADD COLUMN monthly_service_charge_cap_cents int NULL;

ALTER TABLE day_pass_product
    ADD COLUMN rider_paid_service_charge_bps int NOT NULL DEFAULT 10000;

ALTER TABLE event_ticket_tier
    ADD COLUMN rider_paid_service_charge_bps int NOT NULL DEFAULT 10000;

-- Snapshot the full service charge owed on each purchase at sale time so the webhook
-- can write the ledger without re-deriving from current tenant/product settings (which
-- might drift between purchase creation and Stripe capture).
ALTER TABLE day_pass_purchase
    ADD COLUMN service_charge_cents int NOT NULL DEFAULT 0;

ALTER TABLE event_ticket_purchase
    ADD COLUMN service_charge_cents int NOT NULL DEFAULT 0;

