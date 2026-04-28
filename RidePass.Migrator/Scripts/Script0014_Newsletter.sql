-- Phase 12: newsletter subscribers + email campaigns.
--
-- newsletter_subscriber carries one row per (tenant, email). A soft-unsubscribe
-- flips unsubscribed_at so we can re-subscribe without losing history. The
-- unsubscribe_token is what appears in outbound campaign links.

CREATE TABLE newsletter_subscriber (
    id                uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id         uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    email             text        NOT NULL,
    name              text        NULL,
    source            text        NOT NULL DEFAULT 'signup'
                                  CHECK (source IN ('signup','account','import','admin')),
    unsubscribe_token uuid        NOT NULL DEFAULT uuid_generate_v4(),
    subscribed_at     timestamptz NOT NULL DEFAULT now(),
    unsubscribed_at   timestamptz NULL,
    CONSTRAINT uk_newsletter_subscriber UNIQUE (tenant_id, email)
);
CREATE UNIQUE INDEX uk_newsletter_unsubscribe_token ON newsletter_subscriber (unsubscribe_token);
CREATE INDEX idx_newsletter_subscriber_tenant_active
    ON newsletter_subscriber (tenant_id) WHERE unsubscribed_at IS NULL;

-- Email campaigns are owned by a tenant. body_html is the authoritative copy;
-- body_text is a best-effort plaintext fallback for clients that need it.
CREATE TABLE email_campaign (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    subject             text        NOT NULL,
    body_html           text        NOT NULL,
    body_text           text        NULL,
    status              text        NOT NULL DEFAULT 'draft'
                                    CHECK (status IN ('draft','scheduled','sending','sent','failed')),
    scheduled_for       timestamptz NULL,
    sent_at             timestamptz NULL,
    recipient_count     int         NOT NULL DEFAULT 0,
    created_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_email_campaign_tenant_status ON email_campaign (tenant_id, status);
CREATE TRIGGER trg_email_campaign_updated_at
    BEFORE UPDATE ON email_campaign
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- One send row per (campaign, recipient). The subscriber_id links to the
-- newsletter_subscriber at send-time; we keep the email denormalized so audit
-- trail survives even if the subscriber row is later deleted.
CREATE TABLE email_campaign_send (
    id             uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    campaign_id    uuid        NOT NULL REFERENCES email_campaign(id) ON DELETE CASCADE,
    subscriber_id  uuid        NULL REFERENCES newsletter_subscriber(id) ON DELETE SET NULL,
    email          text        NOT NULL,
    name           text        NULL,
    sent_at        timestamptz NULL,
    status         text        NOT NULL DEFAULT 'pending'
                               CHECK (status IN ('pending','sent','skipped','failed')),
    error          text        NULL,
    CONSTRAINT uk_email_campaign_send UNIQUE (campaign_id, email)
);
CREATE INDEX idx_email_campaign_send_campaign ON email_campaign_send (campaign_id);
