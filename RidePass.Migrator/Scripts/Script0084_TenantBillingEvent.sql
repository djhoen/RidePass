-- Billing ledger: one row per Twilio Message that we'll charge the tenant for.
-- Populated by the StatusCallback webhook on delivered messages once Twilio
-- finalises the carrier price. Drained asynchronously by the Stripe Meters
-- push handler in TaskRunner — that flow stamps stripe_meter_event_id +
-- pushed_to_stripe_at_utc on success.
--
-- Idempotency: (kind, source_id) is unique because Twilio sometimes retries
-- StatusCallback delivery. ON CONFLICT DO NOTHING on insert lets the same
-- callback arrive twice without double-billing.
--
-- Money columns:
--   twilio_cost_micros — what Twilio charged us, in millionths of one dollar.
--                       Twilio returns "Price" with up to 5 decimals, so we
--                       scale up by 10^6 to keep everything integer.
--   billed_cents       — what we charge the tenant. Computed once at insert
--                       time (current rule: ceil(cost_micros * 2 / 10_000)
--                       — 2x markup, rounded up to the next whole cent).

CREATE TABLE tenant_billing_event (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,

    -- 'sms' today; 'mms', 'voice', etc. later. Used to dimension Stripe Meters.
    kind text NOT NULL,

    -- Logical source the event was derived from. 'sms_send' for outbound SMS
    -- whose only persisted artifact is the Twilio SID; later 'tenant_message'
    -- once the inbound conversation feature lands and we have a row per send.
    source_table text NOT NULL,

    -- The provider-side identifier — for Twilio, the Message SID (SM...).
    -- Globally unique within Twilio, so the (kind, source_id) constraint
    -- prevents double-billing without needing tenant scope in the key.
    source_id text NOT NULL,

    twilio_cost_micros bigint NOT NULL,
    billed_cents int NOT NULL,

    stripe_meter_event_id text NULL,
    pushed_to_stripe_at_utc timestamptz NULL,

    created_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Idempotency for retried StatusCallbacks.
CREATE UNIQUE INDEX ux_tenant_billing_event_source
    ON tenant_billing_event(kind, source_id);

-- Tenant-scoped reads for the future Billing & Usage page.
CREATE INDEX ix_tenant_billing_event_tenant_created
    ON tenant_billing_event(tenant_id, created_at_utc DESC);

-- Worklist for the Stripe push handler. Partial index keeps it tiny — once
-- an event is pushed, the row drops out.
CREATE INDEX ix_tenant_billing_event_pending_push
    ON tenant_billing_event(created_at_utc)
    WHERE pushed_to_stripe_at_utc IS NULL;
