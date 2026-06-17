-- Records each Loam Pass credit drawn for a RidePass entry: which linked LoamMx account it
-- came from, the destination, and the idempotency key used on the LoamMx side. This lets a
-- refund reverse exactly that redemption (un-redeem the right account's credit), and gives a
-- per-entry audit trail. One redemption per event-ticket entry.

CREATE TABLE loampass_redemption (
    id                       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    event_ticket_purchase_id uuid        NOT NULL UNIQUE REFERENCES event_ticket_purchase(id) ON DELETE CASCADE,
    loampass_account_id      text        NOT NULL,
    destination_id           text        NOT NULL,
    idempotency_key          text        NOT NULL,
    status                   text        NOT NULL DEFAULT 'redeemed' CHECK (status IN ('redeemed', 'refunded')),
    created_at               timestamptz NOT NULL DEFAULT now(),
    refunded_at              timestamptz NULL
);
CREATE INDEX idx_loampass_redemption_tenant ON loampass_redemption (tenant_id);
