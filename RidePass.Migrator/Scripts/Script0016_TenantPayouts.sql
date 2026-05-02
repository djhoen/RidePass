-- Phase: tenant payouts ledger.
-- Tracks per-tenant fee schedules (versioned, tiered, optional monthly cap),
-- a transaction-level ledger of every sale/refund with snapshotted fee math,
-- and grouped payouts that mark batches of ledger entries as paid out.

-- Versioned fee schedule per tenant. Old schedules stay for historical lookups.
CREATE TABLE tenant_fee_schedule (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    effective_from_utc  timestamptz NOT NULL,
    effective_to_utc    timestamptz NULL,
    monthly_cap_cents   int         NULL CHECK (monthly_cap_cents IS NULL OR monthly_cap_cents > 0),
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_tenant_fee_schedule_tenant
    ON tenant_fee_schedule (tenant_id, effective_from_utc DESC);

-- Tiers within a schedule. min/max define monthly cumulative-volume range; max NULL = open-ended top tier.
CREATE TABLE tenant_fee_tier (
    id                uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    schedule_id       uuid        NOT NULL REFERENCES tenant_fee_schedule(id) ON DELETE CASCADE,
    min_volume_cents  bigint      NOT NULL CHECK (min_volume_cents >= 0),
    max_volume_cents  bigint      NULL CHECK (max_volume_cents IS NULL OR max_volume_cents > min_volume_cents),
    rate_bps          int         NOT NULL CHECK (rate_bps >= 0 AND rate_bps <= 10000),
    sort_order        int         NOT NULL
);

CREATE INDEX idx_tenant_fee_tier_schedule
    ON tenant_fee_tier (schedule_id, sort_order);

-- Append-only ledger. One row per chargeable event (sale, refund, dispute_loss, manual adjustment).
-- Refunds and adjustments use negative gross/net to net out a prior entry.
CREATE TABLE tenant_ledger_entry (
    id                                       uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                                uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    entry_kind                               text        NOT NULL CHECK (entry_kind IN ('sale', 'refund', 'dispute_loss', 'adjustment')),
    source_kind                              text        NULL CHECK (source_kind IS NULL OR source_kind IN ('day_pass', 'event_ticket')),
    source_id                                uuid        NULL,
    occurred_at_utc                          timestamptz NOT NULL,
    gross_cents                              int         NOT NULL,    -- can be negative for refunds/dispute_loss
    stripe_fee_cents                         int         NOT NULL,    -- positive on sale, negative on refund (Stripe returns the fee)
    ridepass_cut_cents                       int         NOT NULL,    -- positive on sale, negative on refund
    net_to_tenant_cents                      int         NOT NULL,    -- gross - stripe_fee - ridepass_cut
    applied_tier_id                          uuid        NULL REFERENCES tenant_fee_tier(id) ON DELETE SET NULL,
    cumulative_monthly_volume_at_sale_cents  bigint      NULL,        -- snapshot of tenant's monthly cumulative gross at the time
    stripe_payment_intent_id                 text        NULL,
    payout_id                                uuid        NULL,        -- FK added below after tenant_payout exists
    memo                                     text        NULL,
    created_at                               timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_tenant_ledger_entry_tenant_period
    ON tenant_ledger_entry (tenant_id, occurred_at_utc DESC);

-- Fast lookup of unpaid entries (the tenant's available balance).
CREATE INDEX idx_tenant_ledger_entry_unpaid
    ON tenant_ledger_entry (tenant_id)
    WHERE payout_id IS NULL;

CREATE INDEX idx_tenant_ledger_entry_payout
    ON tenant_ledger_entry (payout_id)
    WHERE payout_id IS NOT NULL;

-- A given source purchase can only have one 'sale' ledger entry (idempotency for webhook retries).
-- Refunds/adjustments tied to the same source are intentionally allowed (different entry_kind).
CREATE UNIQUE INDEX uk_tenant_ledger_entry_sale_per_source
    ON tenant_ledger_entry (tenant_id, source_kind, source_id)
    WHERE entry_kind = 'sale' AND source_kind IS NOT NULL AND source_id IS NOT NULL;

-- A payout groups a batch of ledger entries that were paid out together.
CREATE TABLE tenant_payout (
    id                            uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    status                        text        NOT NULL CHECK (status IN ('pending', 'processing', 'paid', 'failed', 'on_hold')),
    period_start_utc              timestamptz NOT NULL,
    period_end_utc                timestamptz NOT NULL,
    payout_date_utc               timestamptz NULL,
    total_gross_cents             int         NOT NULL DEFAULT 0,
    total_stripe_fee_cents        int         NOT NULL DEFAULT 0,
    total_ridepass_cut_cents      int         NOT NULL DEFAULT 0,
    total_adjustment_cents        int         NOT NULL DEFAULT 0,    -- refunds + dispute losses included in this payout
    net_paid_cents                int         NOT NULL DEFAULT 0,
    external_reference            text        NULL,                  -- Stripe transfer id, ACH trace, check #
    memo                          text        NULL,
    created_by_user_id            uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    approved_by_user_id           uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at                    timestamptz NOT NULL DEFAULT now(),
    updated_at                    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_payout_period CHECK (period_end_utc > period_start_utc)
);

ALTER TABLE tenant_ledger_entry
    ADD CONSTRAINT fk_tenant_ledger_entry_payout
    FOREIGN KEY (payout_id) REFERENCES tenant_payout(id) ON DELETE SET NULL;

CREATE INDEX idx_tenant_payout_tenant
    ON tenant_payout (tenant_id, period_start_utc DESC);

CREATE TRIGGER trg_tenant_payout_updated_at
    BEFORE UPDATE ON tenant_payout
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Seed a default flat-5%, no-cap schedule for every existing tenant so calculations work
-- immediately. Super admin can edit per-tenant later.
DO $$
DECLARE
    t_id uuid;
    sched_id uuid;
BEGIN
    FOR t_id IN SELECT id FROM tenant LOOP
        INSERT INTO tenant_fee_schedule (tenant_id, effective_from_utc, monthly_cap_cents)
            VALUES (t_id, now(), NULL)
            RETURNING id INTO sched_id;
        INSERT INTO tenant_fee_tier (schedule_id, min_volume_cents, max_volume_cents, rate_bps, sort_order)
            VALUES (sched_id, 0, NULL, 500, 1);
    END LOOP;
END $$;
