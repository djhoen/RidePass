-- Multi-waiver fixup: drop the per-tenant unique-version constraint left over
-- from the single-waiver schema. Each waiver now owns its own version sequence
-- starting at v1, so two distinct waivers within the same tenant must be able
-- to share a version number.

ALTER TABLE tenant_waiver DROP CONSTRAINT IF EXISTS uk_tenant_waiver_version;
