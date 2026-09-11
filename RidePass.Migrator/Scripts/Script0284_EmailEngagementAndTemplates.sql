-- Phase B of docs/email-builder-parity.md.
--
-- email_engagement: one row per SendGrid open/click event, tied back to the send row it came
-- from (campaign send or automation send) through the unique args stamped on the outbound
-- message. Deduped on SendGrid's own event id because their webhook retries deliveries.
-- Opens are inflated by Apple Mail Privacy Protection (it pre-fetches the pixel), so the UI
-- leads with clicks; both are stored.
--
-- email_template: a saved body + subject + preview text a track can start a campaign or an
-- automation email from.
-- Additive and rerunnable.

CREATE TABLE IF NOT EXISTS email_engagement (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    source_kind     text        NOT NULL,
    source_send_id  uuid        NOT NULL,
    event           text        NOT NULL,
    url             text        NULL,
    sg_event_id     text        NULL,
    occurred_at     timestamptz NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_email_engagement_source') THEN
        ALTER TABLE email_engagement ADD CONSTRAINT ck_email_engagement_source
            CHECK (source_kind IN ('campaign', 'automation'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_email_engagement_event') THEN
        ALTER TABLE email_engagement ADD CONSTRAINT ck_email_engagement_event
            CHECK (event IN ('open', 'click'));
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uk_email_engagement_sg_event
    ON email_engagement (sg_event_id) WHERE sg_event_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_email_engagement_send
    ON email_engagement (source_kind, source_send_id, event);
CREATE INDEX IF NOT EXISTS ix_email_engagement_tenant
    ON email_engagement (tenant_id, occurred_at);

CREATE TABLE IF NOT EXISTS email_template (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name               text        NOT NULL,
    subject            text        NULL,
    preview_text       text        NULL,
    body_html          text        NOT NULL,
    created_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_email_template_tenant ON email_template (tenant_id, name);
