-- In-app notifications. Each notification targets exactly one user — broadcasts (e.g., "tell all
-- super admins") fan out to one row per recipient at emit time so per-user read state is independent.

CREATE TABLE notification (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    recipient_user_id   uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    tenant_id           uuid        NULL REFERENCES tenant(id) ON DELETE CASCADE,
    kind                text        NOT NULL,
    title               text        NOT NULL,
    body                text        NOT NULL,
    link_url            text        NULL,
    is_read             boolean     NOT NULL DEFAULT false,
    created_at          timestamptz NOT NULL DEFAULT now(),
    read_at             timestamptz NULL
);

CREATE INDEX idx_notification_user
    ON notification (recipient_user_id, created_at DESC);

CREATE INDEX idx_notification_user_unread
    ON notification (recipient_user_id, created_at DESC)
    WHERE is_read = false;
