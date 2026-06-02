-- Reverse phone lookup for inbound SMS.
--
-- users.phone is stored loosely (admin types whatever the user typed) and
-- normalized to E.164 at send-time by TwilioSmsSender.NormalizeE164. That
-- shape's fine for outbound, but for INBOUND we need to go the other way:
-- given a Twilio "From" in E.164 (+15551234567), find the user whose loosely
-- stored phone represents the same number.
--
-- Doing that in C# would require pulling every user with a non-null phone,
-- normalizing each in memory, and comparing — fine at 100 users, painful at
-- 100k. Instead this migration adds a Postgres function that mirrors
-- TwilioSmsSender.NormalizeE164 and an expression index over it, so reverse
-- lookup is a single index probe regardless of table size.
--
-- The function MUST stay in sync with NormalizeE164. The rules:
--   • If input starts with '+', strip non-digits from the rest and re-prefix.
--   • Otherwise strip all non-digits.
--   • Length 10 → assume US, prefix '+1'.
--   • Length 11 starting with '1' → prefix '+'.
--   • Length >=10 → prefix '+' and accept (international).
--   • Anything shorter → NULL.
-- IMMUTABLE so the expression index is allowed.
--
-- Backfill: any existing tenant_conversation rows whose customer_user_id is
-- null get filled in where exactly one user matches the normalized phone.
-- The "exactly one" filter avoids picking arbitrarily when multiple users
-- share a number — admin can still set the link manually via the Inbox UI
-- later if we add that affordance.

CREATE OR REPLACE FUNCTION fn_phone_e164(raw text) RETURNS text AS $$
DECLARE
    digits text;
BEGIN
    IF raw IS NULL OR length(trim(raw)) = 0 THEN
        RETURN NULL;
    END IF;

    IF substring(raw FROM 1 FOR 1) = '+' THEN
        RETURN '+' || regexp_replace(substring(raw FROM 2), '[^0-9]', '', 'g');
    END IF;

    digits := regexp_replace(raw, '[^0-9]', '', 'g');

    IF length(digits) = 10 THEN
        RETURN '+1' || digits;
    ELSIF length(digits) = 11 AND substring(digits FROM 1 FOR 1) = '1' THEN
        RETURN '+' || digits;
    ELSIF length(digits) >= 10 THEN
        RETURN '+' || digits;
    ELSE
        RETURN NULL;
    END IF;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE INDEX ix_users_phone_e164
    ON users (fn_phone_e164(phone))
    WHERE phone IS NOT NULL;

-- Backfill existing conversations where the inbound number resolves to
-- exactly one user. The subquery's HAVING count = 1 condition is what keeps
-- us from arbitrarily linking when two users share a phone — those stay null
-- until a human picks.
-- HAVING count(*) = 1 already guarantees a single match per conversation,
-- so array_agg(u.id)[1] just extracts that single uuid. Avoids min(uuid)
-- which Postgres doesn't have a built-in overload for.
UPDATE tenant_conversation tc
SET customer_user_id = match.user_id
FROM (
    SELECT
        c.id AS conversation_id,
        (array_agg(u.id))[1] AS user_id
    FROM tenant_conversation c
    JOIN users u
        ON u.phone IS NOT NULL
       AND fn_phone_e164(u.phone) = c.customer_phone
    WHERE c.customer_user_id IS NULL
    GROUP BY c.id
    HAVING count(*) = 1
) AS match
WHERE tc.id = match.conversation_id;
