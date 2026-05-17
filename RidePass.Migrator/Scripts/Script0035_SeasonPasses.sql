-- Season passes: pre-paid passes valid for a date range, with three kinds of access:
--   * unlimited       — any number of rides during the season
--   * days_of_week    — only valid on certain weekdays (e.g., Mon-Fri only)
--   * credits         — N rides total over the season; each reservation burns one
--
-- Per-event-type perks let a pass include certain event types for free (or at a
-- discount). The discount-application pricing flow is wired in a later iteration —
-- this schema captures the configuration so tenants can set it up now.
--
-- Riders use their pass by reserving a spot at a specific event; the reservation
-- carries the QR token from the pass and is honored at the gate.

CREATE TABLE season_pass_product (
    id                              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name                            text        NOT NULL,
    description                     text        NULL,
    price_cents                     int         NOT NULL CHECK (price_cents > 0),
    valid_from_date                 date        NOT NULL,
    valid_to_date                   date        NOT NULL,
    kind                            text        NOT NULL CHECK (kind IN ('unlimited','days_of_week','credits')),
    valid_days_of_week              int[]       NULL,                    -- 0=Sun..6=Sat; only for kind='days_of_week'
    total_credits                   int         NULL CHECK (total_credits IS NULL OR total_credits > 0),
    requires_waiver                 boolean     NOT NULL DEFAULT true,
    rider_paid_service_charge_bps   int         NOT NULL DEFAULT 10000,
    is_active                       boolean     NOT NULL DEFAULT true,
    sort_order                      int         NOT NULL DEFAULT 100,
    created_at                      timestamptz NOT NULL DEFAULT now(),
    updated_at                      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_season_pass_dates CHECK (valid_to_date >= valid_from_date)
);

CREATE INDEX idx_season_pass_product_tenant ON season_pass_product (tenant_id) WHERE is_active = true;

CREATE TABLE season_pass_event_type_perk (
    id                  uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    pass_product_id     uuid NOT NULL REFERENCES season_pass_product(id) ON DELETE CASCADE,
    event_type_id       uuid NOT NULL REFERENCES tenant_event_type(id) ON DELETE CASCADE,
    discount_percent    int  NOT NULL CHECK (discount_percent BETWEEN 0 AND 100),  -- 100 = included
    UNIQUE (pass_product_id, event_type_id)
);

CREATE TABLE season_pass_purchase (
    id                              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    purchaser_user_id               uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    product_id                      uuid        NOT NULL REFERENCES season_pass_product(id) ON DELETE RESTRICT,
    waiver_signature_id             uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    stripe_payment_intent_id        text        NULL,
    amount_cents                    int         NOT NULL,
    service_charge_cents            int         NOT NULL DEFAULT 0,
    payment_method                  text        NOT NULL DEFAULT 'stripe' CHECK (payment_method IN ('stripe','cash','voucher')),
    status                          text        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','paid','cancelled','refunded')),
    purchaser_email                 text        NOT NULL,
    purchaser_name                  text        NOT NULL,
    redemption_token                uuid        NOT NULL DEFAULT uuid_generate_v4(),
    valid_from_date                 date        NOT NULL,
    valid_to_date                   date        NOT NULL,
    credits_remaining               int         NULL,    -- mutable counter; null for non-credit kinds
    cancellation_reason             text        NULL,
    cancelled_at                    timestamptz NULL,
    cancelled_by_user_id            uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    refund_note                     text        NULL,
    -- Selfie at purchase time so gate staff can verify the holder's identity. Stored
    -- as a base64 JPEG/PNG data URL, scaled client-side to ~600x600 to bound row size.
    photo_data_url                  text        NULL,
    created_at                      timestamptz NOT NULL DEFAULT now(),
    updated_at                      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uk_season_pass_purchase_token   ON season_pass_purchase (redemption_token);
CREATE INDEX idx_season_pass_purchase_user          ON season_pass_purchase (purchaser_user_id, status);
CREATE INDEX idx_season_pass_purchase_stripe        ON season_pass_purchase (stripe_payment_intent_id) WHERE stripe_payment_intent_id IS NOT NULL;

CREATE TABLE season_pass_reservation (
    id                          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    season_pass_purchase_id     uuid        NOT NULL REFERENCES season_pass_purchase(id) ON DELETE CASCADE,
    event_id                    uuid        NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    status                      text        NOT NULL DEFAULT 'reserved' CHECK (status IN ('reserved','checked_in','cancelled')),
    reserved_at                 timestamptz NOT NULL DEFAULT now(),
    checked_in_at               timestamptz NULL,
    cancelled_at                timestamptz NULL,
    UNIQUE (season_pass_purchase_id, event_id)
);

CREATE INDEX idx_season_pass_reservation_event ON season_pass_reservation (event_id) WHERE status <> 'cancelled';
