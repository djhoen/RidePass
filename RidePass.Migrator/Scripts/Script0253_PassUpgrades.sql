-- Season pass upgrades: a tenant offers holders of pass A a move to pass B for a set price.
-- Design: docs/pass-upgrades.md.
--
-- THE STATUS IS THE POINT. 'upgraded' is added to season_pass_purchase because every admission
-- and benefit path in the codebase already filters status = 'paid'. The moment the old pass
-- flips, it stops admitting, stops granting benefits, and stops showing as an active pass, with
-- no new enforcement written anywhere. That is the same property the employee-pass 'pending'
-- state relies on, and it is why an upgrade retires the old row rather than mutating it.
--
-- WHY NOT MUTATE product_id ON THE EXISTING ROW. It is the obvious implementation and it
-- preserves the rider's QR, photo, waiver, and ID verification for free. Rejected: the ledger
-- already recorded a sale of product A at price A, and the purchase row is what reporting joins
-- to. Flipping the product silently rewrites history, so revenue-by-product, the sales list, and
-- any refund of the original all start describing a purchase that never happened. The upgrade is
-- a second sale, so it gets a second row.
--
-- Additive and rerunnable.

CREATE TABLE IF NOT EXISTS season_pass_upgrade_path (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    from_product_id uuid NOT NULL REFERENCES season_pass_product(id) ON DELETE CASCADE,
    to_product_id   uuid NOT NULL REFERENCES season_pass_product(id) ON DELETE CASCADE,
    -- What the holder pays to move up. FLAT, not a computed difference: the tenant decides what
    -- the upgrade is worth, which is rarely just (B - A).
    --
    -- >= 0 rather than > 0: a free upgrade is a legitimate goodwill gesture, and unlike a free
    -- PRODUCT (which would be publicly listed and unbuyable) an upgrade is only ever reachable
    -- by an existing holder.
    price_cents     int  NOT NULL CHECK (price_cents >= 0),
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_upgrade_path_distinct CHECK (from_product_id <> to_product_id)
);

-- One offer per direction. Two live "3-pack to unlimited" rows at different prices is a
-- coin-flip at checkout, not a feature.
CREATE UNIQUE INDEX IF NOT EXISTS uk_upgrade_path_pair
    ON season_pass_upgrade_path (from_product_id, to_product_id);

-- The rider-side read: "what can this pass become?"
CREATE INDEX IF NOT EXISTS ix_upgrade_path_from
    ON season_pass_upgrade_path (tenant_id, from_product_id) WHERE is_active;

-- Deliberately NOT enforced: that B is "better" than A. Nothing in the data says one pass beats
-- another, and a track may legitimately offer a sideways move (weekday to weekend) for a fee.
-- Encoding a hierarchy would invent an ordering the tenant never asked for.

ALTER TABLE season_pass_purchase
    ADD COLUMN IF NOT EXISTS upgraded_from_purchase_id uuid NULL REFERENCES season_pass_purchase(id);

-- Answers "did this holder take the offer?" for the conversion report, and is what the upgrade
-- drip will use as its exit condition.
CREATE INDEX IF NOT EXISTS ix_season_pass_purchase_upgraded_from
    ON season_pass_purchase (upgraded_from_purchase_id)
    WHERE upgraded_from_purchase_id IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'season_pass_purchase_status_check'
          AND conrelid = 'season_pass_purchase'::regclass
          AND pg_get_constraintdef(oid) LIKE '%upgraded%'
    ) THEN
        ALTER TABLE season_pass_purchase DROP CONSTRAINT IF EXISTS season_pass_purchase_status_check;
        ALTER TABLE season_pass_purchase
            ADD CONSTRAINT season_pass_purchase_status_check
            CHECK (status = ANY (ARRAY['pending','paid','failed','cancelled','refunded','upgraded']));
    END IF;
END $$;
