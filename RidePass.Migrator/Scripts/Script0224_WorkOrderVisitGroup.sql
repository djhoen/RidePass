-- Lightweight "customer visit" grouping: several work orders (one per bike) that were dropped off
-- together share a group_id, so a family bringing three bikes is one intake and one place to see
-- them all, while each bike keeps its own status, QC, deposit, lines and "ready" notification. The
-- group_id is just a shared key (no separate table yet); a null group_id is a solo ticket, exactly
-- as today. Additive, rerunnable, backwards-compatible.

ALTER TABLE shop_work_order
    ADD COLUMN IF NOT EXISTS group_id uuid;

-- Finding a visit's siblings is a per-tenant lookup by group_id.
CREATE INDEX IF NOT EXISTS ix_shop_work_order_group
    ON shop_work_order (tenant_id, group_id) WHERE group_id IS NOT NULL;
