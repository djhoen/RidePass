-- Automations can start when someone joins a saved audience (Script0286). Subject rows are
-- audience_member; subject_kind 'audience_member'. Rerunnable: the CHECK is dropped and
-- re-added with the wider set, same as Script0282.
ALTER TABLE marketing_automation DROP CONSTRAINT IF EXISTS ck_marketing_automation_trigger;
ALTER TABLE marketing_automation ADD CONSTRAINT ck_marketing_automation_trigger
    CHECK (trigger_kind IN ('season_pass_purchased', 'event_ticket_purchased', 'newsletter_subscribed', 'audience_joined'));
