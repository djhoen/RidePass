-- Public home page: a benefits section (content + optional side image) plus a
-- per-section visibility map so tenants can toggle any non-hero section on/off.
-- home_sections_json is a { sectionKey: bool } object; a MISSING key means visible,
-- so existing tenants keep every section on by default.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS home_benefits_html text NULL,
    ADD COLUMN IF NOT EXISTS home_sections_json jsonb NOT NULL DEFAULT '{}'::jsonb;

-- Images live on tenant_branding (same place as hero / secondary hero), so the
-- benefits image rides the existing upload pipeline via a new "benefits" kind.
ALTER TABLE tenant_branding
    ADD COLUMN IF NOT EXISTS home_benefits_image_url text NULL;
