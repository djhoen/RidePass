-- Direct charges, phase 2b: waitlist + gift card.
--
-- Waitlist pre-pay charges on its own PaymentIntent (prepay_pi_id) before any ticket row exists;
-- the promoter later creates the ticket. Snapshot the connected account on the waitlist entry at
-- pre-pay time so the promoter can carry it onto the ticket row (for correct refunds). The
-- ConfirmAndPay promotion path creates a ticket directly and already has the column from Script0136.
--
-- Gift cards need no column here: they are never refunded or reconciled, so routing the charge to
-- the connected account (with our service fee as the application fee) is all that's required.
ALTER TABLE event_waitlist ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;
