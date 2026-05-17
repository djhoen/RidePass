-- Event "extras": camping passes, parking passes, pit-vehicle passes, and any
-- custom add-ons the tenant defines (RV hookup, locker, etc.). One unified
-- table covers all kinds — `kind` is free text so tenants can add their own
-- categories. The Vue picker offers Camping / Parking / Pit Vehicle as
-- defaults plus a "Custom..." option.
--
-- Inventory lives on the per-event eligibility row, not on the product —
-- different events at the same track have different physical capacities
-- (10 camp spots one weekend, 20 the next).

CREATE TABLE event_extra_product (
    id                              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name                            text        NOT NULL,
    description                     text        NULL,
    image_url                       text        NULL,
    -- Free-form kind label. Defaults: 'camping' | 'parking' | 'pit_vehicle'.
    -- Tenants can type any other label (slugified client-side for grouping).
    kind                            text        NOT NULL,
    price_cents                     int         NOT NULL CHECK (price_cents >= 0),
    rider_paid_service_charge_bps   int         NOT NULL DEFAULT 10000
                                                CHECK (rider_paid_service_charge_bps BETWEEN 0 AND 10000),
    requires_waiver                 boolean     NOT NULL DEFAULT false,
    is_active                       boolean     NOT NULL DEFAULT true,
    sort_order                      int         NOT NULL DEFAULT 100,
    created_at                      timestamptz NOT NULL DEFAULT now(),
    updated_at                      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_event_extra_product_tenant
    ON event_extra_product (tenant_id, is_active, sort_order);
CREATE TRIGGER trg_event_extra_product_updated_at
    BEFORE UPDATE ON event_extra_product
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- Per-event allow-list with per-event inventory. inventory NULL = unlimited
-- at this event; inventory > 0 = capped to that count.
CREATE TABLE event_extra_eligibility (
    event_id    uuid NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    product_id  uuid NOT NULL REFERENCES event_extra_product(id) ON DELETE CASCADE,
    inventory   int  NULL CHECK (inventory IS NULL OR inventory > 0),
    PRIMARY KEY (event_id, product_id)
);

CREATE INDEX idx_event_extra_eligibility_product
    ON event_extra_eligibility (product_id);


CREATE TABLE event_extra_purchase (
    id                            uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    event_id                      uuid        NOT NULL REFERENCES event(id) ON DELETE RESTRICT,
    product_id                    uuid        NOT NULL REFERENCES event_extra_product(id) ON DELETE RESTRICT,
    purchaser_user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    purchaser_email               text        NOT NULL,
    purchaser_name                text        NOT NULL,
    waiver_signature_id           uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    quantity                      int         NOT NULL DEFAULT 1 CHECK (quantity >= 1),
    -- Snapshot the price per unit at purchase time so future product price
    -- changes don't retro-edit historical sales.
    unit_price_cents_frozen       int         NOT NULL,
    amount_cents                  int         NOT NULL,
    service_charge_cents          int         NOT NULL DEFAULT 0,
    stripe_payment_intent_id      text        NULL,
    redemption_token              uuid        NOT NULL DEFAULT uuid_generate_v4(),
    -- pending  = PI created, awaiting confirm
    -- paid     = PI succeeded, capacity firmly held
    -- redeemed = scanned at the gate
    -- cancelled / failed / refunded
    status                        text        NOT NULL DEFAULT 'pending'
                                              CHECK (status IN ('pending','paid','redeemed','cancelled','failed','refunded')),
    redeemed_at_utc               timestamptz NULL,
    redeemed_by_user_id           uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    cancelled_reason              text        NULL,
    cancelled_by_user_id          uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    cancelled_at                  timestamptz NULL,
    refund_note                   text        NULL,
    payment_method                text        NOT NULL DEFAULT 'stripe',
    created_at                    timestamptz NOT NULL DEFAULT now(),
    updated_at                    timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uk_event_extra_purchase_token ON event_extra_purchase (redemption_token);
CREATE INDEX idx_event_extra_purchase_tenant ON event_extra_purchase (tenant_id, status, created_at DESC);
CREATE INDEX idx_event_extra_purchase_user ON event_extra_purchase (purchaser_user_id, created_at DESC);
CREATE INDEX idx_event_extra_purchase_event_status
    ON event_extra_purchase (event_id, product_id, status);
CREATE INDEX idx_event_extra_purchase_pi
    ON event_extra_purchase (stripe_payment_intent_id)
    WHERE stripe_payment_intent_id IS NOT NULL;
CREATE TRIGGER trg_event_extra_purchase_updated_at
    BEFORE UPDATE ON event_extra_purchase
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- Tenant feature flag — opt-in like gift cards / rentals.
ALTER TABLE tenant
    ADD COLUMN extras_enabled boolean NOT NULL DEFAULT false;
