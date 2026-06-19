-- Sanitize a freshly-restored STAGING database (prod clone) before anyone uses it.
--
-- Scrubs PII (emails, phones, emergency/parent contacts) and neutralizes cloned
-- external credentials (Stripe Connect / Terminal ids, Twilio creds) so staging
-- can't be tied back to real customers or real prod integrations.
--
-- Design notes:
--   * Idempotent and schema-drift-proof: it scrubs by COLUMN NAME across every
--     table in the public schema, so new tables/columns are covered automatically
--     and missing ones are simply not matched (no errors).
--   * Super-admin user rows are intentionally LEFT INTACT so you can still log in
--     to staging with your real super-admin credentials. Everyone else is scrubbed.
--   * Passwords are not touched. Non-super-admin logins won't work (you don't know
--     the hashes); use super-admin + impersonation, or reset via the (silent on
--     staging) password-reset flow.
--
-- Run only against staging:  psql "$STAGE_DB_URL" -v ON_ERROR_STOP=1 -f sanitize-stage.sql

-- Hard safety gate: refuse to run unless the database name looks like staging.
DO $$
BEGIN
    IF current_database() NOT ILIKE '%stage%' THEN
        RAISE EXCEPTION 'Refusing to sanitize: database "%" does not look like a staging DB', current_database();
    END IF;
END $$;

-- 1) users: scrub PII for everyone EXCEPT super admins (keep their login working).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'role') THEN
        UPDATE public.users
           SET email = 'user-' || substr(md5(email), 1, 12) || '@stage.invalid'
         WHERE role <> 'super_admin' AND email IS NOT NULL;

        UPDATE public.users SET phone = NULL
         WHERE role <> 'super_admin'
           AND EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'phone');

        UPDATE public.users
           SET emergency_contact_name = NULL, emergency_contact_phone = NULL
         WHERE role <> 'super_admin'
           AND EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'public' AND table_name = 'users'
                         AND column_name = 'emergency_contact_phone');
        RAISE NOTICE 'scrubbed users PII (super_admin rows preserved)';
    END IF;
END $$;

-- 2) Email-like columns on every OTHER table: replace with a deterministic,
--    unique, non-deliverable address derived from the original (preserves shape +
--    uniqueness for testing, breaks the link to the real person).
DO $$
DECLARE r record;
BEGIN
    FOR r IN
        -- Join to information_schema.tables and restrict to BASE TABLE so we never try
        -- to UPDATE a view (e.g. v_recent_sales, a UNION view exposing purchaser_email).
        SELECT c.table_name, c.column_name
          FROM information_schema.columns c
          JOIN information_schema.tables t
            ON t.table_schema = c.table_schema AND t.table_name = c.table_name
         WHERE c.table_schema = 'public'
           AND t.table_type = 'BASE TABLE'
           AND c.table_name <> 'users'
           AND c.column_name IN ('email', 'purchaser_email', 'buyer_email', 'recipient_email',
                               'contact_email', 'guest_email', 'customer_email')
    LOOP
        EXECUTE format(
            'UPDATE public.%I SET %I = ''redacted-'' || substr(md5(%I), 1, 12) || ''@stage.invalid'' WHERE %I IS NOT NULL',
            r.table_name, r.column_name, r.column_name, r.column_name);
        RAISE NOTICE 'scrubbed email %.%', r.table_name, r.column_name;
    END LOOP;
END $$;

-- 3) Phone / name PII and cloned external credentials: NULL them out everywhere
--    (users handled above). Covers tenant Stripe/Twilio identifiers too.
DO $$
DECLARE r record;
BEGIN
    FOR r IN
        SELECT c.table_name, c.column_name
          FROM information_schema.columns c
          JOIN information_schema.tables t
            ON t.table_schema = c.table_schema AND t.table_name = c.table_name
         WHERE c.table_schema = 'public'
           AND t.table_type = 'BASE TABLE'
           AND c.table_name <> 'users'
           AND c.is_nullable = 'YES'
           AND c.column_name IN (
               -- contact PII
               'phone', 'parent_phone', 'parent_name', 'recipient_name', 'buyer_name',
               'emergency_contact_phone', 'emergency_contact_name',
               -- cloned external credentials / account ids (must not point at prod)
               'twilio_subaccount_sid', 'twilio_auth_token_encrypted', 'twilio_from_number',
               'twilio_messaging_service_sid', 'stripe_connect_account_id', 'stripe_terminal_location_id')
    LOOP
        EXECUTE format('UPDATE public.%I SET %I = NULL WHERE %I IS NOT NULL',
            r.table_name, r.column_name, r.column_name);
        RAISE NOTICE 'nulled %.%', r.table_name, r.column_name;
    END LOOP;
END $$;
