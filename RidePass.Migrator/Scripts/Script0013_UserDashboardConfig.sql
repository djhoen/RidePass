-- Phase 11: per-user dashboard widget layout. JSON blob keeps this open-ended
-- so adding new widget types doesn't require further migrations. NULL means
-- "use role defaults".

ALTER TABLE users ADD COLUMN dashboard_config jsonb NULL;
