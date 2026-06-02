-- Per-tenant SMS opt-out list. Carrier-side STOP filtering on US/Canada
-- toll-free numbers already blocks delivery once a customer texts STOP, but
-- we also need our own list so:
--   • Outbound paths can short-circuit before hitting Twilio (saves the API
--     call, the Twilio cost, and the inevitable failed StatusCallback).
--   • Audit trail survives a tenant changing their Twilio number — carrier
--     filters are scoped to a specific number, our list is scoped to the
--     tenant.
--   • Admin Inbox can show "opted out" beside the conversation so reps don't
--     try to reply.
--
-- One row per (tenant, phone). We track both opt-out and opt-in timestamps
-- so a customer who STOPs, then later STARTs, leaves a complete history
-- without us having to keep a separate event log. opted_out=true is the
-- live suppression signal; the timestamps are for audit/reporting.
--
-- No FK to a customer/user table — the texter might not have an account.
-- Phone is the stable identity here.

CREATE TABLE tenant_sms_opt_out (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,

    -- E.164. Same normalization the rest of the SMS pipeline uses
    -- (SmsSender.NormalizeE164) so lookups join cleanly.
    phone text NOT NULL,

    -- Live suppression flag. True = block outbound. Toggles back to false
    -- when the customer texts START/UNSTOP/YES.
    opted_out boolean NOT NULL,

    -- Most recent STOP/START. Both kept so audit can answer "when did they
    -- first opt out" and "when did they last opt back in" without scanning
    -- tenant_message.
    opted_out_at_utc timestamptz NULL,
    opted_in_at_utc timestamptz NULL,

    -- Which keyword drove the most recent transition ('STOP', 'STOPALL',
    -- 'UNSUBSCRIBE', 'CANCEL', 'END', 'QUIT', 'START', 'UNSTOP', 'YES').
    -- Useful when reconciling carrier compliance reports.
    last_keyword text NULL,

    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_tenant_sms_opt_out_tenant_phone
    ON tenant_sms_opt_out(tenant_id, phone);

-- Active opt-outs only — the suppression read path. Partial index keeps it
-- tight even on tenants with churny opt-in/out history.
CREATE INDEX ix_tenant_sms_opt_out_active
    ON tenant_sms_opt_out(tenant_id, phone)
    WHERE opted_out = true;
