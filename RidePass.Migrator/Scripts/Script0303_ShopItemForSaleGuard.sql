-- A unit on the sales floor (shop_item.status 'for_sale', Script0302) is owned by its listing: only
-- unlisting takes it off the floor (back to the status it was listed from), and only a sale takes it
-- to 'sold'. Every other path that writes shop_item.status (the admin item editor, which defaults a
-- missing status to 'available'; anything that marks a unit rented out or out of service) would
-- otherwise pull a listed bike off the floor silently, stranding its listing and, worse, any online
-- checkout holding it. This trigger refuses that at the database, where no code path can forget.
--
-- The listing code itself moves units out of 'for_sale' legitimately. It says so by setting the
-- transaction-local setting ridepass.listing_change = 'on' (set_config(..., true)) inside the same
-- transaction, so the permission dies with the transaction and can never leak to the next user of a
-- pooled connection. 'for_sale' -> 'sold' (the sale-complete depletion) is always allowed, and so is
-- leaving the status unchanged. current_setting(..., true) returns NULL when the setting was never
-- defined and '' after a transaction that set it has ended, so both read as "not set".
--
-- The error carries its own SQLSTATE (RPL01) so the API can map it to a 409 without matching text.
-- Rerunnable: CREATE OR REPLACE FUNCTION, DROP TRIGGER IF EXISTS before CREATE TRIGGER. Additive:
-- no existing transition is refused except moving a listed unit, which nothing did before 0302.

CREATE OR REPLACE FUNCTION shop_item_guard_for_sale() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.status = 'for_sale'
       AND NEW.status NOT IN ('for_sale', 'sold')
       AND COALESCE(current_setting('ridepass.listing_change', true), '') <> 'on' THEN
        RAISE EXCEPTION 'This unit is listed for sale. Unlist it first.'
            USING ERRCODE = 'RPL01',
                  DETAIL = format('shop_item %s: %s -> %s', OLD.id, OLD.status, NEW.status);
    END IF;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_shop_item_guard_for_sale ON shop_item;
CREATE TRIGGER trg_shop_item_guard_for_sale
    BEFORE UPDATE ON shop_item
    FOR EACH ROW
    WHEN (OLD.status = 'for_sale' AND NEW.status IS DISTINCT FROM OLD.status)
    EXECUTE FUNCTION shop_item_guard_for_sale();
