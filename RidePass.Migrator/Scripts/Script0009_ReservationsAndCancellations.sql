-- Phase 7: reservations (multi-spot, event-tied) + cancellation/refund tracking.

-- Day pass: event binding + quantity (multi-spot reservations) + cancellation fields.
ALTER TABLE day_pass_purchase ADD COLUMN event_id uuid NULL REFERENCES event(id) ON DELETE RESTRICT;
ALTER TABLE day_pass_purchase ADD COLUMN quantity int NOT NULL DEFAULT 1 CHECK (quantity > 0);
ALTER TABLE day_pass_purchase ADD COLUMN cancellation_reason text NULL;
ALTER TABLE day_pass_purchase ADD COLUMN cancelled_at timestamptz NULL;
ALTER TABLE day_pass_purchase ADD COLUMN cancelled_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE day_pass_purchase ADD COLUMN refund_note text NULL;

-- Extend status CHECK to include 'cancelled'.
ALTER TABLE day_pass_purchase DROP CONSTRAINT day_pass_purchase_status_check;
ALTER TABLE day_pass_purchase ADD CONSTRAINT day_pass_purchase_status_check
    CHECK (status IN ('pending','paid','failed','refunded','redeemed','cancelled'));

CREATE INDEX idx_day_pass_purchase_event ON day_pass_purchase (event_id) WHERE event_id IS NOT NULL;

-- Event tickets: cancellation fields (already have status/purchaser/stripe — no event binding
-- since tickets are already event-scoped via tier → event).
ALTER TABLE event_ticket_purchase ADD COLUMN cancellation_reason text NULL;
ALTER TABLE event_ticket_purchase ADD COLUMN cancelled_at timestamptz NULL;
ALTER TABLE event_ticket_purchase ADD COLUMN cancelled_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE event_ticket_purchase ADD COLUMN refund_note text NULL;

ALTER TABLE event_ticket_purchase DROP CONSTRAINT event_ticket_purchase_status_check;
ALTER TABLE event_ticket_purchase ADD CONSTRAINT event_ticket_purchase_status_check
    CHECK (status IN ('pending','paid','failed','refunded','redeemed','cancelled'));
