-- Gift cards: rider buys a denomination as a gift, recipient gets an email with a
-- code, code is then applied as store credit to any purchase until the balance is
-- depleted. Stored-balance model so partial use across multiple visits works.
--
-- Treat gift cards as a payment method, not a discount: at checkout, vouchers and
-- coupons reduce the price first, then the gift card pays whatever cash remains,
-- then Stripe covers anything beyond the balance. This keeps stacking math trivial.

CREATE TABLE gift_card (
    id                          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    code                        text        NOT NULL,
    initial_amount_cents        int         NOT NULL CHECK (initial_amount_cents > 0),
    balance_cents               int         NOT NULL CHECK (balance_cents >= 0),
    -- Buyer (purchaser) — required for refund routing + receipt. Guest checkout is
    -- not currently supported; if we ever do allow it, drop the FK or use SET NULL.
    buyer_user_id               uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    buyer_name                  text        NOT NULL,
    buyer_email                 text        NOT NULL,
    -- Recipient — the person who actually gets the email + uses the card.
    recipient_name              text        NOT NULL,
    recipient_email             text        NOT NULL,
    personal_note               text        NULL,
    -- delivery_status drives the scheduled-delivery worker. 'pending' means we owe
    -- the recipient an email; 'delivered' means we sent it; 'failed' means the
    -- background job hit an error and gave up after retries.
    delivery_status             text        NOT NULL DEFAULT 'pending'
                                            CHECK (delivery_status IN ('pending','delivered','failed')),
    scheduled_delivery_at_utc   timestamptz NULL,
    delivered_at_utc            timestamptz NULL,
    -- 'active' = balance > 0 and not refunded; 'depleted' when balance reaches 0;
    -- 'refunded' when buyer-side refund clears the balance back to issuer.
    status                      text        NOT NULL DEFAULT 'active'
                                            CHECK (status IN ('active','depleted','refunded')),
    stripe_payment_intent_id    text        NULL,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);

-- Codes are unique per tenant. Case-insensitive at the gate so a recipient typing
-- "gift-x9p2" and "GIFT-X9P2" both work.
CREATE UNIQUE INDEX uk_gift_card_tenant_code ON gift_card (tenant_id, lower(code));
-- Worker picks up pending cards efficiently. Filtered to pending so the index
-- stays tiny over time.
CREATE INDEX idx_gift_card_pending_delivery
    ON gift_card (scheduled_delivery_at_utc NULLS FIRST)
    WHERE delivery_status = 'pending';

CREATE TRIGGER trg_gift_card_updated_at
    BEFORE UPDATE ON gift_card
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Each application of a gift card to a purchase. Used for audit + reporting + refund
-- logic ("balance is fully untouched" means zero rows here).
CREATE TABLE gift_card_redemption (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    gift_card_id    uuid        NOT NULL REFERENCES gift_card(id) ON DELETE CASCADE,
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id         uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    source_kind     text        NOT NULL CHECK (source_kind IN ('day_pass','event_ticket','season_pass')),
    source_id       uuid        NOT NULL,
    amount_cents    int         NOT NULL CHECK (amount_cents > 0),
    redeemed_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_gift_card_redemption_card ON gift_card_redemption (gift_card_id, redeemed_at DESC);
-- One application per source row — keeps the math idempotent if a webhook retries.
CREATE UNIQUE INDEX uk_gift_card_redemption_source ON gift_card_redemption (source_kind, source_id);

-- Tenant config — defaults match my $10–$500 recommendation; tenant admins can edit.
ALTER TABLE tenant
    ADD COLUMN gift_cards_enabled    boolean NOT NULL DEFAULT true,
    ADD COLUMN gift_card_min_cents   int     NOT NULL DEFAULT 1000   CHECK (gift_card_min_cents > 0),
    ADD COLUMN gift_card_max_cents   int     NOT NULL DEFAULT 50000  CHECK (gift_card_max_cents >= gift_card_min_cents);
