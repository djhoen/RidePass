-- Email verification for public rider signups. New rider accounts created while
-- SMTP is configured start unverified and must click a link before they can sign in.
-- Existing accounts (and any created before this feature) are backfilled as verified
-- so nobody gets locked out, and the login gate only applies to the 'rider' role, so
-- admin/staff accounts provisioned by trusted admins are unaffected.
--
-- Written idempotently: the column was hand-applied to the dev DB before this script
-- was journaled, so guard every statement to re-run safely. The grandfather backfill
-- only runs the first time the column is created, so it can't re-verify accounts that
-- legitimately went unverified after the feature shipped.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'users' AND column_name = 'email_verified'
    ) THEN
        ALTER TABLE users ADD COLUMN email_verified boolean NOT NULL DEFAULT false;
        UPDATE users SET email_verified = true;        -- grandfather every existing account
    END IF;
END $$;

ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verification_token_hash text NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verification_expires_at timestamptz NULL;

-- Token lookups at verify time go through this hash.
CREATE INDEX IF NOT EXISTS idx_users_email_verification_token
    ON users (email_verification_token_hash)
    WHERE email_verification_token_hash IS NOT NULL;
