-- Rental service fee: restore RidePass's cut on rentals.
--
-- When rentals moved from rental_purchase onto the bike-shop model (Script0200), the old
-- rider_paid_service_charge_bps / RentalCharge.Compute path did not come with them, so every shop
-- rental has been booking with serviceFeeCents: 0. Tickets, extras, passes and memberships all
-- still charge the tenant service fee; rentals stopped. This puts it back.
--
-- Design, deliberately narrower than the old per-product model:
--   * RATE comes from the existing tenant.service_charge_bps — the same percentage events use.
--     There is no per-product rate; a track sets one number for everything they sell.
--   * SPLIT is tenant-level and rental-specific: how much of that fee the customer pays vs the
--     track absorbs. 10000 = customer pays all (the default everywhere else in the system),
--     0 = the track eats it entirely.
--   * BASE is the rental subtotal after discounts, EXCLUDING the refundable deposit. A deposit is
--     the customer's own money held against damage; taking a percentage of it would be charging
--     them a fee to lend us their deposit. Same invariant the old RentalCharge pinned.
--   * TAX is not introduced here. Rentals are all-in priced with no tax line today
--     (shop_rental.tax_cents is hardcoded 0), so there is nothing for the fee to be taxed into.
--     If rental tax is added later, the fee should join the taxable base by default, matching
--     tenant_tax.service_charge_taxable for admissions.
--
-- Rollout note: existing tenants already have a non-zero service_charge_bps (events use it), so
-- deploying this DOES start charging a fee on rentals. Defaulting the split to 10000 keeps
-- RidePass's economics identical to every other product type; a track that would rather absorb it
-- flips the number on Rentals -> Settings.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS rental_rider_paid_service_charge_bps int NOT NULL DEFAULT 10000
        CHECK (rental_rider_paid_service_charge_bps BETWEEN 0 AND 10000);

COMMENT ON COLUMN tenant.rental_rider_paid_service_charge_bps IS
    'Share of the tenant service charge the RENTER pays on a rental (bps). 10000 = renter pays all, 0 = track absorbs it. Rate itself comes from tenant.service_charge_bps.';

-- The full tenant service charge for the rental, snapshotted at booking. This is the fee-calculator
-- input (what RidePass is owed), NOT the portion added to the customer's bill — that portion is
-- already inside total_cents. Mirrors event_ticket_purchase.service_charge_cents.
ALTER TABLE shop_rental
    ADD COLUMN IF NOT EXISTS service_charge_cents int NOT NULL DEFAULT 0
        CHECK (service_charge_cents >= 0);

COMMENT ON COLUMN shop_rental.service_charge_cents IS
    'Full tenant service charge on the rental subtotal (deposit excluded), frozen at booking. The renter-paid share of it is already included in total_cents.';
