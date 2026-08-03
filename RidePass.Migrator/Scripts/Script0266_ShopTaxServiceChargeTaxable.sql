-- Whether the buyer-paid platform service fee on a bike shop sale is part of the taxable base.
--
-- Rentals have made this a tenant choice since Script0214 (tenant.rental_tax_service_charge_taxable,
-- default true), because whether a service fee is taxable is a jurisdiction question, not a
-- product decision. Retail shop sales were built to mirror the rental fee logic but never got the
-- equivalent switch, so they silently never taxed the fee: two sibling paths treating the same fee
-- oppositely, and an under-collection anywhere the fee IS taxable.
--
-- DEFAULT true, to match rentals. Safe as a default rather than a backfill because the fee a buyer
-- pays is governed by tenant.shop_buyer_paid_service_charge_bps, which defaults to 0 and is 0 for
-- every existing tenant: there is no fee on any current sale for this to tax, so no total changes
-- until a track deliberately turns buyer-paid shop fees on.
--
-- The fee is taxed at the tenant's DEFAULT shop tax category rate. That is the rate meaning
-- "anything not otherwise categorised", which a platform fee is; a per-product rate makes no sense
-- for a charge that isn't a product. A tenant with no default category taxes the fee at nothing,
-- exactly as their uncategorised products already sell untaxed.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS shop_tax_service_charge_taxable boolean NOT NULL DEFAULT true;
