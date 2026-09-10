-- Broadcast campaigns get an AUDIENCE. Until now every campaign went to the newsletter list;
-- a track also needs "everyone who bought this camp", "everyone who bought any race this
-- season", and "everyone holding this pass". The recipient snapshot still lands in
-- email_campaign_send at send time (subscriber_id is NULL for purchase-sourced rows), so
-- reporting and the send handler are unchanged.
-- audience_config holds the target ids and an optional date window, e.g.
--   {"eventId": "..."} | {"eventTypeId": "...", "fromUtc": "...", "toUtc": "..."} | {"passProductId": "..."}
-- Additive and rerunnable.
ALTER TABLE email_campaign ADD COLUMN IF NOT EXISTS audience_kind   text  NOT NULL DEFAULT 'subscribers';
ALTER TABLE email_campaign ADD COLUMN IF NOT EXISTS audience_config jsonb NOT NULL DEFAULT '{}'::jsonb;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_email_campaign_audience_kind') THEN
        ALTER TABLE email_campaign ADD CONSTRAINT chk_email_campaign_audience_kind
            CHECK (audience_kind IN ('subscribers', 'event', 'event_type', 'pass_product'));
    END IF;
END $$;
