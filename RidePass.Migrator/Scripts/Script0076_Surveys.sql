-- Surveys: tenant-built questionnaires distributed via email or shared via a
-- public link. Three question kinds:
--   single_choice    — pick one (poll)
--   multiple_choice  — pick any
--   free_form        — text response
--
-- Responses are anonymous-friendly (user_id + email both nullable). Each
-- emailed invite gets its own token so admins can see who opened / completed.
-- A survey-level public_token lets admins share one link broadly without
-- per-recipient tracking.

CREATE TABLE survey (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name            text        NOT NULL,                              -- admin label
    title           text        NOT NULL,                              -- shown to respondents
    description     text        NULL,
    status          text        NOT NULL DEFAULT 'draft'
                                CHECK (status IN ('draft','published','closed')),
    closes_at_utc   timestamptz NULL,
    -- When true, respondents must enter an email + name (still anonymous to other
    -- respondents, but admins can see who answered).
    require_email   boolean     NOT NULL DEFAULT false,
    -- Stable token used in the public share link (/Survey/Public/{token}).
    public_token    uuid        NOT NULL DEFAULT uuid_generate_v4(),
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uk_survey_public_token ON survey (public_token);
CREATE INDEX idx_survey_tenant_status ON survey (tenant_id, status, created_at DESC);
CREATE TRIGGER trg_survey_updated_at
    BEFORE UPDATE ON survey
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE survey_question (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    survey_id       uuid        NOT NULL REFERENCES survey(id) ON DELETE CASCADE,
    kind            text        NOT NULL
                                CHECK (kind IN ('single_choice','multiple_choice','free_form')),
    prompt          text        NOT NULL,
    sort_order      int         NOT NULL DEFAULT 100,
    required        boolean     NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_survey_question_survey ON survey_question (survey_id, sort_order, id);
CREATE TRIGGER trg_survey_question_updated_at
    BEFORE UPDATE ON survey_question
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE survey_question_choice (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    question_id     uuid        NOT NULL REFERENCES survey_question(id) ON DELETE CASCADE,
    label           text        NOT NULL,
    sort_order      int         NOT NULL DEFAULT 100,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_survey_choice_question ON survey_question_choice (question_id, sort_order, id);

CREATE TABLE survey_invite (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    survey_id           uuid        NOT NULL REFERENCES survey(id) ON DELETE CASCADE,
    email               text        NOT NULL,
    token               uuid        NOT NULL DEFAULT uuid_generate_v4(),
    sent_at_utc         timestamptz NULL,
    opened_at_utc       timestamptz NULL,
    completed_at_utc    timestamptz NULL,
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uk_survey_invite_token ON survey_invite (token);
CREATE INDEX idx_survey_invite_survey ON survey_invite (survey_id);
CREATE UNIQUE INDEX uk_survey_invite_email_per_survey
    ON survey_invite (survey_id, lower(email));

CREATE TABLE survey_response (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    survey_id           uuid        NOT NULL REFERENCES survey(id) ON DELETE CASCADE,
    user_id             uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    invite_id           uuid        NULL REFERENCES survey_invite(id) ON DELETE SET NULL,
    respondent_email    text        NULL,
    respondent_name     text        NULL,
    submitted_at_utc    timestamptz NOT NULL DEFAULT now(),
    ip_address          text        NULL
);

CREATE INDEX idx_survey_response_survey ON survey_response (survey_id, submitted_at_utc DESC);
CREATE INDEX idx_survey_response_invite ON survey_response (invite_id) WHERE invite_id IS NOT NULL;

CREATE TABLE survey_answer (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    response_id     uuid        NOT NULL REFERENCES survey_response(id) ON DELETE CASCADE,
    question_id     uuid        NOT NULL REFERENCES survey_question(id) ON DELETE CASCADE,
    -- For choice questions: the picked choice. NULL for free_form.
    choice_id       uuid        NULL REFERENCES survey_question_choice(id) ON DELETE CASCADE,
    -- For free_form: the typed text. NULL for choice questions.
    free_text       text        NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    -- Exactly one of (choice_id, free_text) must be present.
    CONSTRAINT chk_answer_kind CHECK (
        (choice_id IS NOT NULL AND free_text IS NULL)
        OR (choice_id IS NULL AND free_text IS NOT NULL)
    )
);

CREATE INDEX idx_survey_answer_response ON survey_answer (response_id);
CREATE INDEX idx_survey_answer_question ON survey_answer (question_id);
