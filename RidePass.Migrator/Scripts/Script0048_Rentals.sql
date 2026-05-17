-- Rentals: tenants offer gear (helmets, gloves, goggles) and bikes for daily rental.
-- Two inventory models per product:
--   * pool      → identical units counted as a number ("8 size-M helmets"). Cheaper
--                 to manage; condition is tracked at the SKU level only.
--   * per_item  → distinct units (rental_item rows) with serial / condition history.
--                 Worth it for bikes you actually want to track per-machine.
--
-- Capacity rule for both models: at booking time, sum(quantity) over rental_purchase
-- rows whose [start_date, end_date] overlaps the requested window AND whose status
-- is reservation-holding ('paid' / 'out') must remain ≤ inventory. For per_item
-- products inventory = count(rental_item.status='available').
--
-- Money: rental fee charged immediately (rental_pi_id). Deposit is a separate
-- pre-auth (deposit_pi_id) with capture_method='manual'; on return we either
-- void it or capture some/all of it for damage. Stripe pre-auths expire after
-- 7 days, so long rentals will need a re-auth flow — out of scope for MVP.

CREATE TABLE rental_product (
    id                       uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name                     text        NOT NULL,
    description              text        NULL,
    image_url                text        NULL,
    daily_rate_cents         int         NOT NULL CHECK (daily_rate_cents >= 0),
    deposit_cents            int         NOT NULL DEFAULT 0 CHECK (deposit_cents >= 0),
    tracking_kind            text        NOT NULL DEFAULT 'pool'
                                         CHECK (tracking_kind IN ('pool','per_item')),
    -- Used only when tracking_kind='pool'. For per_item, count of rental_item rows
    -- with status='available' is the effective inventory.
    inventory_pool           int         NULL CHECK (inventory_pool IS NULL OR inventory_pool > 0),
    requires_waiver          boolean     NOT NULL DEFAULT true,
    rider_paid_service_charge_bps int    NOT NULL DEFAULT 10000
                                         CHECK (rider_paid_service_charge_bps BETWEEN 0 AND 10000),
    is_active                boolean     NOT NULL DEFAULT true,
    sort_order               int         NOT NULL DEFAULT 100,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_rental_product_tenant ON rental_product (tenant_id, is_active, sort_order);
CREATE TRIGGER trg_rental_product_updated_at BEFORE UPDATE ON rental_product
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


CREATE TABLE rental_item (
    id          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    product_id  uuid        NOT NULL REFERENCES rental_product(id) ON DELETE CASCADE,
    label       text        NOT NULL,        -- "Bike A", "Helmet #12"
    serial      text        NULL,
    notes       text        NULL,
    -- 'available' = bookable; 'maintenance' = temporarily out of pool;
    -- 'retired' = permanently retired, retained for history.
    status      text        NOT NULL DEFAULT 'available'
                            CHECK (status IN ('available','maintenance','retired')),
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_rental_item_product ON rental_item (product_id, status);
CREATE TRIGGER trg_rental_item_updated_at BEFORE UPDATE ON rental_item
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


CREATE TABLE rental_purchase (
    id                            uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    product_id                    uuid        NOT NULL REFERENCES rental_product(id) ON DELETE RESTRICT,
    purchaser_user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    purchaser_email               text        NOT NULL,
    purchaser_name                text        NOT NULL,
    waiver_signature_id           uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    -- Inclusive date range, expressed in tenant tz at the time of booking.
    start_date                    date        NOT NULL,
    end_date                      date        NOT NULL CHECK (end_date >= start_date),
    quantity                      int         NOT NULL CHECK (quantity >= 1),
    -- Snapshot of the daily rate at booking time so future product price changes
    -- don't retro-edit historical rentals.
    daily_rate_cents_frozen       int         NOT NULL,
    days_count                    int         NOT NULL CHECK (days_count >= 1),
    amount_cents                  int         NOT NULL,
    service_charge_cents          int         NOT NULL DEFAULT 0,
    deposit_cents                 int         NOT NULL DEFAULT 0,
    rental_pi_id                  text        NULL,
    deposit_pi_id                 text        NULL,
    deposit_captured_cents        int         NOT NULL DEFAULT 0,
    redemption_token              uuid        NOT NULL DEFAULT uuid_generate_v4(),
    -- pending  = PI created, awaiting rider confirm
    -- paid     = PI succeeded; capacity is reserved for this window
    -- out      = counter handed equipment over; checked_out_at set
    -- returned = equipment back; deposit voided / captured per damage outcome
    -- damaged  = like returned but with a non-zero deposit_captured_cents
    -- cancelled / failed = not reserving capacity
    status                        text        NOT NULL DEFAULT 'pending'
                                              CHECK (status IN
                                                ('pending','paid','out','returned','damaged','cancelled','failed')),
    checked_out_at                timestamptz NULL,
    returned_at                   timestamptz NULL,
    condition_notes               text        NULL,
    payment_method                text        NOT NULL DEFAULT 'stripe',
    cancelled_reason              text        NULL,
    cancelled_by_user_id          uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    applied_reward_redemption_id  uuid        NULL REFERENCES reward_redemption(id) ON DELETE SET NULL,
    created_at                    timestamptz NOT NULL DEFAULT now(),
    updated_at                    timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uk_rental_purchase_token ON rental_purchase (redemption_token);
CREATE INDEX idx_rental_purchase_tenant ON rental_purchase (tenant_id, status, start_date);
-- Hot path: capacity check by product over a date window.
CREATE INDEX idx_rental_purchase_product_window ON rental_purchase
    (product_id, status, start_date, end_date);
CREATE INDEX idx_rental_purchase_user ON rental_purchase (purchaser_user_id);
CREATE INDEX idx_rental_purchase_pi ON rental_purchase (rental_pi_id) WHERE rental_pi_id IS NOT NULL;
CREATE INDEX idx_rental_purchase_deposit_pi ON rental_purchase (deposit_pi_id) WHERE deposit_pi_id IS NOT NULL;
CREATE TRIGGER trg_rental_purchase_updated_at BEFORE UPDATE ON rental_purchase
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- Per-item assignment table. Empty for pool-tracked products. A rental_purchase of
-- quantity N against a per_item product will have N rows here, each pointing at a
-- specific rental_item.
CREATE TABLE rental_purchase_item (
    id           uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    purchase_id  uuid NOT NULL REFERENCES rental_purchase(id) ON DELETE CASCADE,
    item_id      uuid NOT NULL REFERENCES rental_item(id) ON DELETE RESTRICT,
    UNIQUE (purchase_id, item_id)
);
CREATE INDEX idx_rental_purchase_item_item ON rental_purchase_item (item_id);


-- Extend redemption + ledger source_kind constraints so coupons / gift cards / payouts
-- can reference rental purchases the same way they reference other sale kinds.
ALTER TABLE coupon_redemption DROP CONSTRAINT coupon_redemption_source_kind_check;
ALTER TABLE coupon_redemption ADD CONSTRAINT coupon_redemption_source_kind_check
    CHECK (source_kind IN ('day_pass','event_ticket','season_pass','rental'));

ALTER TABLE gift_card_redemption DROP CONSTRAINT gift_card_redemption_source_kind_check;
ALTER TABLE gift_card_redemption ADD CONSTRAINT gift_card_redemption_source_kind_check
    CHECK (source_kind IN ('day_pass','event_ticket','season_pass','rental'));

ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
    CHECK (source_kind IS NULL OR source_kind IN ('day_pass','event_ticket','season_pass','rental'));


-- Coupons can target rentals too (or 'all' as before). Existing 'all'-scoped
-- coupons still apply to rentals; this just lets tenants narrow to rentals only.
ALTER TABLE coupon DROP CONSTRAINT coupon_applicable_scope_check;
ALTER TABLE coupon ADD CONSTRAINT coupon_applicable_scope_check
    CHECK (applicable_scope IN ('all','day_pass','event_ticket','season_pass','rental'));


-- Tenant feature flag — opt-in like gift cards. New tenants (and existing on apply)
-- default to false until an admin enables it on Settings → Rentals.
ALTER TABLE tenant
    ADD COLUMN rentals_enabled boolean NOT NULL DEFAULT false;
