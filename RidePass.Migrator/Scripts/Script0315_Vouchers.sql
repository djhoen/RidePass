-- Vouchers (Highland go-live item 11): free single-admission codes that staff give to friends and
-- that admins mint in batches for vendors ("10 free passes for Red Bull"). Each code is an ordinary
-- coupon row (100% off, event_ticket scope, one use) tagged with the batch it came from, so it
-- redeems through the same checkout and Gate Sale paths and the redemption is a normal ticket with
-- the normal check-in. What the batch adds is who issued it, for whom, and the allowance count.

CREATE TABLE IF NOT EXISTS voucher_batch (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- 'employee': a staff member giving codes from their own allowance.
    -- 'vendor':   an admin minting a list of codes for an outside party; no allowance.
    kind                text        NOT NULL CHECK (kind IN ('employee', 'vendor')),
    issued_by_user_id   uuid        NOT NULL REFERENCES users(id),
    -- Who the codes are for: the friend's name, or the vendor ("Red Bull").
    label               text        NULL,
    recipient_email     text        NULL,
    note                text        NULL,
    code_count          int         NOT NULL CHECK (code_count BETWEEN 1 AND 500),
    -- Optional narrowing to one event; NULL = any day's rider admission.
    event_id            uuid        NULL REFERENCES event(id) ON DELETE SET NULL,
    expires_at_utc      timestamptz NULL,
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_voucher_batch_tenant ON voucher_batch (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_voucher_batch_issuer ON voucher_batch (tenant_id, issued_by_user_id, created_at DESC);

ALTER TABLE coupon ADD COLUMN IF NOT EXISTS voucher_batch_id uuid NULL REFERENCES voucher_batch(id) ON DELETE CASCADE;
-- When a code was voided (unused codes only), so an employee gets it back on their allowance.
ALTER TABLE coupon ADD COLUMN IF NOT EXISTS voucher_voided_at timestamptz NULL;
CREATE INDEX IF NOT EXISTS idx_coupon_voucher_batch ON coupon (voucher_batch_id) WHERE voucher_batch_id IS NOT NULL;

-- A voucher admits one rider once. Callers hold an advisory lock on the code while they check and
-- record; this index is the backstop, so two registers can never both redeem one code.
ALTER TABLE coupon_redemption ADD COLUMN IF NOT EXISTS is_voucher boolean NOT NULL DEFAULT false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_coupon_redemption_voucher_once
    ON coupon_redemption (coupon_id) WHERE is_voucher;

-- How many codes a staff member may give away per calendar year (tenant time). No row = none.
-- NULL allowance = unlimited. Admins are unlimited without a row.
CREATE TABLE IF NOT EXISTS staff_voucher_allowance (
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id             uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    yearly_allowance    int         NULL CHECK (yearly_allowance IS NULL OR yearly_allowance BETWEEN 0 AND 1000),
    updated_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    updated_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, user_id)
);
