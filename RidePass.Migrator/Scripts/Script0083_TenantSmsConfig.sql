-- Per-tenant Twilio SMS configuration. Each tenant gets its own Twilio
-- Subaccount (provisioned by ITwilioSubaccountProvisioner on demand) so that
-- their customers see a tenant-owned phone number and so that 10DLC/toll-free
-- compliance is registered against the tenant's brand, not RidePass's. Auth
-- token is stored encrypted at rest via EncryptionHelper.
--
-- sms_enabled is the tenant-visible on/off; an off-but-provisioned tenant
-- still owns the number (we keep paying Twilio the rental) but no sends fire.
-- This shape lets tenants toggle without losing their number + 10DLC reg.
--
-- Nullable on purpose: the column exists for every tenant from day 1 but
-- only populated for tenants that have completed provisioning. No backfill
-- needed — existing flows fall back to the global Sms:Twilio:* config until
-- per-tenant credentials land (TwilioSmsSender resolves tenant-first, global-
-- second).

ALTER TABLE tenant
    ADD COLUMN twilio_subaccount_sid     text         NULL,
    ADD COLUMN twilio_auth_token_encrypted text       NULL,
    ADD COLUMN twilio_from_number        text         NULL,
    ADD COLUMN sms_enabled               boolean      NOT NULL DEFAULT false,
    ADD COLUMN sms_enabled_at_utc        timestamptz  NULL;
