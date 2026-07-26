-- Marketing automations (drip campaigns): a trigger, a wait, and an email that goes out on its
-- own from then on. Design: docs/drip-campaigns.md.
--
-- A LINEAR SEQUENCE, NOT A GRAPH. Every comparable product (Klaviyo, ActiveCampaign, HubSpot)
-- ships a node-graph canvas, and that is the right shape when the hard part is expressing the
-- flow. Here the whole feature is "30 days after they buy a pass, tell them about the upgrade":
-- one trigger, one wait, one email. Steps are an ordered list, and if branching is ever needed a
-- linear sequence migrates into a graph cleanly. The reverse does not.
--
-- THE SEND LOG IS THE ENROLMENT RECORD. There is no "flow state" table tracking where each rider
-- is in the sequence. A step is due for a subject when the elapsed time says so and no send row
-- exists for that (step, subject) pair, which the unique index enforces. That is what makes the
-- sweep re-runnable: a tick that dies halfway through re-sends nothing on the next one, and a
-- rider who becomes ineligible after step 1 simply never matches step 2.
--
-- Additive and rerunnable.

CREATE TABLE IF NOT EXISTS marketing_automation (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name               text        NOT NULL,
    -- Phase 2 ships 'season_pass_purchased'. The column exists now so a second trigger is an
    -- INSERT rather than a schema change.
    trigger_kind       text        NOT NULL DEFAULT 'season_pass_purchased',
    -- Trigger-specific configuration, e.g. { "fromProductId": "..." }. jsonb because each
    -- trigger needs different fields and a column per trigger would be mostly nulls.
    trigger_config     jsonb       NOT NULL DEFAULT '{}'::jsonb,
    -- Exit conditions, ALL evaluated at send time rather than enrolment time. State changing
    -- during the wait is the entire point of the wait: a rider who upgraded on day 12 must not
    -- get the day-30 "an upgrade is available" email.
    stop_on_upgrade    boolean     NOT NULL DEFAULT true,
    stop_when_used_up  boolean     NOT NULL DEFAULT true,
    -- Tenant-local send window; both NULL means any hour. A step that comes due at 3am waits for
    -- the window rather than being skipped.
    send_window_start  time        NULL,
    send_window_end    time        NULL,
    -- False at rest, so authoring and arming are separate acts and saving never sends.
    is_active          boolean     NOT NULL DEFAULT false,
    -- Set when armed. The sweep ignores anything purchased earlier, which is what stops
    -- activation blasting two seasons of back catalogue on the first tick.
    enrol_from_utc     timestamptz NULL,
    created_by_user_id uuid        NULL REFERENCES users(id),
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_marketing_automation_trigger') THEN
        ALTER TABLE marketing_automation ADD CONSTRAINT ck_marketing_automation_trigger
            CHECK (trigger_kind IN ('season_pass_purchased'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_marketing_automation_window') THEN
        -- Either both bounds or neither. One-sided is ambiguous and the sweep would have to
        -- guess which half of the day it meant.
        ALTER TABLE marketing_automation ADD CONSTRAINT ck_marketing_automation_window
            CHECK ((send_window_start IS NULL) = (send_window_end IS NULL));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_marketing_automation_tenant
    ON marketing_automation (tenant_id);
-- The sweep's driving read: every armed automation across all tenants.
CREATE INDEX IF NOT EXISTS ix_marketing_automation_active
    ON marketing_automation (is_active) WHERE is_active;

CREATE TABLE IF NOT EXISTS marketing_automation_step (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    automation_id  uuid        NOT NULL REFERENCES marketing_automation(id) ON DELETE CASCADE,
    step_order     int         NOT NULL,
    -- Days after the TRIGGER, not after the previous step. Relative offsets are the nicer way to
    -- author and the worse way to store: deleting step 2 silently moves step 3 earlier. Store
    -- absolute; the editor can present it however reads best.
    delay_days     int         NOT NULL CHECK (delay_days >= 0),
    subject        text        NOT NULL,
    body_html      text        NOT NULL,
    body_text      text        NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_automation_step_order
    ON marketing_automation_step (automation_id, step_order);

-- One send per (step, subject), ever. This IS the dedupe and the enrolment record.
CREATE TABLE IF NOT EXISTS marketing_automation_send (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    automation_id  uuid        NOT NULL REFERENCES marketing_automation(id) ON DELETE CASCADE,
    step_id        uuid        NOT NULL REFERENCES marketing_automation_step(id) ON DELETE CASCADE,
    -- What the automation is about. For 'season_pass_purchased' this is the purchase id.
    subject_kind   text        NOT NULL,
    subject_id     uuid        NOT NULL,
    email          text        NOT NULL,
    status         text        NOT NULL,
    -- Written for skips AND failures. A failure that records nothing is retried every tick
    -- forever, which is how a broken template becomes a mail-bomb.
    skip_reason    text        NULL,
    sent_at        timestamptz NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_automation_send_status') THEN
        ALTER TABLE marketing_automation_send ADD CONSTRAINT ck_automation_send_status
            CHECK (status IN ('sent', 'failed', 'skipped'));
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uk_automation_send_once
    ON marketing_automation_send (step_id, subject_kind, subject_id);

-- The sweep's "what have I already handled" read, and the reporting rollup.
CREATE INDEX IF NOT EXISTS ix_automation_send_lookup
    ON marketing_automation_send (automation_id, subject_kind, subject_id);
CREATE INDEX IF NOT EXISTS ix_automation_send_tenant_month
    ON marketing_automation_send (tenant_id, sent_at) WHERE status = 'sent';
