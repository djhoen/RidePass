-- The bike shop is the only place money moves through RidePass for free.
--
-- Every other revenue surface snapshots what the platform is owed on the sale: event tickets,
-- extras, memberships, packages, season passes and shop rentals all carry service_charge_cents.
-- shop_sale does not, and both ledger writers (BikeShopRegisterController.WriteCashLedger for
-- cash, StripePurchaseFinalizer for card) pass a literal 0 into IFeeCalculator, which does not
-- compute a charge, it only caps one it is handed. So RidepassCutCents comes out as 0 on every
-- bike shop sale, while the cash entry's memo cheerfully reads "tenant owes service charge".
--
-- This closes that. Note what it covers: a shop_sale is created by THREE paths, all of which
-- inherit the charge automatically.
--   * the counter register            (BikeShopRegisterController)
--   * the online store                (ShopStoreController)
--   * billing out a repair            (BikeShopWorkOrderController, via shop_sale.work_order_id)
-- Repair labour is therefore included. If a track should not pay a platform charge on labour,
-- that is a one-predicate carve-out on work_order_id, but it is a deliberate decision rather
-- than something to leave ambiguous.
--
-- shop_buyer_paid_service_charge_bps decides only WHO FUNDS the charge, never whether it is owed:
--   10000 = added to what the customer pays, as a visible line
--       0 = the track absorbs it out of their own margin (default)
-- Defaulting to 0 is deliberate. It starts the charge accruing immediately without silently
-- adding a fee line to a walk-in buying an inner tube, which is a change no track asked for and
-- their customer would notice first. A track that wants to pass it on opts in.
--
-- Additive and rerunnable. No backfill: past sales genuinely carried no charge, and inventing one
-- retroactively would misstate what those tenants were owed.

ALTER TABLE shop_sale
    ADD COLUMN IF NOT EXISTS service_charge_cents int NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_shop_sale_service_charge_cents') THEN
        ALTER TABLE shop_sale ADD CONSTRAINT chk_shop_sale_service_charge_cents
            CHECK (service_charge_cents >= 0);
    END IF;
END $$;

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS shop_buyer_paid_service_charge_bps int NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_shop_buyer_paid_bps') THEN
        ALTER TABLE tenant ADD CONSTRAINT chk_tenant_shop_buyer_paid_bps
            CHECK (shop_buyer_paid_service_charge_bps BETWEEN 0 AND 10000);
    END IF;
END $$;
