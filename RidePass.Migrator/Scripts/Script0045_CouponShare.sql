-- Tracks every time a rider sends a coupon code to someone else from My Passes.
-- Captures the recipient's name + email so the tenant has a marketing-ready list of
-- "people who got pitched on this track but haven't bought yet". Multiple shares per
-- coupon are allowed (a rider may resend if the friend loses the email).
--
-- redeemed_at is denormalized convenience: when the recipient actually buys with the
-- code, a backfill from coupon_redemption fills it in. Keeps marketing reports fast
-- without a join.

CREATE TABLE coupon_share (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    coupon_id       uuid        NOT NULL REFERENCES coupon(id) ON DELETE CASCADE,
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    sender_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    recipient_email text        NOT NULL,
    recipient_name  text        NULL,
    personal_note   text        NULL,
    sent_at         timestamptz NOT NULL DEFAULT now(),
    redeemed_at     timestamptz NULL
);

CREATE INDEX idx_coupon_share_coupon ON coupon_share (coupon_id, sent_at DESC);
-- Marketing pulls: "all recipient emails this tenant has captured, deduped".
CREATE INDEX idx_coupon_share_tenant_email ON coupon_share (tenant_id, lower(recipient_email));
