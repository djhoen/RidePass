-- Bike shop, Phase 2: retail sale + tax.
--
-- The register sells variants off the shared catalog. This adds the sale tables, a per-line tax
-- config mirroring the concessions pattern, the ledger source kind, and the v_recent_sales branch so
-- a shop sale shows up in the admin dashboard and Purchases list like every other sale kind. Payment
-- orchestration (cash / card-present) and stock depletion on paid live in code, not here.
--
-- Additive + idempotent. No existing behavior changes until the shop is used.

-- ── Tax categories (mirrors concession_tax_category) ─────────────────────────
CREATE TABLE IF NOT EXISTS shop_tax_category (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    rate_bps    int         NOT NULL DEFAULT 0,      -- basis points: 825 = 8.25%
    is_default  boolean     NOT NULL DEFAULT false,
    sort_order  int         NOT NULL DEFAULT 0,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_tax_category_tenant ON shop_tax_category (tenant_id, is_active);
-- At most one default per tenant, so line-tax resolution ("product's category, else the default")
-- is unambiguous.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_tax_category_default
    ON shop_tax_category (tenant_id) WHERE is_default = true;

-- Per-product tax category. NULL = the tenant's default. Added here (not in 0181) because tax only
-- matters at checkout, so the column and its target table land together.
ALTER TABLE shop_product ADD COLUMN IF NOT EXISTS tax_category_id uuid NULL;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_shop_product_tax_category') THEN
        ALTER TABLE shop_product
            ADD CONSTRAINT fk_shop_product_tax_category
            FOREIGN KEY (tax_category_id) REFERENCES shop_tax_category(id) ON DELETE SET NULL;
    END IF;
END $$;

-- ── Sale ─────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shop_sale (
    id                          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- Buyer: an app user when known, else a walk-in (name/email optional).
    buyer_user_id               uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    buyer_email                 text        NULL,
    buyer_name                  text        NULL,
    status                      text        NOT NULL DEFAULT 'pending'
                                            CHECK (status IN ('pending','paid','failed','refunded')),
    -- Money snapshot. subtotal = gross pre-discount pre-tax; total = subtotal - discount + tax + tip.
    subtotal_cents              int         NOT NULL DEFAULT 0,
    discount_cents              int         NOT NULL DEFAULT 0,
    tax_cents                   int         NOT NULL DEFAULT 0,
    tip_cents                   int         NOT NULL DEFAULT 0,
    total_cents                 int         NOT NULL DEFAULT 0,
    prices_include_tax          boolean     NOT NULL DEFAULT false,   -- snapshot of the tenant setting
    payment_method              text        NOT NULL DEFAULT 'stripe'
                                            CHECK (payment_method IN ('stripe','stripe_direct','cash','voucher')),
    stripe_payment_intent_id    text        NULL,
    -- Set for a direct charge: the connected account the sale was charged on, so a refund targets it.
    stripe_connected_account_id text        NULL,
    order_number                int         NULL,   -- per-tenant, per-local-day; assigned at paid
    sold_by_user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    receipt_token               uuid        NOT NULL DEFAULT gen_random_uuid(),
    refunded_at                 timestamptz NULL,
    refund_note                 text        NULL,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_sale_tenant ON shop_sale (tenant_id, status, created_at);
CREATE INDEX IF NOT EXISTS idx_shop_sale_pi ON shop_sale (stripe_payment_intent_id)
    WHERE stripe_payment_intent_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_sale_receipt ON shop_sale (receipt_token);

DROP TRIGGER IF EXISTS trg_shop_sale_updated_at ON shop_sale;
CREATE TRIGGER trg_shop_sale_updated_at BEFORE UPDATE ON shop_sale
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_sale_line (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    sale_id         uuid        NOT NULL REFERENCES shop_sale(id) ON DELETE CASCADE,
    -- RESTRICT: a sold variant can be deactivated but not deleted, so receipts stay resolvable.
    variant_id      uuid        NOT NULL REFERENCES shop_variant(id) ON DELETE RESTRICT,
    -- Set when a serialized unit is the thing sold (a specific bike). NULL for pool lines.
    item_id         uuid        NULL REFERENCES shop_item(id) ON DELETE SET NULL,
    quantity        int         NOT NULL CHECK (quantity > 0),
    -- Frozen catalog text so later edits never rewrite what a receipt said.
    name_snapshot   text        NOT NULL,
    variant_label   text        NULL,
    unit_price_cents int        NOT NULL,
    discount_cents  int         NOT NULL DEFAULT 0,
    tax_cents       int         NOT NULL DEFAULT 0,
    tax_rate_bps    int         NOT NULL DEFAULT 0,
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_sale_line_sale ON shop_sale_line (sale_id);
-- A serialized unit can be sold once. Partial unique guard so a bike can't land on two paid lines.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_sale_line_item ON shop_sale_line (item_id)
    WHERE item_id IS NOT NULL;

-- ── Ledger source kind ───────────────────────────────────────────────────────
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_source_kind_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
        CHECK (source_kind IS NULL OR source_kind IN (
            'pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras',
            'concession', 'tenant_billing_event', 'email_campaign', 'rental_deposit',
            'shop_sale'
        ));
END $$;

-- ── v_recent_sales: add the bike shop branch ─────────────────────────────────
-- Recreated from Script0145 with one appended UNION branch; every existing branch is unchanged.
CREATE OR REPLACE VIEW v_recent_sales AS
 SELECT 'event_ticket'::text AS kind, t.id, t.tenant_id, t.status, t.amount_cents,
        t.purchaser_user_id, t.purchaser_email, t.purchaser_name, t.stripe_payment_intent_id,
        tt.name AS item_name, t.created_at, t.redemption_token
   FROM event_ticket_purchase t
   LEFT JOIN event_ticket_tier tt ON tt.id = t.tier_id
 UNION ALL
 SELECT 'event_extra'::text, e.id, e.tenant_id, e.status, e.amount_cents,
        e.purchaser_user_id, e.purchaser_email, e.purchaser_name, e.stripe_payment_intent_id,
        ep.name, e.created_at, e.redemption_token
   FROM event_extra_purchase e
   LEFT JOIN event_extra_product ep ON ep.id = e.product_id
 UNION ALL
 SELECT 'season_pass'::text, s.id, s.tenant_id, s.status, s.amount_cents,
        s.purchaser_user_id, s.purchaser_email, s.purchaser_name, s.stripe_payment_intent_id,
        sp.name, s.created_at, s.redemption_token
   FROM season_pass_purchase s
   LEFT JOIN season_pass_product sp ON sp.id = s.product_id
 UNION ALL
 SELECT 'membership'::text, m.id, m.tenant_id, m.status, m.amount_cents,
        m.user_id, u.email,
        TRIM(BOTH FROM (COALESCE(u.first_name, '') || ' ') || COALESCE(u.last_name, '')),
        m.stripe_payment_intent_id, m.name_at_purchase, m.created_at, NULL::uuid
   FROM membership_purchase m
   LEFT JOIN users u ON u.id = m.user_id
 UNION ALL
 SELECT 'gift_card'::text, g.id, g.tenant_id, g.status, g.initial_amount_cents,
        g.buyer_user_id, g.buyer_email, g.buyer_name, g.stripe_payment_intent_id,
        'Gift Card $' || (g.initial_amount_cents / 100)::text, g.created_at, NULL::uuid
   FROM gift_card g
 UNION ALL
 SELECT 'rental'::text, r.id, r.tenant_id, r.status, r.amount_cents,
        r.purchaser_user_id, r.purchaser_email, r.purchaser_name, r.rental_pi_id,
        rp.name, r.created_at, r.redemption_token
   FROM rental_purchase r
   LEFT JOIN rental_product rp ON rp.id = r.product_id
 UNION ALL
 SELECT 'concession'::text, cs.id, cs.tenant_id, cs.status, cs.total_cents,
        cs.purchaser_user_id, cs.purchaser_email, cs.purchaser_name, cs.stripe_payment_intent_id,
        ('Food & Beverage (' || (SELECT COALESCE(sum(l.quantity), 0) FROM concession_sale_line l WHERE l.sale_id = cs.id)::text) || ' items)',
        cs.created_at, NULL::uuid
   FROM concession_sale cs
 UNION ALL
 SELECT 'shop_sale'::text, ss.id, ss.tenant_id, ss.status, ss.total_cents,
        ss.buyer_user_id, ss.buyer_email, ss.buyer_name, ss.stripe_payment_intent_id,
        ('Bike Shop (' || (SELECT COALESCE(sum(l.quantity), 0) FROM shop_sale_line l WHERE l.sale_id = ss.id)::text || ' items)'),
        ss.created_at, ss.receipt_token
   FROM shop_sale ss;
