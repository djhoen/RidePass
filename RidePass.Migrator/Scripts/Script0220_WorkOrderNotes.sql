-- Split work-order notes into customer-facing vs internal, per the service benchmark (leaders keep
-- receipt notes separate from a timestamped internal log).
--
--   shop_work_order.customer_notes   one field, printed on the claim tag and the bill. Safe to
--                                    show the customer, unlike intake symptoms or bench chatter.
--   shop_work_order_note             append-only internal thread: who wrote it and when. This is
--                                    the "timestamped internal notes" half; never shown to the
--                                    customer. intake_notes stays as the drop-off symptom note.
--
-- Additive and rerunnable.

ALTER TABLE shop_work_order
    ADD COLUMN IF NOT EXISTS customer_notes text;

CREATE TABLE IF NOT EXISTS shop_work_order_note (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Carried directly (not only via the parent) so every read scopes by tenant without a join.
    tenant_id          uuid NOT NULL REFERENCES tenant(id),
    work_order_id      uuid NOT NULL REFERENCES shop_work_order(id) ON DELETE CASCADE,
    body               text NOT NULL,
    created_by_user_id uuid REFERENCES users(id),
    created_at         timestamptz NOT NULL DEFAULT now()
);

-- The thread is read newest-first per work order.
CREATE INDEX IF NOT EXISTS ix_shop_work_order_note_wo
    ON shop_work_order_note (work_order_id, created_at DESC);
