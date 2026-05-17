-- Tenant membership program.
--
-- A tenant can require an active membership to make certain kinds of purchases
-- (passes, event tickets, season passes, add-ons). Each tenant defines a single
-- membership "product" — a name, a price, and a duration (one_time = lifetime,
-- yearly = 365-day window). Riders buy via a dedicated Stripe PI checkout.
--
-- Renewals are manual: the rider purchases again when their valid_to_utc passes.
-- Tiered memberships (Adult/Junior/Family) are deferred until tenants ask for it;
-- the schema keeps duration_kind/price frozen on each row so we can add a
-- product_id later without breaking historical reads.

ALTER TABLE tenant
    ADD COLUMN membership_enabled boolean NOT NULL DEFAULT false,
    ADD COLUMN membership_name text NOT NULL DEFAULT 'Track Membership',
    ADD COLUMN membership_price_cents int NOT NULL DEFAULT 0
        CHECK (membership_price_cents >= 0),
    ADD COLUMN membership_duration_kind text NOT NULL DEFAULT 'yearly'
        CHECK (membership_duration_kind IN ('one_time', 'yearly')),
    ADD COLUMN membership_required_for_pass boolean NOT NULL DEFAULT false,
    ADD COLUMN membership_required_for_event_ticket boolean NOT NULL DEFAULT false,
    ADD COLUMN membership_required_for_season_pass boolean NOT NULL DEFAULT false,
    ADD COLUMN membership_required_for_extras boolean NOT NULL DEFAULT false;

CREATE TABLE membership_purchase (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,

    -- Frozen at purchase time so historical reads remain correct after the
    -- tenant changes the configured name / price / duration.
    name_at_purchase text NOT NULL,
    price_cents int NOT NULL CHECK (price_cents >= 0),
    duration_kind text NOT NULL CHECK (duration_kind IN ('one_time', 'yearly')),

    valid_from_utc timestamptz NOT NULL DEFAULT now(),
    -- NULL for one-time / lifetime memberships.
    valid_to_utc timestamptz NULL,

    amount_cents int NOT NULL,            -- price + rider service charge portion
    service_charge_cents int NOT NULL DEFAULT 0,
    payment_method text NOT NULL DEFAULT 'stripe',

    stripe_payment_intent_id text NULL,
    status text NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'paid', 'failed', 'cancelled', 'refunded')),

    cancelled_reason text NULL,
    cancelled_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    cancelled_at timestamptz NULL,

    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_membership_purchase_user_tenant
    ON membership_purchase (user_id, tenant_id);
CREATE INDEX idx_membership_purchase_tenant_status
    ON membership_purchase (tenant_id, status);
CREATE INDEX idx_membership_purchase_valid_to
    ON membership_purchase (tenant_id, user_id, valid_to_utc) WHERE status = 'paid';
CREATE UNIQUE INDEX idx_membership_purchase_stripe_pi
    ON membership_purchase (stripe_payment_intent_id) WHERE stripe_payment_intent_id IS NOT NULL;

CREATE TRIGGER trg_membership_purchase_updated_at
    BEFORE UPDATE ON membership_purchase
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- tenant_ledger_entry source_kind: add 'membership' alongside existing kinds.
DO $$
DECLARE
    cn text;
BEGIN
    -- Drop any existing CHECK constraint on source_kind so we can replace it.
    FOR cn IN
        SELECT con.conname
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        WHERE cls.relname = 'tenant_ledger_entry'
          AND con.contype = 'c'
          AND pg_get_constraintdef(con.oid) ILIKE '%source_kind%'
    LOOP
        EXECUTE format('ALTER TABLE tenant_ledger_entry DROP CONSTRAINT %I', cn);
    END LOOP;
END $$;

ALTER TABLE tenant_ledger_entry
    ADD CONSTRAINT tenant_ledger_entry_source_kind_check
    CHECK (source_kind IS NULL OR source_kind IN ('pass','event_ticket','season_pass','rental','membership'));
