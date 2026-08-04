-- Customer-facing display for the bike shop counter: a second tablet that mirrors the charges
-- being rung up (register cart or rental quote) and lets the customer read and sign the rental
-- agreement and the waiver without turning the staff tablet around. Same relay pattern as
-- concession_display (staff device pushes state_json, display polls), but the back-channel is a
-- generic response_json (signature + signer details) instead of a tip amount. Every state push
-- clears response_json so a stale signature can never attach to the wrong request; the staff
-- device only accepts a response whose requestId matches the outstanding one.

CREATE TABLE IF NOT EXISTS shop_display (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    pair_code     text        NOT NULL,
    state_json    text        NULL,
    response_json text        NULL,
    updated_at    timestamptz NOT NULL DEFAULT now(),
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_display_tenant_code ON shop_display (tenant_id, pair_code);
