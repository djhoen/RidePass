-- QuickBooks DESKTOP alongside QuickBooks Online.
--
-- The Online sync (Script0174) posts a summarised journal entry per business day over Intuit's REST
-- API. Desktop has no such API: it is a Windows program holding a local company file, and the only
-- routes in are the Web Connector (a SOAP service the track installs and leaves running next to
-- QuickBooks) or an IIF file the bookkeeper imports by hand (File > Utilities > Import > IIF
-- Files). This adds the IIF route. A track on Desktop downloads one file covering every closed day
-- not yet exported, imports it, and gets the same per-day journal entries an Online track gets,
-- built by the same JournalEntryBuilder.
--
-- Two things Desktop does differently, and why each table below exists:
--
--   * Accounts and classes are referenced BY FULL NAME ("Income:Tickets & Fees",
--     "Corporate:Revenue:Tickets & Fees"), not by id, and an IIF import that names an account the
--     company does not have CREATES it, silently. A typo in a mapping would therefore not fail, it
--     would grow a junk account in the customer's books. So the track uploads its own Chart of
--     Accounts and Class List exported from Desktop (qbd_list_item), the mapping screen offers only
--     names from that list, and every export re-checks each line against it.
--   * We cannot see whether a downloaded file was ever imported, and importing the same file twice
--     doubles a day's revenue. So each export is a BATCH with its file content frozen
--     (qbd_export_batch): re-downloading returns the identical bytes rather than a rebuilt file, and
--     a new export only ever covers days no batch has claimed. A batch the bookkeeper never imported
--     can be voided, which releases its days for the next export.
--
-- The days a batch covers are recorded in the SAME qbo_sync_log the Online sync uses (new
-- export_batch_id column), as 'success'. That table's unique (tenant_id, business_date) index is
-- the double-posting guard for both editions at once: a day exported to Desktop can never also be
-- posted to Online if the track later moves editions, and vice versa.
--
-- Mappings get their own tables rather than reusing qbo_account_mapping / qbo_class_mapping because
-- the two editions identify an account differently (QBO Account.Id vs a Desktop full name). Sharing
-- one row would make a track that switches editions post Online ids as Desktop names. Keeping both
-- means switching back restores the old mapping intact.
--
-- Additive, rerunnable, backwards-compatible: five new tables and one nullable column; the deployed
-- Online code never reads any of them.

-- ── Desktop setup (the Desktop analogue of tenant_quickbooks_connection) ─────────────────────
CREATE TABLE IF NOT EXISTS tenant_quickbooks_desktop (
    id                      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- Nothing before this business date is ever exported, so switching it on cannot dump history
    -- into books that already have it keyed in. Same rule as the Online connection's.
    sync_start_date         date        NOT NULL,
    -- From the HDR row of the uploaded list file, so the screen can confirm which company it is.
    company_product         text        NULL,
    lists_uploaded_at_utc   timestamptz NULL,
    created_by_user_id      uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_quickbooks_desktop_tenant
    ON tenant_quickbooks_desktop (tenant_id);

DROP TRIGGER IF EXISTS trg_tenant_quickbooks_desktop_updated_at ON tenant_quickbooks_desktop;
CREATE TRIGGER trg_tenant_quickbooks_desktop_updated_at
    BEFORE UPDATE ON tenant_quickbooks_desktop
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── The track's own Chart of Accounts and Class List, as uploaded ────────────────────────────
-- Replaced wholesale per list on each upload (a file that carries only classes leaves the
-- accounts alone). account_type is the IIF ACCNTTYPE code (INC, EXP, OCASSET, OCLIAB, AR, ...).
CREATE TABLE IF NOT EXISTS qbd_list_item (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    list_kind       text        NOT NULL,
    full_name       text        NOT NULL,
    account_type    text        NULL,
    account_number  text        NULL,
    is_hidden       boolean     NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_qbd_list_item_tenant_kind_name
    ON qbd_list_item (tenant_id, list_kind, full_name);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_qbd_list_item_kind') THEN
        ALTER TABLE qbd_list_item ADD CONSTRAINT ck_qbd_list_item_kind
            CHECK (list_kind IN ('account', 'class'));
    END IF;
END $$;

-- ── Mappings (Desktop edition) ───────────────────────────────────────────────────────────────
-- Same key spaces as their Online twins: mapping_key is a QboAccountKeys slot, bucket_key a
-- ProfitCenterMap bucket ('pc:<uuid>' or a built-in QboDepartments key, deliberately not an FK,
-- see Script0277).
CREATE TABLE IF NOT EXISTS qbd_account_mapping (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    mapping_key         text        NOT NULL,
    account_full_name   text        NOT NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_qbd_account_mapping_tenant_key
    ON qbd_account_mapping (tenant_id, mapping_key);

DROP TRIGGER IF EXISTS trg_qbd_account_mapping_updated_at ON qbd_account_mapping;
CREATE TRIGGER trg_qbd_account_mapping_updated_at
    BEFORE UPDATE ON qbd_account_mapping
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS qbd_class_mapping (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    bucket_key          text        NOT NULL,
    class_full_name     text        NOT NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_qbd_class_mapping_tenant_bucket
    ON qbd_class_mapping (tenant_id, bucket_key);

DROP TRIGGER IF EXISTS trg_qbd_class_mapping_updated_at ON qbd_class_mapping;
CREATE TRIGGER trg_qbd_class_mapping_updated_at
    BEFORE UPDATE ON qbd_class_mapping
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Export batches ───────────────────────────────────────────────────────────────────────────
-- file_content is the IIF text exactly as first generated. Never rebuilt: a mapping changed after
-- the download must not make the "same" file differ from the one the bookkeeper may already have
-- imported.
CREATE TABLE IF NOT EXISTS qbd_export_batch (
    id                      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    from_date               date        NOT NULL,
    to_date                 date        NOT NULL,
    day_count               int         NOT NULL,
    total_debits_cents      bigint      NOT NULL DEFAULT 0,
    file_content            text        NOT NULL,
    created_by_user_id      uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    download_count          int         NOT NULL DEFAULT 0,
    last_downloaded_at_utc  timestamptz NULL,
    voided_at_utc           timestamptz NULL,
    voided_by_user_id       uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at              timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_qbd_export_batch_tenant_recent
    ON qbd_export_batch (tenant_id, created_at DESC);

-- ── Which batch exported a day ───────────────────────────────────────────────────────────────
-- NULL for every Online post and every row written before this script. Voiding a batch deletes
-- its days' log rows, so the FK never has to cascade; SET NULL is only a backstop.
ALTER TABLE qbo_sync_log
    ADD COLUMN IF NOT EXISTS export_batch_id uuid NULL REFERENCES qbd_export_batch(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS idx_qbo_sync_log_export_batch
    ON qbo_sync_log (export_batch_id) WHERE export_batch_id IS NOT NULL;
