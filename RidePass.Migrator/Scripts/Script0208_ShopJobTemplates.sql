-- Saved repair jobs ("standard jobs"), so a shop stops retyping the work it does every week.
--
-- From the Lightspeed DMS comparison in docs/bike-shop.md: DMS saves "job titles, descriptions,
-- required parts, labor hours, and specific rates into an instantly accessible library". An MX
-- shop repeats a short list of jobs constantly (fork seals, top end, suspension service, tire and
-- mousse), and today every work order line is typed from scratch.
--
-- A template's lines mirror shop_work_order_line's shape so applying one is a straight copy:
-- 'labor' carries a description, 'part' points at a variant. The same CHECK is restated here so a
-- malformed template can't exist and then fail at apply time.

CREATE TABLE IF NOT EXISTS shop_job_template (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    -- Free-text fit note, the equivalent of DMS filtering by year/make/model. Deliberately not a
    -- structured vehicle model: an MX shop's "fits 250F four-strokes" is a judgement, not a table.
    fits_note   text        NULL,
    -- Prefilled into the work order's intake notes when applied, so the standard caveats
    -- ("bring your own gasket if you want OEM") travel with the job.
    notes       text        NULL,
    is_active   boolean     NOT NULL DEFAULT true,
    sort_order  int         NOT NULL DEFAULT 100,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_shop_job_template_tenant
    ON shop_job_template (tenant_id, is_active, sort_order, name);
-- One template per name per tenant, so the picker can't show two identical entries.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_job_template_name
    ON shop_job_template (tenant_id, lower(name));

DROP TRIGGER IF EXISTS trg_shop_job_template_updated_at ON shop_job_template;
CREATE TRIGGER trg_shop_job_template_updated_at BEFORE UPDATE ON shop_job_template
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_job_template_line (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    template_id      uuid        NOT NULL REFERENCES shop_job_template(id) ON DELETE CASCADE,
    line_kind        text        NOT NULL CHECK (line_kind IN ('labor', 'part')),
    description      text        NULL,
    -- RESTRICT: a variant used by a template can be deactivated but not deleted out from under it.
    variant_id       uuid        NULL REFERENCES shop_variant(id) ON DELETE RESTRICT,
    quantity         int         NOT NULL DEFAULT 1 CHECK (quantity > 0),
    -- Labor: the rate to charge for this job. Parts: NULL means "use the variant's price at the
    -- moment the template is applied", which is almost always what you want, since a saved part
    -- price silently goes stale.
    unit_price_cents int         NULL CHECK (unit_price_cents IS NULL OR unit_price_cents >= 0),
    sort_order       int         NOT NULL DEFAULT 100,
    created_at       timestamptz NOT NULL DEFAULT now(),
    -- Same shape rule as shop_work_order_line: labor describes itself, a part points at stock.
    CONSTRAINT chk_shop_job_template_line_shape CHECK (
        (line_kind = 'labor' AND description IS NOT NULL AND variant_id IS NULL)
     OR (line_kind = 'part'  AND variant_id IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_shop_job_template_line_template
    ON shop_job_template_line (template_id, sort_order);
CREATE INDEX IF NOT EXISTS idx_shop_job_template_line_variant
    ON shop_job_template_line (variant_id) WHERE variant_id IS NOT NULL;
