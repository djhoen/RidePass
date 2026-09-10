-- Dynamic campaigns, Phase 1 (docs/dynamic-campaigns-plan.md): a second trigger and step timing
-- that can be measured from something other than the purchase.
--
-- trigger_kind gains 'event_ticket_purchased' (config: {"eventId": ...} or {"eventTypeId": ...}).
-- Each step gains an ANCHOR ('purchase' | 'event_start' | 'event_end' | 'pass_expiry' |
-- 'fixed_date'), a signed OFFSET in days (negative = before), and SEND_ON for the fixed-date
-- anchor. delay_days stays for purchase-anchored steps so nothing already built changes meaning;
-- the API keeps delay_days = offset_days for that anchor.
-- Additive and rerunnable.

ALTER TABLE marketing_automation DROP CONSTRAINT IF EXISTS ck_marketing_automation_trigger;
ALTER TABLE marketing_automation ADD CONSTRAINT ck_marketing_automation_trigger
    CHECK (trigger_kind IN ('season_pass_purchased', 'event_ticket_purchased'));

ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS anchor      text NOT NULL DEFAULT 'purchase';
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS offset_days int  NOT NULL DEFAULT 0;
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS send_on     date NULL;

-- Existing purchase-anchored steps: offset mirrors the delay they already had.
UPDATE marketing_automation_step SET offset_days = delay_days
WHERE anchor = 'purchase' AND offset_days = 0 AND delay_days <> 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_automation_step_anchor') THEN
        ALTER TABLE marketing_automation_step ADD CONSTRAINT ck_automation_step_anchor
            CHECK (anchor IN ('purchase', 'event_start', 'event_end', 'pass_expiry', 'fixed_date'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_automation_step_fixed_date') THEN
        -- A fixed-date step must carry its date; every other anchor must not.
        ALTER TABLE marketing_automation_step ADD CONSTRAINT ck_automation_step_fixed_date
            CHECK ((anchor = 'fixed_date') = (send_on IS NOT NULL));
    END IF;
END $$;
