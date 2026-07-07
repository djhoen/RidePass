-- Snapshot the Stripe connected account a gift-card purchase was charged on, so the pending-purchase
-- reconciler can query the PaymentIntent status on the right account for direct-charge tenants (their
-- gift cards are sold on their own connected account). NULL = charged on the platform account.
-- Additive and rerunnable. Existing pending platform-mode cards reconcile fine with NULL; pre-existing
-- direct-mode pending cards (rare, and none until direct mode is live) simply won't auto-reconcile.

ALTER TABLE gift_card ADD COLUMN IF NOT EXISTS stripe_connected_account_id text NULL;
