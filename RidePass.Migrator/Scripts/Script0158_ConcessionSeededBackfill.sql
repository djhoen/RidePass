-- Backfill seeded_at for tenants that already have an F&B catalog from before seed-tracking existed
-- (Script0157), so the "Load starter content" button hides for them too. "Has a catalog" = at least one
-- concession product. Both statements are idempotent: the UPDATE only touches still-NULL rows, and the
-- INSERT only creates rows for product-having tenants that have no menu-settings row yet.

-- Tenants with products but no menu-settings row yet: create one, already stamped seeded.
INSERT INTO concession_menu_settings (tenant_id, seeded_at, updated_at)
SELECT DISTINCT p.tenant_id, now(), now()
FROM concession_product p
WHERE NOT EXISTS (SELECT 1 FROM concession_menu_settings ms WHERE ms.tenant_id = p.tenant_id)
ON CONFLICT (tenant_id) DO NOTHING;

-- Existing menu-settings rows that were never stamped but whose tenant already has products.
UPDATE concession_menu_settings ms
SET seeded_at = now()
WHERE ms.seeded_at IS NULL
  AND EXISTS (SELECT 1 FROM concession_product p WHERE p.tenant_id = ms.tenant_id);
