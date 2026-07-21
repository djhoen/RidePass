-- QuickBooks Online sync: per-tenant connection, chart-of-accounts mapping, and a post log.
--
-- Tracks keep their own books. Today a tenant re-keys their RidePass revenue into QuickBooks by
-- hand off the Reports screens, which is slow and drifts. This wires a real accounting sync: once
-- a night we post one summarised Journal Entry per tenant per business date into their QBO company.
--
-- Why a connection TABLE and not more columns on `tenant` (the Twilio/Stripe precedent): an OAuth
-- link has a lifecycle those integrations don't. Intuit hands back a short-lived access token (~1h)
-- plus a rotating refresh token that itself expires (~100 days) and is REPLACED on nearly every
-- refresh. That's four correlated columns that change on a timer, plus connect/disconnect/re-auth
-- transitions and a sync cursor. That belongs in its own row with its own updated_at, not smeared
-- across the widest table in the schema.
--
-- Tokens are encrypted at rest with EncryptionHelper (AES-256-CBC, Encryption:KeyBase64 /
-- Encryption:IvBase64), exactly as tenant.twilio_auth_token_encrypted already is. The raw tokens
-- are never stored. Rotating the encryption key makes every stored token undecryptable and forces
-- every tenant to reconnect, same caveat that already applies to Twilio.
--
-- Idempotent and additive: three new tables, nothing existing is touched. A tenant with no
-- connection row simply never syncs, so this is inert until someone connects.

-- ── The OAuth link to one QBO company ────────────────────────────────────────────────────────
-- One QBO company per tenant (ux_tenant_quickbooks_connection_tenant). A tenant that needs to move
-- to a different company disconnects and reconnects, which is deliberate: silently re-pointing a
-- sync at a new realm would strand the already-posted journal entries in the old company.
CREATE TABLE IF NOT EXISTS tenant_quickbooks_connection (
    id                              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    realm_id                        text        NOT NULL,             -- QBO company id
    refresh_token_encrypted         text        NOT NULL,
    refresh_token_expires_at_utc    timestamptz NULL,                 -- Intuit: ~100 days, rotates on refresh
    access_token_encrypted          text        NULL,                 -- ~1h; cached to avoid a refresh per call
    access_token_expires_at_utc     timestamptz NULL,
    status                          text        NOT NULL DEFAULT 'active',   -- active | expired | revoked | error
    sync_enabled                    boolean     NOT NULL DEFAULT true,
    -- Nothing before this date is ever posted. Set at connect time (defaults to "today") so linking
    -- an account can't dump years of history into a live set of books that already has it keyed in.
    sync_start_date                 date        NOT NULL,
    -- Cursor: the most recent business date successfully posted. NULL = nothing posted yet.
    last_synced_date                date        NULL,
    last_sync_at_utc                timestamptz NULL,
    last_sync_error                 text        NULL,
    connected_by_user_id            uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    connected_at_utc                timestamptz NOT NULL DEFAULT now(),
    created_at                      timestamptz NOT NULL DEFAULT now(),
    updated_at                      timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_quickbooks_connection_tenant
    ON tenant_quickbooks_connection (tenant_id);
-- The nightly sweep walks connections, not tenants: only the linked-and-enabled ones cost anything.
CREATE INDEX IF NOT EXISTS idx_tenant_quickbooks_connection_due
    ON tenant_quickbooks_connection (status, sync_enabled)
    WHERE status = 'active' AND sync_enabled = true;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenant_qbo_connection_status') THEN
        ALTER TABLE tenant_quickbooks_connection ADD CONSTRAINT ck_tenant_qbo_connection_status
            CHECK (status IN ('active', 'expired', 'revoked', 'error'));
    END IF;
END $$;

DROP TRIGGER IF EXISTS trg_tenant_quickbooks_connection_updated_at ON tenant_quickbooks_connection;
CREATE TRIGGER trg_tenant_quickbooks_connection_updated_at
    BEFORE UPDATE ON tenant_quickbooks_connection
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Chart-of-accounts mapping ────────────────────────────────────────────────────────────────
-- Every tenant's chart of accounts is their own, so we can't hardcode account ids. Instead the sync
-- emits a fixed set of semantic keys (QboAccountKeys in code, 'revenue_concession',
-- 'liability_sales_tax', 'expense_stripe_fees', ...) and the tenant maps each one onto an account
-- in their QBO company from the settings screen. An unmapped key that a given day's activity needs
-- makes that day's post fail loudly rather than silently booking to the wrong account.
--
-- qbo_account_name is a display snapshot for the settings UI so the list renders without a QBO
-- round-trip. qbo_account_id is the only value the sync itself trusts.
CREATE TABLE IF NOT EXISTS qbo_account_mapping (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    mapping_key         text        NOT NULL,
    qbo_account_id      text        NOT NULL,
    qbo_account_name    text        NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);
-- One account per key per tenant; the upsert in code relies on this.
CREATE UNIQUE INDEX IF NOT EXISTS ux_qbo_account_mapping_tenant_key
    ON qbo_account_mapping (tenant_id, mapping_key);

DROP TRIGGER IF EXISTS trg_qbo_account_mapping_updated_at ON qbo_account_mapping;
CREATE TRIGGER trg_qbo_account_mapping_updated_at
    BEFORE UPDATE ON qbo_account_mapping
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ── Post log ─────────────────────────────────────────────────────────────────────────────────
-- One row per (tenant, business date). This is the idempotency anchor for the whole sync: the
-- unique index below is what makes a re-run, a retry, a manual re-sync, two dispatchers racing, -- unable to post the same day's journal entry twice. Double-posting revenue into a customer's live
-- books is the worst thing this feature could do, so the guarantee is in the database, not in code.
--
-- Business date is the tenant-local calendar date (bucketed via tenant.timezone), not a UTC date:
-- a track's Saturday night gate revenue belongs on Saturday's books, and a UTC date would push the
-- evening of a US event onto the next day.
CREATE TABLE IF NOT EXISTS qbo_sync_log (
    id                      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    business_date           date        NOT NULL,
    status                  text        NOT NULL,          -- success | failed | no_activity
    qbo_journal_entry_id    text        NULL,              -- set on success; the QBO JournalEntry.Id
    qbo_doc_number          text        NULL,
    entry_count             int         NOT NULL DEFAULT 0,   -- accounting rows summarised into the post
    total_debits_cents      bigint      NOT NULL DEFAULT 0,   -- == total credits; a balance tripwire
    attempt_count           int         NOT NULL DEFAULT 0,
    last_error              text        NULL,
    synced_at_utc           timestamptz NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_qbo_sync_log_tenant_date
    ON qbo_sync_log (tenant_id, business_date);
CREATE INDEX IF NOT EXISTS idx_qbo_sync_log_tenant_recent
    ON qbo_sync_log (tenant_id, business_date DESC);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_qbo_sync_log_status') THEN
        ALTER TABLE qbo_sync_log ADD CONSTRAINT ck_qbo_sync_log_status
            CHECK (status IN ('success', 'failed', 'no_activity'));
    END IF;
END $$;

DROP TRIGGER IF EXISTS trg_qbo_sync_log_updated_at ON qbo_sync_log;
CREATE TRIGGER trg_qbo_sync_log_updated_at
    BEFORE UPDATE ON qbo_sync_log
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
