-- PROFIT CENTERS INSIDE QUICKBOOKS: map each reporting bucket onto a QBO Class.
--
-- Until now the only separation that reached a customer's books was the chart of accounts: the
-- journal entry posts one line per revenue slot and each slot carries an AccountRef, nothing else.
-- A track that maps Bike Shop, Training and Concessions onto three income accounts gets three
-- lines; a track that maps them all onto one "Sales" account gets one, and no report in QuickBooks
-- can split it back out. Profit centers (Script0275) existed only on the RidePass side.
--
-- This table closes that gap. Each bucket the reports already group by can name a QBO Class, and
-- the sync stamps that class onto every revenue line belonging to the bucket. The tenant then gets
-- a real Profit & Loss by Class in QuickBooks, matching the Revenue by Department report here,
-- WITHOUT having to split their chart of accounts to do it.
--
-- bucket_key is the same string ProfitCenterMap resolves a revenue slot to, so it is either:
--   * 'pc:<uuid>'  a tenant-configured profit center (profit_center.id), or
--   * a built-in QboDepartments key ('bike_shop', 'food_beverage', 'tickets_passes',
--     'training', 'other') for a tenant who has never configured centers.
-- Deliberately NOT an FK: half the legal values name no row at all, and the built-in fallback has
-- to keep working for a tenant with an empty profit_center table. A row whose center is later
-- deleted simply stops resolving (its slots fall back to their built-in department, which may
-- carry its own class row), the same harmless-orphan rule profit_center_revenue_key documents.
--
-- No backfill. Zero rows means "post no classes at all", which is exactly today's behavior, and
-- the sync skips the whole class lookup in that case so an unconfigured tenant's journal entry is
-- byte-identical to the one it posted before this migration.
CREATE TABLE IF NOT EXISTS qbo_class_mapping (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    bucket_key     text        NOT NULL,
    qbo_class_id   text        NOT NULL,
    -- Display snapshot so the settings screen renders the current choice without a QBO round-trip,
    -- exactly like qbo_account_mapping.qbo_account_name. Never trusted by the sync itself.
    qbo_class_name text        NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);

-- One class per bucket per tenant; the upsert in code relies on this.
CREATE UNIQUE INDEX IF NOT EXISTS ux_qbo_class_mapping_tenant_bucket
    ON qbo_class_mapping (tenant_id, bucket_key);

DROP TRIGGER IF EXISTS trg_qbo_class_mapping_updated_at ON qbo_class_mapping;
CREATE TRIGGER trg_qbo_class_mapping_updated_at
    BEFORE UPDATE ON qbo_class_mapping
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
