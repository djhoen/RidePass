-- Multiple waivers per tenant + per-event waiver attachment + waiver expiration.
--
-- Before:
--   * Tenant had at most ONE active waiver (uk_tenant_waiver_active partial index).
--   * Events implicitly used that one active waiver via event.requires_waiver bool.
--
-- After:
--   * Many waivers can be active simultaneously (e.g. "General", "Race-Day", "Minor").
--   * Each waiver has an optional human-readable `name` admins use to label it
--     (separate from the legal `title`) plus an optional `expires_at` cutoff.
--   * Events can attach a specific waiver via event.waiver_id. Null = fall back to
--     any active non-expired tenant waiver (keeps existing single-waiver tenants
--     working without further config).

DROP INDEX IF EXISTS uk_tenant_waiver_active;

ALTER TABLE tenant_waiver
    ADD COLUMN name       text        NOT NULL DEFAULT 'Waiver',
    ADD COLUMN expires_at timestamptz NULL;

-- Backfill existing tenants: the single waiver each had becomes their default
-- "General Waiver" so the admin UI labels read sensibly out of the gate.
UPDATE tenant_waiver SET name = 'General Waiver' WHERE name = 'Waiver';

-- ON DELETE SET NULL — if an admin removes a waiver, the event simply loses its
-- explicit attachment and falls back to whatever the tenant has active. The
-- rider_waiver_signature.waiver_id stays cascading-delete since the signature
-- only makes sense in the context of its waiver text.
ALTER TABLE event
    ADD COLUMN waiver_id uuid NULL REFERENCES tenant_waiver(id) ON DELETE SET NULL;

CREATE INDEX idx_event_waiver_id ON event (waiver_id) WHERE waiver_id IS NOT NULL;
