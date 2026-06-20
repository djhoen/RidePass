-- Gift cards must not be spendable or deliverable until the purchase is actually paid.
--
-- Before this, BuyGiftCard minted the card with status='active' and full balance up front,
-- then the webhook just sent the delivery email on success. A declined or abandoned purchase
-- left a fully active card, and worse, the scheduled-delivery worker (delivery_status='pending'
-- AND status='active' AND scheduled IS NULL) would auto-email the code for an unpaid immediate
-- card, making it genuinely redeemable.
--
-- Fix: mint the card 'pending', activate it only on payment_intent.succeeded, and void it on
-- payment_intent.payment_failed. The redemption validator and the delivery worker both gate on
-- status='active', so a 'pending' (unpaid) card is neither spendable nor deliverable.
--
-- This migration just widens the status CHECK to allow the new 'pending' and 'void' states.
-- Existing rows are all paid-and-active and are unaffected; the column default stays 'active'.

ALTER TABLE gift_card DROP CONSTRAINT IF EXISTS gift_card_status_check;
ALTER TABLE gift_card ADD CONSTRAINT gift_card_status_check
    CHECK (status IN ('pending', 'active', 'depleted', 'refunded', 'void'));
