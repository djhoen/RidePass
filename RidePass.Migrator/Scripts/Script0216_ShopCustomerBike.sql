-- The customer's bike as a real record, not a sentence.
--
-- Today a work order carries customer_bike_desc: free text like "2022 Trek Fuel EX 8, black".
-- That makes the most useful question in a service department unanswerable — "has this bike been
-- in before, and what did we do to it?" — and it blocks everything downstream: warranty lookup by
-- serial, recall matching by model, and a multi-point inspection that accrues history per bike.
-- Ascend keys its whole service module off the serial for exactly this reason.
--
-- Ownership is deliberately loose. A bike can belong to a user account OR to a walk-in name and
-- phone, because work orders already accept walk-ins with no account. Tightening this to
-- customer_user_id would make the record useless for most counter traffic.

CREATE TABLE IF NOT EXISTS shop_customer_bike (
    id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,

    -- Owner: an account when we have one, otherwise the walk-in details from the ticket.
    customer_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    customer_name     text        NULL,
    customer_phone    text        NULL,

    -- The identity. Nullable because plenty of older bikes arrive with an unreadable or missing
    -- serial, and refusing the record then would push staff straight back to free text.
    serial            text        NULL,

    brand             text        NULL,
    model             text        NULL,
    model_year        int         NULL CHECK (model_year IS NULL OR (model_year BETWEEN 1900 AND 2100)),
    color             text        NULL,
    size              text        NULL,
    notes             text        NULL,

    -- Set when this is a bike WE sold: the serialized unit it left the shop as. Gives warranty
    -- context and lets intake auto-fill from the sale instead of retyping.
    sold_item_id      uuid        NULL REFERENCES shop_item(id) ON DELETE SET NULL,

    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);

-- A serial identifies one physical bike, so it resolves to one record per tenant. This is what
-- makes "this bike has been here before" work: intake looks the serial up and finds the existing
-- bike (updating the owner if it has been sold on) rather than minting a duplicate.
-- Case-insensitive and partial, so serial-less bikes don't collide with each other.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_customer_bike_serial
    ON shop_customer_bike (tenant_id, lower(serial)) WHERE serial IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_shop_customer_bike_owner
    ON shop_customer_bike (tenant_id, customer_user_id) WHERE customer_user_id IS NOT NULL;
-- Walk-in lookup by phone, which is how a counter finds a returning customer with no account.
CREATE INDEX IF NOT EXISTS idx_shop_customer_bike_phone
    ON shop_customer_bike (tenant_id, customer_phone) WHERE customer_phone IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_shop_customer_bike_sold_item
    ON shop_customer_bike (sold_item_id) WHERE sold_item_id IS NOT NULL;

DROP TRIGGER IF EXISTS trg_shop_customer_bike_updated_at ON shop_customer_bike;
CREATE TRIGGER trg_shop_customer_bike_updated_at BEFORE UPDATE ON shop_customer_bike
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- Link the work order to the bike. SET NULL rather than CASCADE: deleting a bike record must never
-- take repair history with it.
ALTER TABLE shop_work_order
    ADD COLUMN IF NOT EXISTS customer_bike_id uuid NULL
        REFERENCES shop_customer_bike(id) ON DELETE SET NULL;

-- Service history hot path: "every job on this bike, newest first".
CREATE INDEX IF NOT EXISTS idx_shop_work_order_bike
    ON shop_work_order (customer_bike_id, created_at DESC) WHERE customer_bike_id IS NOT NULL;

-- customer_bike_desc is KEPT. Existing work orders still read from it, and it stays the fallback
-- for a bike nobody has bothered to formalise. New tickets prefer the linked record.
COMMENT ON COLUMN shop_work_order.customer_bike_desc IS
    'Legacy/fallback free-text bike description. Prefer customer_bike_id; this remains for historical rows and quick unstructured intake.';
