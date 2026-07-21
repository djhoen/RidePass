-- Loyalty phase 4: credit-back reward programs. A reward_program can now pay out store credit
-- as a rate on spend ("earn 5% back on every purchase") instead of minting a percent-off
-- voucher after N purchases. The award writes a 'loyalty_award' tenant_credit_entry keyed to
-- the settled purchase, so the once-per-reference unique index makes webhook + reconciler
-- double-fires idempotent (the index gains 'loyalty_award' below).
--
-- Additive + idempotent.

ALTER TABLE reward_program ADD COLUMN IF NOT EXISTS reward_kind text NOT NULL DEFAULT 'percent_off'
    CHECK (reward_kind IN ('percent_off', 'credit_rate'));
-- Basis points of the money collected that comes back as credit (500 = 5%).
ALTER TABLE reward_program ADD COLUMN IF NOT EXISTS credit_rate_bps int NULL
    CHECK (credit_rate_bps IS NULL OR (credit_rate_bps > 0 AND credit_rate_bps <= 10000));
-- Which spend qualifies for credit-back (independent of requirement_kind, which drives the
-- voucher counting of percent_off programs).
ALTER TABLE reward_program ADD COLUMN IF NOT EXISTS credit_qualifying_kind text NOT NULL DEFAULT 'any'
    CHECK (credit_qualifying_kind IN ('any', 'event_ticket', 'concession', 'shop_sale'));

DROP INDEX IF EXISTS uk_credit_entry_once_per_ref;
CREATE UNIQUE INDEX uk_credit_entry_once_per_ref
    ON tenant_credit_entry (kind, reference_kind, reference_id)
    WHERE reference_id IS NOT NULL
      AND kind IN ('deposit_excess', 'refund_to_credit', 'redeem', 'redeem_reversal', 'loyalty_award');
