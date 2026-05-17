-- Track Feedback — free-form messages from riders / spectators / anyone visiting
-- the public site. Separate from the survey feature (Script0076 will add that)
-- because feedback is unsolicited and ad-hoc whereas surveys are structured +
-- distributed by the tenant.
--
-- Guest-friendly: user_id nullable (the rider may not be logged in or may not
-- even have an account). Email + name captured directly on the row so admins
-- can reply without joining to users.

CREATE TABLE track_feedback (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id         uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    name            text        NOT NULL,
    email           text        NOT NULL,
    -- Optional 1-5 star rating. Nullable so admins can collect plain comments
    -- without forcing a numeric judgement.
    rating          int         NULL CHECK (rating IS NULL OR rating BETWEEN 1 AND 5),
    -- Free-form body. Reasonable cap to discourage abuse.
    body            text        NOT NULL CHECK (length(body) BETWEEN 1 AND 4000),
    -- Status workflow: 'new' (just submitted) → 'addressed' (admin acted on it)
    --                  or 'dismissed' (spam / nothing to do).
    status          text        NOT NULL DEFAULT 'new'
                                CHECK (status IN ('new','addressed','dismissed')),
    admin_notes     text        NULL,
    -- Audit trail when an admin transitions status.
    actioned_by_user_id  uuid   NULL REFERENCES users(id) ON DELETE SET NULL,
    actioned_at_utc      timestamptz NULL,
    ip_address      text        NULL,
    user_agent      text        NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_track_feedback_tenant_status_created
    ON track_feedback (tenant_id, status, created_at DESC);
CREATE INDEX idx_track_feedback_email
    ON track_feedback (tenant_id, lower(email));

CREATE TRIGGER trg_track_feedback_updated_at
    BEFORE UPDATE ON track_feedback
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
