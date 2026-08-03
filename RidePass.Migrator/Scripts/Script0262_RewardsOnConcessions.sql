-- Punch-card rewards can now be earned on food and drink.
--
-- reward_program.requirement_kind said what counts toward "buy N, get one". It allowed 'pass'
-- (dead: day passes were retired), 'event_ticket', and 'any'. There was no way to say "coffees",
-- which makes the single most common F&B loyalty scheme inexpressible.
--
-- Worse, 'any' was a lie. RewardRepository.CountQualifyingPurchases only ever queried
-- event_ticket_purchase, so a tenant who chose "any purchase" and expected burgers to count got a
-- counter that never moved, with nothing anywhere saying why. That query is fixed alongside this
-- script; this only widens what the column is allowed to hold.
--
-- 'pass' stays permitted rather than being cleaned up here: existing rows may still carry it, the
-- counting code already returns 0 for it deliberately, and dropping a value out from under live
-- rows is a bigger change than this one wants to be.

ALTER TABLE reward_program DROP CONSTRAINT IF EXISTS reward_program_requirement_kind_check;
ALTER TABLE reward_program ADD CONSTRAINT reward_program_requirement_kind_check
    CHECK (requirement_kind IN ('pass', 'event_ticket', 'concession', 'any'));

-- Vouchers are redeemed against a source, and concession sales are now one of those sources.
-- reward_redemption.redeemed_on_kind is free text today; this comment records the vocabulary so a
-- future reader knows 'concession' is expected alongside 'event_ticket'.
COMMENT ON COLUMN reward_redemption.redeemed_on_kind IS
    'What the voucher was spent on: ''event_ticket'' or ''concession''. Set with redeemed_on_id when the sale settles.';

-- Which voucher (if any) was spent on a sale, so the CARD path can mark it used only once payment
-- actually succeeds. Cash settles inline in the controller and doesn't need this, but a card sale
-- is created 'pending' and finalised later by the Stripe webhook, which has no access to the
-- request that priced the cart. Without the column, a card customer's voucher would either be
-- burned before they paid, or never marked used at all and spendable twice.
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS reward_redemption_id uuid NULL
    REFERENCES reward_redemption (id) ON DELETE SET NULL;
