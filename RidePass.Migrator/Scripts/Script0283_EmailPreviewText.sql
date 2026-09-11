-- Preview text (the "preheader" inboxes show after the subject line) for broadcast campaigns
-- and for each automation email. NULL = the inbox picks the first words of the body, as today.
-- Additive and rerunnable.
ALTER TABLE email_campaign            ADD COLUMN IF NOT EXISTS preview_text text NULL;
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS preview_text text NULL;
