-- Season pass benefits: one tenant-driven model for everything a pass grants.
--
-- What a pass "includes" was spread across places that didn't know about each other:
--   * events  — season_pass_event_type_perk (per product x event type, discount_percent).
--                Configured since Script0035 but never read at pricing time, so a holder
--                got no discount at checkout. The admin UI could only express 100% (a
--                checkbox), so "25% off race entry" wasn't sayable at all.
--   * F&B     — concession_menu_settings.season_pass_discount_* (Script0160). TENANT-WIDE:
--                one discount for every pass product, so Bronze and Platinum can't differ.
--   * rentals — nothing.
-- Adding more surfaces that way means N configs in N shapes, and a landing page that can't
-- describe a pass without special-casing each one. This table is the single source.
--
-- benefit_type is the surface; scope_id narrows within it (event_type_id for 'event',
-- NULL = every event type). discount_kind/value mirror the coupon table's convention so the
-- math and the admin vocabulary match what tenants already use:
--   'percent' -> discount_value is BPS (10000 = 100% = included free)
--   'amount'  -> discount_value is CENTS
--
-- 'concession' and 'rental' are permitted here so the surfaces can be wired without another
-- constraint rebuild, but NOTHING writes them yet and the admin editor doesn't offer them:
-- F&B still reads its own tenant-wide config, and letting a tenant set a per-product F&B
-- benefit the POS ignores would be worse than not offering it. Each gets switched over with
-- its read path in the same change. 'retail' arrives with the bike shop.

CREATE TABLE IF NOT EXISTS season_pass_benefit (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Denormalized from the product so every query can filter by tenant directly instead of
    -- joining through season_pass_product. ON DELETE CASCADE on both: a deleted product's
    -- benefits are meaningless, and so are a deleted tenant's.
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    pass_product_id uuid        NOT NULL REFERENCES season_pass_product(id) ON DELETE CASCADE,
    benefit_type    text        NOT NULL CHECK (benefit_type IN ('event','concession','rental','buddy_pass')),
    -- What the benefit narrows to within its surface. event -> tenant_event_type.id.
    -- NULL = the whole surface ("10% off all F&B", "included at every event type").
    -- Untyped uuid rather than an FK: the referent differs per benefit_type, so no single FK
    -- fits. Orphan cleanup for 'event' rides on the event-type delete below.
    scope_id        uuid        NULL,
    discount_kind   text        NOT NULL DEFAULT 'percent' CHECK (discount_kind IN ('percent','amount')),
    discount_value  int         NOT NULL DEFAULT 0 CHECK (discount_value >= 0),
    -- How many times a season this benefit can be used. NULL = unlimited (the normal case for
    -- a discount). Set for countable grants: "2 buddy passes per season".
    quantity        int         NULL CHECK (quantity IS NULL OR quantity > 0),
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

-- One benefit per product per surface per scope. Two "25% off race entry" rows on one product
-- would double-apply at checkout. COALESCE the NULL scope to a sentinel because Postgres
-- treats NULLs as distinct in a unique index, which would otherwise allow unlimited duplicate
-- whole-surface rows (exactly the ones most likely to be double-entered).
CREATE UNIQUE INDEX IF NOT EXISTS uk_season_pass_benefit_product_type_scope
    ON season_pass_benefit (pass_product_id, benefit_type, COALESCE(scope_id, '00000000-0000-0000-0000-000000000000'::uuid));

-- Checkout's hot path: "what does this product grant on this surface?"
CREATE INDEX IF NOT EXISTS idx_season_pass_benefit_lookup
    ON season_pass_benefit (tenant_id, pass_product_id, benefit_type);

DROP TRIGGER IF EXISTS trg_season_pass_benefit_updated_at ON season_pass_benefit;
CREATE TRIGGER trg_season_pass_benefit_updated_at
    BEFORE UPDATE ON season_pass_benefit
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Carry every configured event perk across. discount_percent (0-100) becomes bps, so the
-- existing "included" perks (100) land as 10000 = free entry, which is what they already meant.
--
-- season_pass_event_type_perk is deliberately LEFT IN PLACE (expand-then-contract): the
-- deployed app still reads it, and it's the rollback path if this goes wrong. It gets dropped
-- in a later migration once nothing references it. Until then this migration is the only
-- writer of the copy, so re-running must not duplicate — hence ON CONFLICT DO NOTHING against
-- the unique index above.
INSERT INTO season_pass_benefit (tenant_id, pass_product_id, benefit_type, scope_id, discount_kind, discount_value)
SELECT p.tenant_id, perk.pass_product_id, 'event', perk.event_type_id, 'percent', perk.discount_percent * 100
FROM season_pass_event_type_perk perk
JOIN season_pass_product p ON p.id = perk.pass_product_id
ON CONFLICT DO NOTHING;

-- Deleting an event type has to take its benefits with it. season_pass_event_type_perk got
-- this free from its FK; scope_id can't have one (its referent depends on benefit_type), so
-- the cascade is a trigger instead. Without it a deleted type leaves benefits pointing at
-- nothing, and checkout would price against a scope that no longer exists.
CREATE OR REPLACE FUNCTION delete_benefits_for_event_type() RETURNS trigger AS $$
BEGIN
    DELETE FROM season_pass_benefit
    WHERE benefit_type = 'event' AND scope_id = OLD.id;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_tenant_event_type_delete_benefits ON tenant_event_type;
CREATE TRIGGER trg_tenant_event_type_delete_benefits
    BEFORE DELETE ON tenant_event_type
    FOR EACH ROW EXECUTE FUNCTION delete_benefits_for_event_type();
