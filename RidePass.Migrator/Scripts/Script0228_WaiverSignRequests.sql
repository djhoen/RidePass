-- Signature requests: an admin emails a customer (or a whole event roster) a link to
-- sign a waiver before arrival. One row per outreach; the token in the emailed link is
-- the credential for the public /SignWaiver/{token} page (same posture as the rental
-- signing link). Status walks pending -> sent -> opened -> signed; cancelled is terminal.
-- Rerunnable: everything is IF NOT EXISTS. Backwards-compatible: purely additive.

CREATE TABLE IF NOT EXISTS waiver_sign_request (
    id                   uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id            uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- NULL = "whatever the tenant's active default waiver is when they open the link".
    waiver_id            uuid        NULL REFERENCES tenant_waiver(id) ON DELETE SET NULL,
    token                text        NOT NULL,
    recipient_email      text        NOT NULL,
    recipient_name       text        NULL,
    -- Set when the request came from a bulk "event roster" send.
    event_id             uuid        NULL REFERENCES event(id) ON DELETE SET NULL,
    status               text        NOT NULL DEFAULT 'pending'
                              CHECK (status IN ('pending', 'sent', 'opened', 'signed', 'cancelled')),
    signature_id         uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    requested_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at           timestamptz NOT NULL DEFAULT now(),
    sent_at              timestamptz NULL,
    opened_at            timestamptz NULL,
    signed_at            timestamptz NULL
);

-- The token is the public credential, so it must be globally unique.
CREATE UNIQUE INDEX IF NOT EXISTS uk_waiver_sign_request_token
    ON waiver_sign_request (token);

CREATE INDEX IF NOT EXISTS idx_waiver_sign_request_tenant
    ON waiver_sign_request (tenant_id, created_at DESC);

-- Bulk sends dedupe against open requests for the same address.
CREATE INDEX IF NOT EXISTS idx_waiver_sign_request_email
    ON waiver_sign_request (tenant_id, lower(recipient_email));
