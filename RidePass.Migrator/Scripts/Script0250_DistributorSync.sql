-- Automatic distributor catalog sync, and the provenance that makes it safe.
--
-- TWO THINGS, and the first exists because of the second.
--
-- 1. shop_variant.manufacturer_name_source
--    Script0249 made manufacturer_name the ONE field that feeds the cross-tenant platform_part
--    library. That was safe while a shop typed it. It stops being safe the moment a distributor
--    sync writes it: QBP's Content Licensing Service is licensed PER DEALER, so pooling one
--    dealer's CLS content into a library other dealers read is exactly the redistribution that
--    licensing model exists to prevent. It would have happened silently, with no code change,
--    purely from switching the sync on.
--
--    So every manufacturer_name now records where it came from, and only sources RidePass is
--    actually entitled to pool are contributed. The allow-list lives in ONE place,
--    Services.BikeShop.LibraryContribution, next to the tests that pin it.
--
--      'shop'    a human at the shop typed it. RidePass's own data. Poolable.
--      'import'  came in on a CSV the shop uploaded. Poolable: the shop chose to supply it, and
--                RidePass never agreed to any terms over a file a customer handed us.
--      'library' came back out of platform_part. Already shared; pooling is a no-op.
--      'qbp'     from QBP's licensed CLS feed. NOT poolable.
--      'sample'  from the fake distributor that exists to exercise this pipeline on dev and
--                staging. NOT poolable, deliberately, so a test run proves the guard works.
--
--    Existing rows are backfilled to 'shop' because that is the only way one could have been set
--    before this script: no sync existed.
--
-- 2. tenant_distributor_credential
--    Per-tenant, because CLS keys are issued per dealer and RidePass cannot hold one key and serve
--    every shop from it. Every integrator in this space (Lightspeed, Masterlinq, Finale) works the
--    same way: the dealer gets their own credentials from the distributor and hands them over.
--    Secrets are encrypted at rest with EncryptionHelper, the same treatment as
--    tenant.twilio_auth_token_encrypted.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'shop_variant'
    ) THEN
        ALTER TABLE shop_variant
            ADD COLUMN IF NOT EXISTS manufacturer_name_source text NULL;

        -- Backfill, not a DEFAULT: a default would silently label future sync-written rows 'shop'
        -- if a code path ever forgot to set it, and mislabelling licensed content as poolable is
        -- the one failure this column exists to prevent. NULL means "unknown", and unknown is
        -- treated as not poolable.
        UPDATE shop_variant
        SET manufacturer_name_source = 'shop'
        WHERE manufacturer_name IS NOT NULL AND manufacturer_name_source IS NULL;

        -- Drop-then-add so a later script can widen the allow-list. Plain statements rather than
        -- EXECUTE with dollar-tag quoting: PL/pgSQL runs DDL directly, so the wrapper bought
        -- nothing, and DbUp's preprocessor reads a named dollar-tag as a variable placeholder and
        -- aborts the whole migration run with "Variable c has no value defined". The unnamed tag
        -- opening this DO block is fine; a NAMED one is not.
        ALTER TABLE shop_variant DROP CONSTRAINT IF EXISTS ck_shop_variant_mfr_name_source;
        ALTER TABLE shop_variant ADD CONSTRAINT ck_shop_variant_mfr_name_source
            CHECK (manufacturer_name_source IS NULL
                   OR manufacturer_name_source IN ('shop', 'import', 'library', 'qbp', 'sample'));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS tenant_distributor_credential (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant (id) ON DELETE CASCADE,
    -- 'qbp' today. A slug rather than an enum so adding J&B, BTI or Hawley is a row, not a schema
    -- change; the code resolves a slug to an IDistributorCatalogSource.
    distributor         text        NOT NULL,
    -- The dealer's account number with the distributor. Not a secret (it is printed on their
    -- invoices) and useful to show in the UI so an admin can confirm which account is connected.
    account_number      text        NULL,
    username            text        NULL,
    -- EncryptionHelper blobs, never plaintext. Two secrets because QBP issues both an EFTP
    -- password and a separate CLS API key, and integrators are asked for both.
    password_encrypted  text        NULL,
    api_key_encrypted   text        NULL,
    is_enabled          boolean     NOT NULL DEFAULT true,
    -- Sync bookkeeping. last_status/last_error are what the settings screen shows so a shop can
    -- see a failing sync without anyone reading logs.
    last_sync_at        timestamptz NULL,
    last_status         text        NULL,       -- 'ok' | 'error' | 'running'
    last_error          text        NULL,
    last_products_seen  int         NOT NULL DEFAULT 0,
    last_variants_updated int       NOT NULL DEFAULT 0,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);

-- One credential per distributor per tenant. Re-connecting updates in place rather than
-- accumulating stale keys that a sync might pick the wrong one of.
CREATE UNIQUE INDEX IF NOT EXISTS uk_tenant_distributor_credential
    ON tenant_distributor_credential (tenant_id, distributor);

-- The sweep's work queue: enabled credentials, oldest sync first.
CREATE INDEX IF NOT EXISTS ix_tenant_distributor_credential_due
    ON tenant_distributor_credential (last_sync_at) WHERE is_enabled;
