-- Campaigns and automation steps can go out as email, text, or both. Send rows carry the
-- channel and, for texts, the phone they went to; the dedupe keys gain the channel so one
-- person can receive the email AND the text of the same step. Additive and rerunnable.

-- Campaigns
ALTER TABLE email_campaign ADD COLUMN IF NOT EXISTS channel  text NOT NULL DEFAULT 'email';
ALTER TABLE email_campaign ADD COLUMN IF NOT EXISTS sms_body text NULL;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_email_campaign_channel') THEN
        ALTER TABLE email_campaign ADD CONSTRAINT chk_email_campaign_channel
            CHECK (channel IN ('email', 'sms', 'both'));
    END IF;
END $$;

ALTER TABLE email_campaign_send ADD COLUMN IF NOT EXISTS channel text NOT NULL DEFAULT 'email';
ALTER TABLE email_campaign_send ADD COLUMN IF NOT EXISTS phone   text NULL;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_email_campaign_send_channel') THEN
        ALTER TABLE email_campaign_send ADD CONSTRAINT chk_email_campaign_send_channel
            CHECK (channel IN ('email', 'sms'));
    END IF;
END $$;
ALTER TABLE email_campaign_send DROP CONSTRAINT IF EXISTS uk_email_campaign_send;
CREATE UNIQUE INDEX IF NOT EXISTS uk_email_campaign_send_channel
    ON email_campaign_send (campaign_id, email, channel);

-- Automations
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS channel  text NOT NULL DEFAULT 'email';
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS sms_body text NULL;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_marketing_automation_step_channel') THEN
        ALTER TABLE marketing_automation_step ADD CONSTRAINT chk_marketing_automation_step_channel
            CHECK (channel IN ('email', 'sms', 'both'));
    END IF;
END $$;

ALTER TABLE marketing_automation_send ADD COLUMN IF NOT EXISTS channel text NOT NULL DEFAULT 'email';
ALTER TABLE marketing_automation_send ADD COLUMN IF NOT EXISTS phone   text NULL;
DROP INDEX IF EXISTS uk_automation_send_once;
CREATE UNIQUE INDEX IF NOT EXISTS uk_automation_send_once_channel
    ON marketing_automation_send (step_id, subject_kind, subject_id, channel);
