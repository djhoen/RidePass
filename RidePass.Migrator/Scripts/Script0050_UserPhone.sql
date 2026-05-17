-- User phone number — required for new accounts, used for SMS notifications
-- (waitlist promotion, eventually generic event/tenant alerts). Stored loosely
-- (TwilioSmsSender normalizes to E.164 at send time so users can type either
-- "555-123-4567" or "+15551234567"). Existing users default to NULL until they
-- update their profile; the API only blocks waitlist signup when phone is NULL.

ALTER TABLE users
    ADD COLUMN phone text NULL;
