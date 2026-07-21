-- Rental sales tax.
--
-- Rentals have been booking with tax_cents hardcoded to 0 ("all-in priced, matching the old rental
-- system") while retail sales tax properly through shop_tax_category. Renting tangible personal
-- property is taxable in most US states, so a shop renting bikes through RidePass has been
-- under-collecting, and that is the tenant's liability.
--
-- Rate is TENANT-level, not per product: one rental tax rate for the track. (Retail keeps its
-- per-product tax categories; rentals are a small enough surface that one rate is the right
-- trade.)
--
-- NULL vs 0 is load-bearing:
--   NULL = never configured. The UI warns, because silence here means under-collection.
--   0    = deliberately tax-free (a jurisdiction that doesn't tax rentals). No warning.
-- That distinction is the whole reason this column is nullable rather than DEFAULT 0.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS rental_tax_bps int NULL
        CHECK (rental_tax_bps IS NULL OR (rental_tax_bps >= 0 AND rental_tax_bps <= 10000));

COMMENT ON COLUMN tenant.rental_tax_bps IS
    'Sales tax on rentals, basis points (825 = 8.25%). NULL = never configured (UI warns); 0 = deliberately untaxed. The refundable deposit is never taxed.';

-- Whether the renter-paid service fee is part of the taxable base. Mirrors
-- tenant_tax.service_charge_taxable for admissions, which defaults true on the same reasoning:
-- a mandatory fee attached to a taxable sale is generally taxable, but it varies by jurisdiction.
ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS rental_tax_service_charge_taxable boolean NOT NULL DEFAULT true;

COMMENT ON COLUMN tenant.rental_tax_service_charge_taxable IS
    'Is the renter-paid service fee included in the rental taxable base. Defaults true (taxable in most jurisdictions), matching the admissions rule.';
