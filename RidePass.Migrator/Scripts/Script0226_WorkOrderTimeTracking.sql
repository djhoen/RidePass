-- Estimated-vs-actual labor time on work orders (small-shop friendly: one timer per ticket, not
-- per-tech clock-ins).
--   shop_work_order_line.estimated_minutes    per labor line, summed to a job estimate. Null on parts.
--   shop_work_order.actual_minutes            accumulated worked minutes (from the timer or a manual set)
--   shop_work_order.timer_started_at          when the running segment started; NULL = timer stopped
--   shop_job_template_line.estimated_minutes  standard time carried by a saved job, auto-filling the estimate
--
-- Additive, rerunnable, backwards-compatible (existing rows read as no estimate / zero actual / stopped).

ALTER TABLE shop_work_order_line
    ADD COLUMN IF NOT EXISTS estimated_minutes integer;

ALTER TABLE shop_work_order
    ADD COLUMN IF NOT EXISTS actual_minutes integer NOT NULL DEFAULT 0;

ALTER TABLE shop_work_order
    ADD COLUMN IF NOT EXISTS timer_started_at timestamptz;

ALTER TABLE shop_job_template_line
    ADD COLUMN IF NOT EXISTS estimated_minutes integer;
