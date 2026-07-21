-- Condition photos for bike shop work orders and rentals.
--
-- Lightspeed Retail allows up to 12 images on a work order and nothing else for authorization;
-- their DMS product adds eSignature on top. Photos are the cheaper half of that protection and
-- cover the same dispute ("that scratch was already there"), so they come first. See
-- docs/bike-shop.md.
--
-- ONE table for both owners rather than two near-identical ones, because the read/write path,
-- the storage plumbing, and the per-owner cap are all the same. Two NULLABLE FKs with an
-- exactly-one CHECK, NOT a polymorphic (owner_kind, owner_id) pair: this keeps real referential
-- integrity and real ON DELETE CASCADE, which a polymorphic owner silently gives up.
--
-- Rentals need photos at BOTH ends: 'intake' when the gear goes out and 'return' when it comes
-- back, because a damage capture against the security deposit is exactly the moment someone will
-- want evidence. Work orders use 'intake' (and optionally 'progress' for a tech documenting what
-- they found mid-repair).

CREATE TABLE IF NOT EXISTS shop_condition_photo (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    work_order_id uuid        NULL REFERENCES shop_work_order(id) ON DELETE CASCADE,
    rental_id     uuid        NULL REFERENCES shop_rental(id)     ON DELETE CASCADE,
    -- intake   = condition when it arrived / went out (the baseline)
    -- return   = condition when rented gear came back (justifies a damage capture)
    -- progress = a tech documenting something found mid-repair
    stage         text        NOT NULL DEFAULT 'intake'
                              CHECK (stage IN ('intake', 'return', 'progress')),
    image_url     text        NOT NULL,
    caption       text        NULL,
    uploaded_by_user_id uuid  NULL REFERENCES users(id) ON DELETE SET NULL,
    sort_order    int         NOT NULL DEFAULT 100,
    created_at    timestamptz NOT NULL DEFAULT now(),
    -- Exactly one owner. Belt and braces against a row that belongs to everything or nothing.
    CONSTRAINT chk_shop_condition_photo_owner CHECK (
        (work_order_id IS NOT NULL AND rental_id IS NULL)
     OR (work_order_id IS NULL AND rental_id IS NOT NULL)
    )
);

-- Read paths: "show me this work order's / this rental's photos", newest group first.
CREATE INDEX IF NOT EXISTS idx_shop_condition_photo_wo
    ON shop_condition_photo (work_order_id, stage, sort_order) WHERE work_order_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_shop_condition_photo_rental
    ON shop_condition_photo (rental_id, stage, sort_order) WHERE rental_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_shop_condition_photo_tenant
    ON shop_condition_photo (tenant_id, created_at);
