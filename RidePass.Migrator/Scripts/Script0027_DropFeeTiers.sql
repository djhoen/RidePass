-- The flat per-tenant service charge introduced in Script0026 supersedes the tiered fee
-- system. Drop the tier tables. CASCADE auto-removes the tenant_ledger_entry.applied_tier_id
-- foreign key constraint; the column itself stays so historical entries remain readable.

DROP TABLE IF EXISTS tenant_fee_tier CASCADE;
DROP TABLE IF EXISTS tenant_fee_schedule CASCADE;
