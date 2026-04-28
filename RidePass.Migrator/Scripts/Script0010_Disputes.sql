-- Phase 8: Stripe disputes / chargebacks. One row per Stripe dispute. Upserted from
-- charge.dispute.* webhooks — status and evidence_due_by may change over time as the
-- dispute progresses (warning → needs_response → won/lost).

CREATE TABLE dispute (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    day_pass_purchase_id uuid NULL REFERENCES day_pass_purchase(id) ON DELETE SET NULL,
    event_ticket_purchase_id uuid NULL REFERENCES event_ticket_purchase(id) ON DELETE SET NULL,
    stripe_dispute_id text NOT NULL UNIQUE,
    stripe_payment_intent_id text NOT NULL,
    stripe_charge_id text NULL,
    amount_cents bigint NOT NULL,
    currency text NOT NULL,
    reason text NULL,
    status text NOT NULL,
    evidence_due_by timestamptz NULL,
    stripe_created_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT dispute_purchase_link CHECK (
        (day_pass_purchase_id IS NOT NULL AND event_ticket_purchase_id IS NULL) OR
        (day_pass_purchase_id IS NULL AND event_ticket_purchase_id IS NOT NULL) OR
        (day_pass_purchase_id IS NULL AND event_ticket_purchase_id IS NULL)
    )
);

CREATE INDEX idx_dispute_tenant ON dispute (tenant_id);
CREATE INDEX idx_dispute_status ON dispute (status);
CREATE INDEX idx_dispute_payment_intent ON dispute (stripe_payment_intent_id);
