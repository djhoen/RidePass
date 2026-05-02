-- Allow multiple purchase rows to share a single Stripe PaymentIntent so a counter
-- cart with mixed line items (passes + tickets) can be charged on one PaymentIntent.
-- Replaces partial UNIQUE indexes on stripe_payment_intent_id with non-unique ones
-- for the same lookup-by-PI workload.

DROP INDEX IF EXISTS uk_day_pass_purchase_stripe_pi;
DROP INDEX IF EXISTS uk_event_ticket_purchase_stripe_pi;

CREATE INDEX IF NOT EXISTS idx_day_pass_purchase_stripe_pi
    ON day_pass_purchase (stripe_payment_intent_id)
    WHERE stripe_payment_intent_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_event_ticket_purchase_stripe_pi
    ON event_ticket_purchase (stripe_payment_intent_id)
    WHERE stripe_payment_intent_id IS NOT NULL;
