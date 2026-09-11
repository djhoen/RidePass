-- Campaigns can send to a saved audience (Script0286): audience_kind 'audience' with
-- audience_config { "audienceId": ... }. Rerunnable: the CHECK is dropped and re-added with the
-- wider set (the original was added by Script0280).
ALTER TABLE email_campaign DROP CONSTRAINT IF EXISTS chk_email_campaign_audience_kind;
ALTER TABLE email_campaign ADD CONSTRAINT chk_email_campaign_audience_kind
    CHECK (audience_kind IN ('subscribers', 'event', 'event_type', 'pass_product', 'audience'));
