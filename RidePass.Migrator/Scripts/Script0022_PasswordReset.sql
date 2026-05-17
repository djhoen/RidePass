-- Self-serve password reset tokens. We store SHA-256 of the token (not the token itself)
-- so a leaked DB snapshot can't be used to assume identities.

CREATE TABLE password_reset_token (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash      text        NOT NULL,                       -- hex-encoded SHA-256 of the token
    expires_at_utc  timestamptz NOT NULL,
    used_at_utc     timestamptz NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX idx_password_reset_token_hash ON password_reset_token (token_hash);
CREATE INDEX idx_password_reset_user ON password_reset_token (user_id, created_at DESC);
