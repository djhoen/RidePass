-- Tracks the first time a tenant was ever published, never reset. Powers the
-- stage->prod tenant promotion guard: a tenant that has EVER been published (or
-- has live orders) can never be overwritten by an import, because it may hold real
-- customer data. NULL = never published = safe to overwrite while still a demo.

ALTER TABLE tenant ADD COLUMN first_published_at timestamptz;

-- Backfill: any tenant currently published is treated as already-published.
UPDATE tenant SET first_published_at = now()
WHERE is_published = true AND first_published_at IS NULL;

-- Stamp first_published_at the first time is_published flips true; never overwrite it
-- (so unpublishing + republishing keeps the original "ever published" mark).
CREATE OR REPLACE FUNCTION set_tenant_first_published_at() RETURNS trigger AS $$
BEGIN
    IF NEW.is_published AND NEW.first_published_at IS NULL THEN
        NEW.first_published_at := now();
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_tenant_first_published_at ON tenant;
CREATE TRIGGER trg_tenant_first_published_at
    BEFORE INSERT OR UPDATE OF is_published ON tenant
    FOR EACH ROW
    EXECUTE FUNCTION set_tenant_first_published_at();
