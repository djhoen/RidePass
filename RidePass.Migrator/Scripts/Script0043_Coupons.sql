-- Tenant-managed coupon codes that riders can type in at checkout. Distinct from
-- the existing reward voucher system (which mints single-use tokens earned through
-- behavior); coupons are free-text codes with reuse limits, scope filters, and
-- date windows. Phase 2 will extend this table with issued_to_user_id + batch_id
-- so racers can be issued personal coupon batches off a race-entry purchase.

CREATE TABLE coupon (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    code                text        NOT NULL,
    description         text        NULL,
    -- 'percent' = discount_value is bps (10000 = 100%). 'amount' = discount_value is cents.
    -- Storing percent as bps keeps the math integer-only and matches the rest of the codebase.
    discount_kind       text        NOT NULL CHECK (discount_kind IN ('percent', 'amount')),
    discount_value      int         NOT NULL CHECK (discount_value > 0),
    -- 'all' applies to any purchase; the others narrow to a single product line.
    -- applicable_event_id, when set, narrows further to a specific event (race-day promo).
    applicable_scope    text        NOT NULL DEFAULT 'all'
                                    CHECK (applicable_scope IN ('all', 'day_pass', 'event_ticket', 'season_pass')),
    applicable_event_id uuid        NULL REFERENCES event(id) ON DELETE SET NULL,
    valid_from_utc      timestamptz NULL,
    valid_to_utc        timestamptz NULL,
    max_total_uses      int         NULL CHECK (max_total_uses IS NULL OR max_total_uses > 0),
    max_uses_per_user   int         NULL CHECK (max_uses_per_user IS NULL OR max_uses_per_user > 0),
    is_active           boolean     NOT NULL DEFAULT true,
    created_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);

-- Codes are unique per tenant. Case-insensitive so SUMMER25 == summer25 at the gate.
CREATE UNIQUE INDEX uk_coupon_tenant_code ON coupon (tenant_id, lower(code));
CREATE INDEX idx_coupon_tenant_active ON coupon (tenant_id) WHERE is_active = true;

CREATE TRIGGER trg_coupon_updated_at
    BEFORE UPDATE ON coupon
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Each application of a coupon to a purchase. Used for max-total-uses and max-uses-per-user
-- enforcement, plus reporting. source_kind/source_id mirror the ledger pattern so we can
-- attribute the redemption back to the specific day_pass / event_ticket / season_pass row.
CREATE TABLE coupon_redemption (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    coupon_id           uuid        NOT NULL REFERENCES coupon(id) ON DELETE CASCADE,
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    source_kind         text        NOT NULL CHECK (source_kind IN ('day_pass', 'event_ticket', 'season_pass')),
    source_id           uuid        NOT NULL,
    discount_cents      int         NOT NULL,    -- the actual amount taken off, post-rounding
    redeemed_at         timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_coupon_redemption_coupon ON coupon_redemption (coupon_id, redeemed_at DESC);
CREATE INDEX idx_coupon_redemption_user ON coupon_redemption (coupon_id, user_id) WHERE user_id IS NOT NULL;
-- One coupon per source row is the cap (no double-apply on retries / refunded-and-rebought).
CREATE UNIQUE INDEX uk_coupon_redemption_source ON coupon_redemption (source_kind, source_id);
