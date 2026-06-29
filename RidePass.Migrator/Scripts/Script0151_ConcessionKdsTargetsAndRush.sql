-- Cook-screen targets (configurable color-escalation thresholds) + a per-order rush/priority flag.
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS prep_warn_minutes int NOT NULL DEFAULT 5;
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS prep_late_minutes int NOT NULL DEFAULT 10;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS is_rush boolean NOT NULL DEFAULT false;
