-- Phase 4: day pass sales + waivers.

ALTER TABLE tenant ADD COLUMN require_reservation_for_passes boolean NOT NULL DEFAULT false;

CREATE TABLE day_pass_product (
    id           uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id    uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name         text        NOT NULL,
    description  text        NULL,
    price_cents  int         NOT NULL CHECK (price_cents > 0),
    is_active    boolean     NOT NULL DEFAULT true,
    sort_order   int         NOT NULL DEFAULT 100,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_day_pass_product_tenant ON day_pass_product (tenant_id, is_active);

CREATE TRIGGER trg_day_pass_product_updated_at
    BEFORE UPDATE ON day_pass_product
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE tenant_waiver (
    id          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    version     int         NOT NULL,
    title       text        NOT NULL DEFAULT 'Waiver & Release of Liability',
    body        text        NOT NULL DEFAULT '',
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uk_tenant_waiver_version UNIQUE (tenant_id, version)
);

-- Only one active waiver per tenant.
CREATE UNIQUE INDEX uk_tenant_waiver_active
    ON tenant_waiver (tenant_id)
    WHERE is_active = true;

CREATE TRIGGER trg_tenant_waiver_updated_at
    BEFORE UPDATE ON tenant_waiver
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE rider_waiver_signature (
    id         uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id  uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id    uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    waiver_id  uuid        NOT NULL REFERENCES tenant_waiver(id) ON DELETE CASCADE,
    signed_at  timestamptz NOT NULL DEFAULT now(),
    ip_address text        NULL,
    CONSTRAINT uk_rider_waiver_once UNIQUE (user_id, waiver_id)
);

CREATE INDEX idx_rider_waiver_sig_user ON rider_waiver_signature (tenant_id, user_id);

CREATE TABLE day_pass_purchase (
    id                         uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                  uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    purchaser_user_id          uuid        NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    product_id                 uuid        NOT NULL REFERENCES day_pass_product(id) ON DELETE RESTRICT,
    waiver_signature_id        uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    valid_on_date              date        NULL,
    stripe_payment_intent_id   text        NULL,
    amount_cents               int         NOT NULL,
    status                     text        NOT NULL DEFAULT 'pending'
                                           CHECK (status IN ('pending','paid','failed','refunded','redeemed')),
    purchaser_email            text        NOT NULL,
    purchaser_name             text        NOT NULL,
    created_at                 timestamptz NOT NULL DEFAULT now(),
    updated_at                 timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_day_pass_purchase_tenant_status ON day_pass_purchase (tenant_id, status);
CREATE UNIQUE INDEX uk_day_pass_purchase_stripe_pi
    ON day_pass_purchase (stripe_payment_intent_id)
    WHERE stripe_payment_intent_id IS NOT NULL;

CREATE TRIGGER trg_day_pass_purchase_updated_at
    BEFORE UPDATE ON day_pass_purchase
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Seed an initial empty waiver v1 for each new tenant.
CREATE OR REPLACE FUNCTION seed_initial_waiver()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO tenant_waiver (tenant_id, version, title, body, is_active)
    VALUES (NEW.id, 1, 'Waiver & Release of Liability', '', true)
    ON CONFLICT DO NOTHING;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_tenant_insert_waiver
    AFTER INSERT ON tenant
    FOR EACH ROW EXECUTE FUNCTION seed_initial_waiver();

-- Backfill waivers for existing tenants.
INSERT INTO tenant_waiver (tenant_id, version, title, body, is_active)
SELECT t.id, 1, 'Waiver & Release of Liability', '', true
FROM tenant t
WHERE NOT EXISTS (SELECT 1 FROM tenant_waiver tw WHERE tw.tenant_id = t.id);
