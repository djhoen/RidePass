-- One-time migration: repoint stored image URLs from the host-relative "/uploads/..."
-- form (LocalFilesystemImageStorage) to absolute Spaces URLs, after the files have been
-- synced into the bucket (e.g. `s3cmd sync` / `aws s3 sync wwwroot/uploads s3://bucket/uploads`).
--
-- Self-maintaining: it rewrites EVERY text column on every base table whose value starts
-- with "/uploads/", so all current and future image-URL columns are covered without
-- listing them. Idempotent: a second run finds no "/uploads/%" values (they're now
-- absolute) and changes nothing.
--
-- Usage (edit the base URL below first, no trailing slash):
--   psql "$PROD_DB_URL" -v ON_ERROR_STOP=1 -f scripts/rewrite-upload-urls.sql
--
-- Note: image URLs embedded inside rich-text/HTML columns (e.g. about_html) are NOT
-- rewritten (they don't *start* with /uploads/). Keep the local files in place so those
-- continue to serve via the /uploads static route until any such content is re-saved.

\set base 'https://REPLACE_WITH_PUBLIC_BASE_URL'
SELECT set_config('app.upload_base', :'base', false);

DO $$
DECLARE
    r record;
    base text := current_setting('app.upload_base');
BEGIN
    IF base IS NULL OR base = '' OR base LIKE '%REPLACE%' THEN
        RAISE EXCEPTION 'Set the base URL (\\set base ...) at the top of this script first.';
    END IF;
    base := rtrim(base, '/');

    FOR r IN
        SELECT c.table_name, c.column_name
          FROM information_schema.columns c
          JOIN information_schema.tables t
            ON t.table_schema = c.table_schema AND t.table_name = c.table_name
         WHERE c.table_schema = 'public'
           AND t.table_type = 'BASE TABLE'
           AND c.data_type IN ('text', 'character varying')
    LOOP
        -- new value = base || old value (old value already begins with "/uploads/")
        EXECUTE format(
            'UPDATE public.%I SET %I = %L || %I WHERE %I LIKE ''/uploads/%%''',
            r.table_name, r.column_name, base, r.column_name, r.column_name);
        IF FOUND THEN
            RAISE NOTICE 'repointed %.%', r.table_name, r.column_name;
        END IF;
    END LOOP;
END $$;
