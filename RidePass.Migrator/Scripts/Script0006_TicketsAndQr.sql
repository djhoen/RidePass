-- Phase 5: QR redemption tokens + event ticket tiers + event ticket purchases.

-- Add redemption_token to day_pass_purchase (backfill existing rows).
ALTER TABLE day_pass_purchase ADD COLUMN redemption_token uuid NULL;
UPDATE day_pass_purchase SET redemption_token = uuid_generate_v4() WHERE redemption_token IS NULL;
ALTER TABLE day_pass_purchase ALTER COLUMN redemption_token SET NOT NULL;
ALTER TABLE day_pass_purchase ALTER COLUMN redemption_token SET DEFAULT uuid_generate_v4();
CREATE UNIQUE INDEX uk_day_pass_purchase_token ON day_pass_purchase (redemption_token);

CREATE TABLE event_ticket_tier (
    id           uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id    uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    event_id     uuid        NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    name         text        NOT NULL,
    price_cents  int         NOT NULL CHECK (price_cents > 0),
    inventory    int         NULL CHECK (inventory IS NULL OR inventory > 0),
    sort_order   int         NOT NULL DEFAULT 100,
    is_active    boolean     NOT NULL DEFAULT true,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_event_ticket_tier_event ON event_ticket_tier (event_id, is_active);
CREATE INDEX idx_event_ticket_tier_tenant ON event_ticket_tier (tenant_id);

CREATE TRIGGER trg_event_ticket_tier_updated_at
    BEFORE UPDATE ON event_ticket_tier
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE event_ticket_purchase (
    id                       uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    tier_id                  uuid        NOT NULL REFERENCES event_ticket_tier(id) ON DELETE RESTRICT,
    purchaser_user_id        uuid        NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    stripe_payment_intent_id text        NULL,
    amount_cents             int         NOT NULL,
    status                   text        NOT NULL DEFAULT 'pending'
                                         CHECK (status IN ('pending','paid','failed','refunded','redeemed')),
    purchaser_email          text        NOT NULL,
    purchaser_name           text        NOT NULL,
    redemption_token         uuid        NOT NULL DEFAULT uuid_generate_v4(),
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_event_ticket_purchase_tenant_status ON event_ticket_purchase (tenant_id, status);
CREATE UNIQUE INDEX uk_event_ticket_purchase_token ON event_ticket_purchase (redemption_token);
CREATE UNIQUE INDEX uk_event_ticket_purchase_stripe_pi
    ON event_ticket_purchase (stripe_payment_intent_id)
    WHERE stripe_payment_intent_id IS NOT NULL;

CREATE TRIGGER trg_event_ticket_purchase_updated_at
    BEFORE UPDATE ON event_ticket_purchase
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
