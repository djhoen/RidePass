-- Sales tax for event admissions. Tracks (the tenant) are the merchant of record for the
-- admissions they sell, so they remit amusement/admission tax to their jurisdiction; RidePass
-- only calculates and collects it at checkout so the tenant has the money and the records.
--
-- This is deliberately separate from the existing concession sales tax (Script0159): amusement
-- tax is usually a local rate that has nothing to do with the state sales-tax rate on food, so a
-- tenant configures the two independently. We model it as a single per-tenant tax rate keyed by a
-- tax_kind so concessions can migrate onto this same table later; for now only 'admission' is used.
--
-- Because gate fees were folded into event_ticket_tier (Script0117), every admission, race entry,
-- spectator gate, and rider gate fee, is an event_ticket_purchase row, so the tax snapshot lives
-- there and there alone. The base that gets taxed is the post-discount ticket price plus, when the
-- tenant says so, the rider's service-charge share (a mandatory fee is part of the admission charge
-- in most jurisdictions, so service_charge_taxable defaults true). prices_include_tax lets a tenant
-- advertise tax-inclusive ticket prices (common for events) instead of adding tax on top.
--
-- Idempotent and additive: defaults reproduce today's behavior (no tax row, 0%, tax added on top),
-- so existing pricing is unchanged until a tenant sets a rate. Historical tickets keep tax_cents = 0,
-- which is correct (no tax was collected on them). No backfill of past purchases.

CREATE TABLE IF NOT EXISTS tenant_tax_rate (
    id                      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    tax_kind                text        NOT NULL,                   -- 'admission' (future: 'concession_sales')
    rate_bps                int         NOT NULL DEFAULT 0,         -- basis points: 900 = 9.00%
    prices_include_tax      boolean     NOT NULL DEFAULT false,     -- advertised price already includes tax
    service_charge_taxable  boolean     NOT NULL DEFAULT true,      -- is the rider service-charge share taxed
    jurisdiction_label      text        NULL,                       -- e.g. "City of ___ amusement tax"
    is_active               boolean     NOT NULL DEFAULT true,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_tenant_tax_rate_tenant ON tenant_tax_rate (tenant_id);
-- One rate per kind per tenant; the upsert in code relies on this.
CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_tax_rate_tenant_kind ON tenant_tax_rate (tenant_id, tax_kind);

DROP TRIGGER IF EXISTS trg_tenant_tax_rate_updated_at ON tenant_tax_rate;
CREATE TRIGGER trg_tenant_tax_rate_updated_at
    BEFORE UPDATE ON tenant_tax_rate
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Tax snapshot frozen on each ticket at checkout so later rate changes never rewrite history and
-- refunds can prorate the tax. tax_cents is the tax portion contained in amount_cents (we store
-- amount_cents tax-inclusive, so on-top tax grows amount_cents and inclusive tax is the backed-out
-- portion). tax_inclusive records which mode was in effect for this row.
ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS tax_cents     int     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS tax_rate_bps  int     NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS tax_inclusive boolean NOT NULL DEFAULT false;
