-- Give every profit center a color, so a center reads the same on the Sales Summary chart, the
-- End of Day report, Revenue by Department and the QuickBooks mapping screen.
--
-- Nullable rather than NOT NULL DEFAULT: the default belongs to the CODE
-- (Services.Accounting.ProfitCenterPalette), which assigns the first unused palette slot when a
-- center is created and knows which slot is held back for the "total revenue" series. Freezing
-- one hex into a column default would paint every new center the same color instead.
--
-- The backfill hands existing centers the palette in sort order, so a tenant who configured
-- centers before this migration lands on the same colors they would have been given had they
-- created them today. Rerunnable and non-destructive via `AND color IS NULL`: replaying must
-- never repaint a center the tenant has since recolored.
--
-- The palette here is slots 2-8 of the validated categorical order; slot 1 (blue #2a78d6) is
-- deliberately absent because charts reserve it for the all-revenue line. Keep this array in
-- lockstep with ProfitCenterPalette.Slots — the C# copy is the one the app reads at runtime,
-- this one only seeds rows that already exist.

ALTER TABLE profit_center
    ADD COLUMN IF NOT EXISTS color text NULL;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_profit_center_color') THEN
        ALTER TABLE profit_center
            ADD CONSTRAINT chk_profit_center_color
            CHECK (color IS NULL OR color ~ '^#[0-9a-fA-F]{6}$');
    END IF;
END $$;

WITH palette AS (
    SELECT unnest(ARRAY[
        '#eb6834',   -- orange
        '#1baf7a',   -- aqua
        '#eda100',   -- yellow
        '#e87ba4',   -- magenta
        '#008300',   -- green
        '#4a3aa7',   -- violet
        '#e34948'    -- red
    ]) AS hex, generate_series(0, 6) AS idx
),
ranked AS (
    SELECT id,
           -- Per TENANT, so each tenant's first center gets the first palette color rather than
           -- the ordering depending on how many other tenants configured centers first.
           (ROW_NUMBER() OVER (PARTITION BY tenant_id ORDER BY sort_order, lower(name)) - 1) AS idx
    FROM profit_center
    WHERE color IS NULL
)
UPDATE profit_center pc
SET color = p.hex
FROM ranked r
JOIN palette p ON p.idx = (r.idx % 7)     -- past seven centers colors repeat; the UI flags it
WHERE pc.id = r.id
  AND pc.color IS NULL;
