-- Route outbound SMS through a per-tenant Twilio Messaging Service instead
-- of binding the send call to a single From number.
--
-- Why this exists even though we still ship one toll-free number per tenant:
-- a Messaging Service is Twilio's "sender pool" abstraction. Today the pool
-- has exactly one sender (the toll-free number we just bought). When a
-- tenant later wants a short code or a 10DLC long code for higher
-- throughput, we attach the new sender to the same MG SID and the send
-- path keeps working unchanged — Twilio handles sticky-sender for two-way
-- threads, fallback if a sender is throttled, etc. Doing it now while the
-- pool is single-sender means zero refactor when we add types later.
--
-- Nullable: tenants provisioned before this column existed have NULL here
-- and continue to send via twilio_from_number directly. TwilioSmsSender
-- prefers MessagingServiceSid when set and falls back to the legacy path
-- otherwise. No backfill — those tenants keep working as-is, and the next
-- time they re-provision they'll pick up an MG.

ALTER TABLE tenant
    ADD COLUMN twilio_messaging_service_sid text NULL;
