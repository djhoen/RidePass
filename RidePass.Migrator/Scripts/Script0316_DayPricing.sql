-- Per-day prices (Highland go-live item 13): "Walk up prices are different than buying in advance
-- online. Flex prices aren't refundable or cancelable. Certain days are more expensive than others."
--
-- * walk_up_price_cents: what Gate Sale charges for this ticket type; NULL = the online price.
-- * non_refundable: riders can't cancel it, and only an Admin can cancel or refund it.
-- * day_price_rule: venue-wide peak days. A day pass (gate_fee) on a matching day costs more. A
--   rule for a specific date beats the weekday rules; among rules of one kind the biggest wins.
--   Rules never stack, and never touch race entries or free tickets.

ALTER TABLE event_ticket_tier
    ADD COLUMN IF NOT EXISTS walk_up_price_cents int NULL
        CHECK (walk_up_price_cents IS NULL OR walk_up_price_cents BETWEEN 0 AND 1000000),
    ADD COLUMN IF NOT EXISTS non_refundable boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS day_price_rule (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    label               text        NOT NULL CHECK (length(btrim(label)) > 0),
    -- Exactly one of: a weekday (0 = Sunday .. 6 = Saturday) or a calendar date (venue time).
    weekday             smallint    NULL CHECK (weekday IS NULL OR weekday BETWEEN 0 AND 6),
    on_date             date        NULL,
    -- NULL = every event type.
    event_type_id       uuid        NULL REFERENCES tenant_event_type(id) ON DELETE CASCADE,
    -- 'amount' = cents added; 'percent' = basis points added (2500 = +25%).
    adjust_kind         text        NOT NULL CHECK (adjust_kind IN ('amount', 'percent')),
    adjust_value        int         NOT NULL CHECK (adjust_value > 0 AND adjust_value <= 1000000),
    is_active           boolean     NOT NULL DEFAULT true,
    created_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_day_price_rule_day CHECK ((weekday IS NULL) <> (on_date IS NULL))
);

CREATE INDEX IF NOT EXISTS idx_day_price_rule_tenant ON day_price_rule (tenant_id) WHERE is_active;
