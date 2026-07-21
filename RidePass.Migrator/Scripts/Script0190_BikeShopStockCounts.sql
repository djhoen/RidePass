-- Bike shop stock takes: physical counts with variance, on the shop's real inventory.
--
-- A count snapshots every active POOL variant's expected on-hand at start, staff walk the shop
-- entering what's actually on the shelf, and completing the count trues stock up: each counted
-- line writes a 'stocktake' movement for the difference against the CURRENT on-hand (not the
-- snapshot — stock keeps moving while you count; the snapshot is kept for the variance report,
-- the truth-up is against reality at completion). Serialized units are deliberately out of
-- scope: they're counted by walking the racks and fixing item statuses, not by quantity.
--
-- Mirrors concession_inventory_count, but backed by the shop's append-only movement ledger so
-- every count adjustment is auditable.
--
-- Additive + idempotent.

CREATE TABLE IF NOT EXISTS shop_stock_count (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    status              text        NOT NULL DEFAULT 'open' CHECK (status IN ('open','completed','cancelled')),
    notes               text        NULL,
    started_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    started_at          timestamptz NOT NULL DEFAULT now(),
    completed_at        timestamptz NULL
);
CREATE INDEX IF NOT EXISTS idx_shop_stock_count_tenant ON shop_stock_count (tenant_id, status, started_at);

CREATE TABLE IF NOT EXISTS shop_stock_count_line (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    count_id     uuid NOT NULL REFERENCES shop_stock_count(id) ON DELETE CASCADE,
    -- RESTRICT: a variant on a historical count can be deactivated but not deleted.
    variant_id   uuid NOT NULL REFERENCES shop_variant(id) ON DELETE RESTRICT,
    -- On-hand at count start (the variance report's baseline).
    expected_qty int  NOT NULL,
    -- What staff actually found. NULL = not counted yet; uncounted lines are skipped at completion.
    counted_qty  int  NULL CHECK (counted_qty IS NULL OR counted_qty >= 0),
    UNIQUE (count_id, variant_id)
);
CREATE INDEX IF NOT EXISTS idx_shop_stock_count_line_count ON shop_stock_count_line (count_id);
