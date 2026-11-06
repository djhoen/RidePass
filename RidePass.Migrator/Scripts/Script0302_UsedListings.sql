-- Used and ex-rental sales (docs/bike-shop.md). A shop that rents bikes sells last season's fleet
-- every year, and a parts wall often carries take-off or open-box gear. Two additions cover both
-- without changing how price, stock or the register already work.
--
-- shop_product.condition ('new' | 'used') is a badge and a filter for the storefront. The price
-- stays on the variant exactly as before: a used product is simply a product the buyer is told is
-- used. It defaults to 'new', so every existing product keeps reading the way it does today and no
-- backfill is needed.
--
-- shop_item_listing puts ONE specific serialized unit (a shop_item) on the sales floor at its own
-- asking price, with a condition grade, notes and its own photos. The unit moves to the new
-- shop_item status 'for_sale': it has left the rental fleet (availability counts and the rental
-- pickers only look at 'available') but it is not sold yet. The existing sale-complete path flips
-- it to 'sold' through the ordinary shop_sale_line.item_id, so a listing carries no status of its
-- own: ACTIVE is unlisted_at IS NULL with the unit in 'for_sale', SOLD is derived from the unit and
-- its sale line, and UNLISTED is unlisted_at set (the unit goes back to the status it was listed
-- from, prior_status: the fleet, the bench or retirement). A listing is one stay on the floor; unlisting closes the row and re-listing opens a new one, which is why the
-- uniqueness is partial on unlisted_at IS NULL and the history of past listings is kept.
--
-- reserved_sale_id / reserved_at hold the unit for an online checkout in progress. There is no
-- reservation status either: the reservation is live while the sale it points at is still pending
-- (or paid) and it was taken less than twenty minutes ago (Services.BikeShop.ListingRules decides, in
-- one place). A stale one is ignored on read, and it is only ever cleared by RELEASING it: its
-- PaymentIntent cancelled, its sale marked abandoned, its sale line made to let go of the unit.
-- Simply nulling the column is not enough, because uk_shop_sale_line_item would keep the unit from
-- ever being sold again through the dead sale's line.
--
-- Widening a CHECK never invalidates existing rows, so the shop_item status constraint is a plain
-- DROP+ADD. It is found by its definition rather than assumed by name: Script0181 declared it inline,
-- which Postgres names shop_item_status_check, but a hand-fixed database must not break the script.
-- Everything here is rerunnable: guarded ALTERs, IF NOT EXISTS on tables and indexes, DO-block
-- guards on constraints. Script number 0302 rather than 0301 because 0301 was taken by
-- Script0301_ShopReceipts.sql while this was being written.

-- ── Product condition badge ──────────────────────────────────────────────────
ALTER TABLE shop_product ADD COLUMN IF NOT EXISTS condition text NOT NULL DEFAULT 'new';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'shop_product_condition_check' AND conrelid = 'shop_product'::regclass
    ) THEN
        ALTER TABLE shop_product
            ADD CONSTRAINT shop_product_condition_check CHECK (condition IN ('new', 'used'));
    END IF;
END $$;

-- ── Unit status gains 'for_sale' ─────────────────────────────────────────────
DO $$
DECLARE
    v_conname text;
    v_def     text;
BEGIN
    IF to_regclass('public.shop_item') IS NULL THEN
        RETURN;
    END IF;

    -- The status CHECK, whatever it is called today: the one check on shop_item that names the
    -- rental state. Prefer the generated name when several match.
    SELECT conname, pg_get_constraintdef(oid) INTO v_conname, v_def
    FROM pg_constraint
    WHERE conrelid = 'shop_item'::regclass
      AND contype = 'c'
      AND pg_get_constraintdef(oid) LIKE '%rented_out%'
    ORDER BY (conname = 'shop_item_status_check') DESC, conname
    LIMIT 1;

    IF v_def IS NOT NULL AND v_def LIKE '%for_sale%' THEN
        RETURN;   -- already widened
    END IF;

    IF v_conname IS NOT NULL THEN
        EXECUTE format('ALTER TABLE shop_item DROP CONSTRAINT %I', v_conname);
    END IF;

    ALTER TABLE shop_item
        ADD CONSTRAINT shop_item_status_check
        CHECK (status IN ('available', 'rented_out', 'sold', 'maintenance', 'retired', 'for_sale'));
END $$;

-- ── Listings ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS shop_item_listing (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    item_id             uuid        NOT NULL REFERENCES shop_item(id) ON DELETE CASCADE,
    -- Defaults to "{product name} {variant label} · {unit label}"; the sale line's name snapshot.
    title               text        NOT NULL,
    asking_price_cents  int         NOT NULL CHECK (asking_price_cents > 0),
    condition_grade     text        NOT NULL CHECK (condition_grade IN ('excellent', 'good', 'fair')),
    condition_notes     text        NULL,
    -- false = counter only: rings at the register but never appears in the public store.
    published           boolean     NOT NULL DEFAULT true,
    listed_at           timestamptz NOT NULL DEFAULT now(),
    listed_by_user_id   uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    -- An online checkout in progress (see the header for when it counts as live).
    reserved_sale_id    uuid        NULL REFERENCES shop_sale(id) ON DELETE SET NULL,
    reserved_at         timestamptz NULL,
    unlisted_at         timestamptz NULL
);

-- Where the unit came from when it was listed ('available', 'maintenance' or 'retired'), so taking
-- it off the floor puts it back there: a retired bike that did not sell must not quietly rejoin the
-- rental fleet, and a bike pulled off the bench goes back to the bench. NULL (a row written before
-- the column existed) reads as 'available'. A separate guarded ALTER rather than a column in the
-- CREATE TABLE above, because dev databases ran an earlier cut of this unreleased script.
ALTER TABLE shop_item_listing ADD COLUMN IF NOT EXISTS prior_status text NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'shop_item_listing_prior_status_check' AND conrelid = 'shop_item_listing'::regclass
    ) THEN
        ALTER TABLE shop_item_listing
            ADD CONSTRAINT shop_item_listing_prior_status_check
            CHECK (prior_status IS NULL OR prior_status IN ('available', 'maintenance', 'retired'));
    END IF;
END $$;

-- One open listing per unit; closed ones stay as history.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_item_listing_active
    ON shop_item_listing (tenant_id, item_id) WHERE unlisted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_shop_item_listing_tenant
    ON shop_item_listing (tenant_id, published, unlisted_at);

-- ── Listing photos ───────────────────────────────────────────────────────────
-- A used bike is sold on its own photos (scratches and all), not the product's catalog shots;
-- the product image is only the fallback cover when a listing has none.
CREATE TABLE IF NOT EXISTS shop_listing_photo (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    listing_id  uuid        NOT NULL REFERENCES shop_item_listing(id) ON DELETE CASCADE,
    image_url   text        NOT NULL,
    caption     text        NULL,
    sort_order  int         NOT NULL DEFAULT 0,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_shop_listing_photo_listing
    ON shop_listing_photo (tenant_id, listing_id, sort_order);
