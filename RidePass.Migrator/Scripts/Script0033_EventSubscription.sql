-- Anyone — logged-in rider or anonymous email — can subscribe to be notified when a
-- tenant publishes a new event. Channels are independent: notify_email and notify_sms
-- can each be on or off. Phone is required only when notify_sms = true.
--
-- The unsubscribe_token grants one-click unsubscribe via emails (CAN-SPAM compliance).

CREATE TABLE event_subscription (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    email               text        NOT NULL,
    phone               text        NULL,
    notify_email        boolean     NOT NULL DEFAULT true,
    notify_sms          boolean     NOT NULL DEFAULT false,
    unsubscribe_token   uuid        NOT NULL DEFAULT uuid_generate_v4(),
    unsubscribed_at     timestamptz NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uk_event_subscription_tenant_email
    ON event_subscription (tenant_id, LOWER(email));
CREATE UNIQUE INDEX uk_event_subscription_token
    ON event_subscription (unsubscribe_token);
CREATE INDEX idx_event_subscription_active
    ON event_subscription (tenant_id)
    WHERE unsubscribed_at IS NULL AND (notify_email = true OR notify_sms = true);
