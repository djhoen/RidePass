-- Concessions / store: a standalone in-person storefront (food, drink, swag) that a
-- cashier rings up through the mobile tap-to-pay app, separate from events. The buyer is
-- anonymous (no user attached); the cashier device is the authenticated SalesCounter client.
-- Per-tenant on/off flag, default OFF so it only appears once a tenant sets it up.

ALTER TABLE tenant ADD COLUMN IF NOT EXISTS concessions_enabled boolean NOT NULL DEFAULT false;

-- Catalog item. A burger/soda is just this row; a shirt/hat adds variant rows below.
CREATE TABLE concession_product (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    description text        NULL,
    category    text        NOT NULL DEFAULT 'other' CHECK (category IN ('food', 'drink', 'swag', 'other')),
    price_cents int         NOT NULL CHECK (price_cents >= 0),
    image_url   text        NULL,
    is_active   boolean     NOT NULL DEFAULT true,
    sort_order  int         NOT NULL DEFAULT 0,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_concession_product_tenant ON concession_product (tenant_id, is_active, sort_order);

-- Optional per-product variants (e.g. a shirt's S/M/L/XL). price_cents NULL = use the
-- product price; inventory NULL = unlimited stock.
CREATE TABLE concession_variant (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id  uuid        NOT NULL REFERENCES concession_product(id) ON DELETE CASCADE,
    size        text        NULL,
    color       text        NULL,
    price_cents int         NULL CHECK (price_cents IS NULL OR price_cents >= 0),
    image_url   text        NULL,
    inventory   int         NULL CHECK (inventory IS NULL OR inventory >= 0),
    is_active   boolean     NOT NULL DEFAULT true,
    sort_order  int         NOT NULL DEFAULT 0,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_concession_variant_product ON concession_variant (product_id);
-- One row per (product, size, color); blanks coalesced so a NULL size can't dupe.
CREATE UNIQUE INDEX uk_concession_variant
    ON concession_variant (product_id, COALESCE(size, ''), COALESCE(color, ''));

-- A cashier sale (anonymous buyer). One PaymentIntent per sale; the webhook flips it paid.
CREATE TABLE concession_sale (
    id                       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    status                   text        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'paid', 'failed', 'refunded')),
    subtotal_cents           int         NOT NULL,
    total_cents              int         NOT NULL,
    stripe_payment_intent_id text        NULL,
    sold_by_user_id          uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at               timestamptz NOT NULL DEFAULT now(),
    paid_at                  timestamptz NULL
);
CREATE INDEX idx_concession_sale_tenant ON concession_sale (tenant_id, created_at DESC);
CREATE INDEX idx_concession_sale_pi
    ON concession_sale (stripe_payment_intent_id) WHERE stripe_payment_intent_id IS NOT NULL;

-- Line items. Name/price snapshotted so later catalog edits don't rewrite sale history.
CREATE TABLE concession_sale_line (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    sale_id          uuid NOT NULL REFERENCES concession_sale(id) ON DELETE CASCADE,
    product_id       uuid NULL REFERENCES concession_product(id) ON DELETE SET NULL,
    variant_id       uuid NULL REFERENCES concession_variant(id) ON DELETE SET NULL,
    name_snapshot    text NOT NULL,
    variant_label    text NULL,
    unit_price_cents int  NOT NULL,
    quantity         int  NOT NULL CHECK (quantity > 0),
    line_total_cents int  NOT NULL
);
CREATE INDEX idx_concession_sale_line_sale ON concession_sale_line (sale_id);
CREATE INDEX idx_concession_sale_line_variant ON concession_sale_line (variant_id) WHERE variant_id IS NOT NULL;

-- Allow 'concession' as a ledger source_kind so paid concession sales record revenue and
-- flow to tenant balance / payouts like every other sale kind.
ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
    CHECK (source_kind IS NULL OR source_kind IN
        ('pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras', 'concession'));
