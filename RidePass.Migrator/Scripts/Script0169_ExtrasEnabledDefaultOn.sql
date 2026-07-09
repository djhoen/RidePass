-- Add-ons should be on for new tenants out of the box. The column was born
-- DEFAULT false in Script0054, but add-ons (camping, parking, merch) are part
-- of the standard offering for both MX and MTB venues and the super-admin
-- create form has defaulted the toggle to on for a while; this brings the
-- schema default in line so a tenant row inserted without the column (or via
-- a client that omits the flag) starts with add-ons enabled. Existing tenants
-- are left untouched -- this only changes the default for future inserts.

ALTER TABLE tenant ALTER COLUMN extras_enabled SET DEFAULT true;
