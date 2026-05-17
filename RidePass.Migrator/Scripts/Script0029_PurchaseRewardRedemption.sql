-- Track which reward voucher (if any) was applied to each purchase line so the webhook
-- can mark the redemption used after Stripe confirms — and so refund/cancel flows can
-- (in the future) restore the voucher.

ALTER TABLE day_pass_purchase
    ADD COLUMN applied_reward_redemption_id uuid NULL REFERENCES reward_redemption(id) ON DELETE SET NULL;

ALTER TABLE event_ticket_purchase
    ADD COLUMN applied_reward_redemption_id uuid NULL REFERENCES reward_redemption(id) ON DELETE SET NULL;
