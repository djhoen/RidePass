-- Per-venue switch: when on, only the venue's Admins (tenant_admin) may refund. Managers keep
-- every other permission. Platform super admins are unaffected (they pass every check).
-- Highland asked for this ("only super admins [their Admins] can refund").
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS refunds_admin_only boolean NOT NULL DEFAULT false;
