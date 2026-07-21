-- Bike shop, Phase 3: rentals re-homed onto the unified catalog.
--
-- This absorbs the standalone rental system's DESIGN (pool-vs-serialized capacity, half-open
-- time-window reservations from Script0177, security-deposit pre-auth with manual damage capture
-- from Script0048/0179, lesson wiring) onto the shop_* catalog, per docs/bike-shop.md. The old
-- rental_* tables are left in place untouched (zero prod data; they retire in a later cleanup
-- once nothing references them) — nothing here reads or writes them.
--
-- Model recap:
--   * A rental is a booking of catalog variants for a window [starts_at, ends_at) (half-open, so
--     back-to-back bookings that touch at the boundary do NOT collide).
--   * Booking reserves CAPACITY by window overlap; physical stock only moves at checkout/return
--     (rental_out / rental_return movements). For pool variants the fleet total for availability
--     is stock_on_hand + what's currently out; serialized units are picked at booking.
--   * The rental fee is a normal auto-capture PaymentIntent (or cash). The deposit is a SEPARATE
--     manual-capture hold — never charged unless damage is captured on return.
--
-- Additive + idempotent throughout.

CREATE TABLE IF NOT EXISTS shop_rental (
    id                          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    renter_user_id              uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    renter_name                 text        NULL,   -- walk-ins allowed; name strongly encouraged at the counter
    renter_email                text        NULL,
    renter_phone                text        NULL,
    waiver_signature_id         uuid        NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL,
    -- Half-open [starts_at, ends_at): the single source of truth for every availability check.
    starts_at                   timestamptz NOT NULL,
    ends_at                     timestamptz NOT NULL,
    -- pending   = created, awaiting payment confirm
    -- paid      = fee settled; capacity reserved for the window
    -- out       = gear handed over (checked_out_at set; stock moved)
    -- returned  = gear back, deposit released
    -- damaged   = returned with a non-zero deposit capture
    -- cancelled / failed = not reserving capacity
    status                      text        NOT NULL DEFAULT 'pending'
                                            CHECK (status IN ('pending','paid','out','returned','damaged','cancelled','failed')),
    amount_cents                int         NOT NULL DEFAULT 0,
    tax_cents                   int         NOT NULL DEFAULT 0,
    total_cents                 int         NOT NULL DEFAULT 0,
    deposit_cents               int         NOT NULL DEFAULT 0,
    deposit_pi_id               text        NULL,   -- manual-capture hold; captured only for damage
    deposit_captured_cents      int         NOT NULL DEFAULT 0,
    payment_method              text        NOT NULL DEFAULT 'stripe'
                                            CHECK (payment_method IN ('stripe','stripe_direct','cash','voucher')),
    stripe_payment_intent_id    text        NULL,   -- the rental FEE intent
    stripe_connected_account_id text        NULL,
    order_number                int         NULL,
    sold_by_user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    receipt_token               uuid        NOT NULL DEFAULT gen_random_uuid(),
    checked_out_at              timestamptz NULL,
    returned_at                 timestamptz NULL,
    condition_notes             text        NULL,
    -- Set when booked as part of a lesson (absorbs rental_purchase.event_id from Script0177).
    event_id                    uuid        NULL REFERENCES event(id) ON DELETE SET NULL,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_shop_rental_window CHECK (ends_at > starts_at)
);
-- Availability hot path: active rentals overlapping a window, per tenant.
CREATE INDEX IF NOT EXISTS idx_shop_rental_tenant_window
    ON shop_rental (tenant_id, status, starts_at, ends_at);
CREATE INDEX IF NOT EXISTS idx_shop_rental_fee_pi
    ON shop_rental (stripe_payment_intent_id) WHERE stripe_payment_intent_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_shop_rental_deposit_pi
    ON shop_rental (deposit_pi_id) WHERE deposit_pi_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_shop_rental_event
    ON shop_rental (event_id) WHERE event_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_rental_receipt ON shop_rental (receipt_token);

DROP TRIGGER IF EXISTS trg_shop_rental_updated_at ON shop_rental;
CREATE TRIGGER trg_shop_rental_updated_at BEFORE UPDATE ON shop_rental
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_rental_line (
    id                     uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    rental_id              uuid        NOT NULL REFERENCES shop_rental(id) ON DELETE CASCADE,
    -- RESTRICT: a variant with rental history can be deactivated but not deleted.
    variant_id             uuid        NOT NULL REFERENCES shop_variant(id) ON DELETE RESTRICT,
    -- The specific unit for a serialized line, assigned at booking. NO unique index (unlike a sale
    -- line's) — the same bike is rented many times over its life; collisions are prevented by the
    -- window-overlap availability check, not by uniqueness.
    item_id                uuid        NULL REFERENCES shop_item(id) ON DELETE SET NULL,
    quantity               int         NOT NULL CHECK (quantity > 0),
    name_snapshot          text        NOT NULL,
    variant_label          text        NULL,
    -- Rate frozen at booking so later price edits never rewrite what was agreed.
    daily_rate_cents_frozen int        NOT NULL,
    deposit_cents_frozen   int         NOT NULL DEFAULT 0,
    line_amount_cents      int         NOT NULL DEFAULT 0,
    created_at             timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_rental_line_rental  ON shop_rental_line (rental_id);
CREATE INDEX IF NOT EXISTS idx_shop_rental_line_variant ON shop_rental_line (variant_id);
CREATE INDEX IF NOT EXISTS idx_shop_rental_line_item    ON shop_rental_line (item_id) WHERE item_id IS NOT NULL;

-- Which shop variants (bikes) may be booked as part of which lesson, with an optional per-lesson
-- price override. The shop_* successor to event_rental_eligibility; lesson checkout re-points here
-- when the lessons flow folds in.
CREATE TABLE IF NOT EXISTS shop_lesson_rentable (
    event_id             uuid NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    variant_id           uuid NOT NULL REFERENCES shop_variant(id) ON DELETE CASCADE,
    price_cents_override int  NULL CHECK (price_cents_override IS NULL OR price_cents_override >= 0),
    PRIMARY KEY (event_id, variant_id)
);
CREATE INDEX IF NOT EXISTS idx_shop_lesson_rentable_variant ON shop_lesson_rentable (variant_id);

-- Ledger kinds: the rental fee, and damage kept out of a deposit (distinct kinds so the two
-- entries on one rental never collide on the (source_kind, source_id) sale-entry unique index).
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_source_kind_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
        CHECK (source_kind IS NULL OR source_kind IN (
            'pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras',
            'concession', 'tenant_billing_event', 'email_campaign', 'rental_deposit',
            'shop_sale', 'shop_rental', 'shop_rental_deposit'
        ));
END $$;

-- v_recent_sales: add the shop rental branch (recreated from Script0182's definition with one
-- appended UNION; every existing branch unchanged).
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
   FROM shop_sale ss
 UNION ALL
 SELECT 'shop_rental'::text, sr.id, sr.tenant_id, sr.status, sr.total_cents,
        sr.renter_user_id, sr.renter_email, sr.renter_name, sr.stripe_payment_intent_id,
        ('Bike Shop Rental (' || (SELECT COALESCE(sum(l.quantity), 0) FROM shop_rental_line l WHERE l.rental_id = sr.id)::text || ' items)'),
        sr.created_at, sr.receipt_token
   FROM shop_rental sr;
