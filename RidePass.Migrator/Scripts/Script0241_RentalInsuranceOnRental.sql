-- Record the damage waiver ("insurance") on the RENTAL, not just the tenant setting that offers it.
--
-- Script0233 added the tenant-level offer (rental_insurance_enabled / _label / _bps) and the
-- booking paths charge it, but nothing on shop_rental says whether a given rental bought it. The
-- fee was folded into amount_cents and the only trace was deposit_cents landing at 0, which is
-- indistinguishable from gear that simply carries no deposit. That costs us in three places:
--
--   * At RETURN the counter sees "Deposit authorized: $0.00" and cannot tell "this rider is
--     covered" from "we never took a deposit". Those call for opposite conversations about damage.
--   * On a receipt or a refund the fee cannot be itemised back out of amount_cents.
--   * In reporting, damage-waiver revenue is invisible.
--
-- insurance_cents is the fee actually charged, frozen at booking. > 0 IS the record that the
-- waiver was bought; the deposit being waived is a consequence of it, not the evidence for it.
--
-- insurance_label_snapshot freezes what the renter was told they were buying, matching how this
-- table already freezes name_snapshot, daily_rate_cents_frozen and deposit_cents_frozen on lines:
-- a tenant renaming "Damage Protection" to "Bike Insurance" must not rewrite last month's receipt.
--
-- Additive and idempotent. No backfill: every existing rental predates the counter offering this,
-- and 0 is the truthful value for all of them.

ALTER TABLE shop_rental
    ADD COLUMN IF NOT EXISTS insurance_cents          int  NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS insurance_label_snapshot text NULL;

-- A negative fee is not a thing; a rental that carries a waiver must have been charged for it.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_shop_rental_insurance_cents') THEN
        ALTER TABLE shop_rental ADD CONSTRAINT chk_shop_rental_insurance_cents
            CHECK (insurance_cents >= 0);
    END IF;
END $$;
