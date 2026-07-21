-- "Checked by" QC sign-off on a work order: a second reviewer attests the job before it goes out.
-- Ascend reports this measurably reduces comebacks. Both columns nullable (unchecked = both null);
-- checked_at stamps when the sign-off happened. Additive and rerunnable.

ALTER TABLE shop_work_order
    ADD COLUMN IF NOT EXISTS checked_by_user_id uuid REFERENCES users(id);

ALTER TABLE shop_work_order
    ADD COLUMN IF NOT EXISTS checked_at timestamptz;
