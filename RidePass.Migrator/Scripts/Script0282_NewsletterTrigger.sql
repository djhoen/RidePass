-- Dynamic campaigns, Phase 2: automations can start when someone joins the newsletter
-- (a welcome series). Subject rows are newsletter_subscriber; subject_kind 'newsletter_subscriber'.
-- Rerunnable: the CHECK is dropped and re-added with the wider set.
ALTER TABLE marketing_automation DROP CONSTRAINT IF EXISTS ck_marketing_automation_trigger;
ALTER TABLE marketing_automation ADD CONSTRAINT ck_marketing_automation_trigger
    CHECK (trigger_kind IN ('season_pass_purchased', 'event_ticket_purchased', 'newsletter_subscribed'));
