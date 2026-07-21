-- Store credit: a per-tenant customer credit balance (the industry-standard home for deposit
-- overages, refunds-to-credit, and future loyalty awards). Modeled like every other balance in
-- the app: an append-only entry ledger plus a cached balance with a floor guard.
--
-- Identity: an account belongs to a rider account (user_id) and/or a bare email/phone, so
-- walk-in work-order customers can hold credit and get matched later if they sign up. Lookup
-- at the counter is by email or phone; the cashier sees the display name to verify the person.
--
-- Accounting: credit never moves money. When real money enters (a deposit, a sale later
-- refunded to credit) the tenant_ledger_entry books it and the platform cut is taken then,
-- once. Redeeming credit reduces what the sale's money entry records; issuing credit from a
-- refund writes NO ledger mirror (the tenant keeps the cash and owes goods instead).
--
-- Additive + idempotent.

CREATE TABLE IF NOT EXISTS tenant_credit_account (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id        uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    email          text        NULL,   -- stored lowercased
    phone          text        NULL,   -- digits only
    display_name   text        NULL,
    balance_cents  int         NOT NULL DEFAULT 0 CHECK (balance_cents >= 0),
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now(),
    -- An account with no identity could never be looked up or matched again.
    CONSTRAINT chk_credit_account_identity CHECK (user_id IS NOT NULL OR email IS NOT NULL OR phone IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS uk_credit_account_user  ON tenant_credit_account (tenant_id, user_id) WHERE user_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_credit_account_email ON tenant_credit_account (tenant_id, lower(email)) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_credit_account_phone ON tenant_credit_account (tenant_id, phone) WHERE phone IS NOT NULL;

DROP TRIGGER IF EXISTS trg_tenant_credit_account_updated_at ON tenant_credit_account;
CREATE TRIGGER trg_tenant_credit_account_updated_at BEFORE UPDATE ON tenant_credit_account
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS tenant_credit_entry (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    account_id         uuid        NOT NULL REFERENCES tenant_credit_account(id) ON DELETE CASCADE,
    delta_cents        int         NOT NULL CHECK (delta_cents <> 0),
    -- deposit_excess    = work-order deposit exceeded the bill; overage kept as credit
    -- refund_to_credit  = a sale refund issued as credit instead of money back
    -- loyalty_award     = granted by a loyalty rule (phase 3)
    -- manual_adjust     = staff grant/correction (positive or negative)
    -- redeem            = spent as tender on a sale (negative)
    -- redeem_reversal   = redeem handed back because the sale failed or was refunded
    kind               text        NOT NULL CHECK (kind IN
                           ('deposit_excess','refund_to_credit','loyalty_award','manual_adjust','redeem','redeem_reversal')),
    reference_kind     text        NULL,   -- 'shop_sale' | 'shop_work_order' | ...
    reference_id       uuid        NULL,
    note               text        NULL,
    created_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_credit_entry_account ON tenant_credit_entry (account_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_credit_entry_tenant  ON tenant_credit_entry (tenant_id, created_at DESC);
-- Reference-carrying kinds happen at most once per source object; a double-fire (webhook +
-- reconciler, double-click) hits 23505 and is treated as already-done.
CREATE UNIQUE INDEX IF NOT EXISTS uk_credit_entry_once_per_ref
    ON tenant_credit_entry (kind, reference_kind, reference_id)
    WHERE reference_id IS NOT NULL
      AND kind IN ('deposit_excess','refund_to_credit','redeem','redeem_reversal');

-- Credit as a tender on shop sales: how much of total_cents was paid with credit, and from
-- which account (so a failed payment or a refund can hand it back). The money paths (PI
-- amount, cash due, ledger entries) all use total - deposit_applied - credit_applied.
ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS credit_applied_cents int NOT NULL DEFAULT 0;
ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS credit_account_id uuid NULL REFERENCES tenant_credit_account(id) ON DELETE SET NULL;

-- Partial deposit refunds (the "deposit exceeds the bill" overage, refunded rather than
-- credited): track how much has been returned; deposit_refunded_at stays the fully-returned
-- stamp.
ALTER TABLE shop_work_order ADD COLUMN IF NOT EXISTS deposit_refunded_cents int NOT NULL DEFAULT 0 CHECK (deposit_refunded_cents >= 0);

-- A deposit can now legitimately refund twice (excess at pickup, remainder after a later sale
-- refund), so carve shop_wo_deposit out of the one-refund-entry-per-source index; idempotency
-- for those writes comes from the deposit_refunded_cents state-flip gate instead.
DROP INDEX IF EXISTS uk_tenant_ledger_entry_refund_per_source;
CREATE UNIQUE INDEX uk_tenant_ledger_entry_refund_per_source
    ON tenant_ledger_entry (tenant_id, source_kind, source_id)
    WHERE entry_kind = 'refund' AND source_kind IS NOT NULL AND source_id IS NOT NULL
      AND source_kind <> 'shop_wo_deposit';
