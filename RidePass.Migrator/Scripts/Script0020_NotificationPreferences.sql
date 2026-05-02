-- Per-user notification preferences. Absence of a row for (user_id, kind) is treated as
-- email_enabled=true so existing super admins get all emails until they opt out.

CREATE TABLE notification_preference (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    kind            text        NOT NULL,
    email_enabled   boolean     NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uk_notification_preference_user_kind UNIQUE (user_id, kind)
);

CREATE INDEX idx_notification_preference_user
    ON notification_preference (user_id);

CREATE TRIGGER trg_notification_preference_updated_at
    BEFORE UPDATE ON notification_preference
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
