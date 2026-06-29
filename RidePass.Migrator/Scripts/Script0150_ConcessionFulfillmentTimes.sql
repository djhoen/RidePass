-- Track kitchen timing: when every line was ready (food done) and when the cook completed/picked up the
-- order. Combined with created_at (submitted) and paid_at (entered the kitchen) this gives prep time and
-- total order time. Both NULL for historical orders; populated going forward.
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS ready_at     timestamptz NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL;
