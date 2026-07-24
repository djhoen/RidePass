-- Packages: a first-class bundled product (modeled on Highland's "Find Your Ride"):
-- a coached session + day/lift admission + a bike + gear, sold at a day-type tiered
-- price, with its own landing page. A package purchase composes a real gate ticket
-- (scannable) and a real shop rental (deposit-held, collectible at the shop), grouped
-- under one package_purchase and settled under one payment.
-- All additive + IF NOT EXISTS, so the script is rerunnable.

-- ── The product ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS package_product (
    id                        uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                 uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name                      text        NOT NULL,
    slug                      text        NULL,
    summary                   text        NULL,        -- one-line, shown on cards/lists
    -- Landing-page content (mirrors season_pass_product's landing model).
    description               text        NULL,        -- rich HTML marketing body
    hero_image_url            text        NULL,
    landing_published         boolean     NOT NULL DEFAULT false,
    -- What the bundle grants. Day admission is booked against the tenant's event of
    -- this type on the chosen date (its rider gate tier).
    includes_day_ticket       boolean     NOT NULL DEFAULT true,
    day_ticket_event_type_code text       NOT NULL DEFAULT 'open_ride',
    -- Coaching: null = no coached session; otherwise the session length. Session times
    -- come from package_session_slot.
    coaching_minutes          int         NULL CHECK (coaching_minutes IS NULL OR coaching_minutes > 0),
    coaching_label            text        NULL,        -- e.g. "Park Ready session"
    is_active                 boolean     NOT NULL DEFAULT true,
    sort_order                int         NOT NULL DEFAULT 0,
    valid_from_date           date        NULL,
    valid_to_date             date        NULL,
    created_at                timestamptz NOT NULL DEFAULT now(),
    updated_at                timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_package_product_tenant
    ON package_product (tenant_id, is_active, sort_order);
-- Slug is the marketing URL; unique per tenant, case-insensitive, when present.
CREATE UNIQUE INDEX IF NOT EXISTS uk_package_product_slug
    ON package_product (tenant_id, lower(slug)) WHERE slug IS NOT NULL;

-- ── Priced day-type tiers (Midweek / Weekend / Afternoon / 3-Pack) ──────────
CREATE TABLE IF NOT EXISTS package_tier (
    id            uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    package_id    uuid        NOT NULL REFERENCES package_product(id) ON DELETE CASCADE,
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name          text        NOT NULL,
    price_cents   int         NOT NULL CHECK (price_cents >= 0),
    -- Which days this tier applies to; the booking validates the chosen date against it.
    day_scope     text        NOT NULL DEFAULT 'any' CHECK (day_scope IN ('any', 'weekday', 'weekend')),
    -- Afternoon-only tiers restrict to the late session slots (slot.is_afternoon).
    afternoon_only boolean    NOT NULL DEFAULT false,
    -- Multi-visit packs (the 3-Pack): how many sessions the price covers.
    session_count int         NOT NULL DEFAULT 1 CHECK (session_count >= 1),
    sort_order    int         NOT NULL DEFAULT 0,
    is_active     boolean     NOT NULL DEFAULT true
);
CREATE INDEX IF NOT EXISTS idx_package_tier_package ON package_tier (package_id, sort_order);

-- ── Bookable coached session slots (weekday vs weekend times) ───────────────
CREATE TABLE IF NOT EXISTS package_session_slot (
    id               uuid    PRIMARY KEY DEFAULT uuid_generate_v4(),
    package_id       uuid    NOT NULL REFERENCES package_product(id) ON DELETE CASCADE,
    tenant_id        uuid    NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    day_scope        text    NOT NULL DEFAULT 'any' CHECK (day_scope IN ('any', 'weekday', 'weekend')),
    start_time       time    NOT NULL,
    is_afternoon     boolean NOT NULL DEFAULT false,
    -- How many riders can book this slot on a given date (coaching capacity).
    capacity         int     NOT NULL DEFAULT 8 CHECK (capacity > 0),
    instructor_id    uuid    NULL REFERENCES instructor(id) ON DELETE SET NULL,
    sort_order       int     NOT NULL DEFAULT 0,
    is_active        boolean NOT NULL DEFAULT true
);
CREATE INDEX IF NOT EXISTS idx_package_session_slot_package ON package_session_slot (package_id, sort_order);

-- ── Included rental gear (the bike + each gear piece) ───────────────────────
CREATE TABLE IF NOT EXISTS package_item (
    id          uuid    PRIMARY KEY DEFAULT uuid_generate_v4(),
    package_id  uuid    NOT NULL REFERENCES package_product(id) ON DELETE CASCADE,
    tenant_id   uuid    NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- 'bike' is the primary rental; 'gear' is a helmet/pads/etc. Both resolve to a
    -- rentable shop_variant, so the deposit and inventory ride the rental engine.
    item_type   text    NOT NULL CHECK (item_type IN ('bike', 'gear')),
    variant_id  uuid    NOT NULL REFERENCES shop_variant(id) ON DELETE RESTRICT,
    quantity    int     NOT NULL DEFAULT 1 CHECK (quantity > 0),
    sort_order  int     NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_package_item_package ON package_item (package_id, sort_order);

-- ── The composed purchase ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS package_purchase (
    id                        uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                 uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    package_id                uuid        NOT NULL REFERENCES package_product(id) ON DELETE RESTRICT,
    tier_id                   uuid        NULL REFERENCES package_tier(id) ON DELETE SET NULL,
    buyer_user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    buyer_name                text        NULL,
    buyer_email               text        NULL,
    ride_date                 date        NOT NULL,
    session_start_at          timestamptz NULL,        -- the chosen coached slot (null = no coaching)
    slot_id                   uuid        NULL REFERENCES package_session_slot(id) ON DELETE SET NULL,
    instructor_id             uuid        NULL REFERENCES instructor(id) ON DELETE SET NULL,
    status                    text        NOT NULL DEFAULT 'pending'
                                  CHECK (status IN ('pending', 'paid', 'cancelled', 'failed')),
    subtotal_cents            int         NOT NULL DEFAULT 0,
    tax_cents                 int         NOT NULL DEFAULT 0,
    total_cents               int         NOT NULL DEFAULT 0,
    deposit_cents             int         NOT NULL DEFAULT 0,
    service_charge_cents      int         NOT NULL DEFAULT 0,
    payment_intent_id         text        NULL,
    deposit_intent_id         text        NULL,
    stripe_connected_account_id text      NULL,
    order_number              int         NULL,
    receipt_token             uuid        NOT NULL DEFAULT uuid_generate_v4(),
    -- The real artifacts this bundle created, so each half stays a first-class row.
    event_ticket_purchase_id  uuid        NULL REFERENCES event_ticket_purchase(id) ON DELETE SET NULL,
    shop_rental_id            uuid        NULL REFERENCES shop_rental(id) ON DELETE SET NULL,
    created_at                timestamptz NOT NULL DEFAULT now(),
    paid_at                   timestamptz NULL
);
CREATE INDEX IF NOT EXISTS idx_package_purchase_tenant ON package_purchase (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_package_purchase_pi ON package_purchase (payment_intent_id) WHERE payment_intent_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_package_purchase_buyer ON package_purchase (tenant_id, buyer_user_id);
-- One booking per slot+date pair counts toward the slot's capacity; the availability
-- check reads this. (A partial unique index would over-constrain; capacity is a count.)
CREATE INDEX IF NOT EXISTS idx_package_purchase_slot_date
    ON package_purchase (slot_id, ride_date) WHERE status <> 'cancelled';

DROP TRIGGER IF EXISTS trg_package_product_updated_at ON package_product;
CREATE TRIGGER trg_package_product_updated_at
    BEFORE UPDATE ON package_product
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
