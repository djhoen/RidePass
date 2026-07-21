-- Per-tenant control over the "your bike is ready" notice, split by channel.
--
-- Script0209 shipped that notice as always-on, which is wrong for two reasons: a shop that phones
-- its customers doesn't want it, and TEXT COSTS MONEY PER MESSAGE, so it must never turn itself on
-- just because Twilio happens to be configured for the gate.
--
-- Defaults are deliberately asymmetric:
--   email = TRUE  - transactional, free, and plainly wanted by the customer ("come get your bike"),
--                   so it matches the behaviour a shop already had before this setting existed.
--   sms   = FALSE - every send bills the tenant, so it is opt-in.
-- This is the opposite reasoning to shop_service_reminder_days (Script0209), which defaults OFF
-- because a reminder months later is marketing-adjacent rather than something the customer is
-- waiting on.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS shop_ready_notify_email boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS shop_ready_notify_sms   boolean NOT NULL DEFAULT false;
