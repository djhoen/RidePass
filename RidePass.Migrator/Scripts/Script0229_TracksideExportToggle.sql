-- Trackside export becomes a tenant feature toggle. The trackside handout is a
-- motocross-race artifact, so it stays on for motocross tenants and defaults OFF
-- for mountain-bike tenants (they can turn it on from Settings > Features).
-- Rerunnable: IF NOT EXISTS on the column; the backfill only touches rows that
-- still carry the column default (guarded by a one-shot predicate below).

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS trackside_export_enabled boolean NOT NULL DEFAULT true;

-- One-shot backfill: only meaningful the first time the column appears. Re-running
-- later must not stomp an MTB tenant that deliberately turned the feature on, so
-- the update is fenced by a journal check on this script's own name.
DO $mig$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schemaversions
        WHERE scriptname LIKE '%Script0229_TracksideExportToggle%'
    ) THEN
        UPDATE tenant SET trackside_export_enabled = false
        WHERE tenant_type = 'mountain_bike';
    END IF;
END $mig$;
