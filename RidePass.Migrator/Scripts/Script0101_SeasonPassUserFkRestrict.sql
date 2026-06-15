-- season_pass_purchase.purchaser_user_id was ON DELETE CASCADE, the odd one out:
-- deleting a rider would silently destroy their paid season passes (and cascade the
-- attached reservations). Every sibling purchase table uses RESTRICT (day pass,
-- event ticket) or SET NULL (rentals, extras). Switch it to RESTRICT so a rider with
-- paid season passes can't be hard-deleted out from under their purchase history.
DO $$
DECLARE cname text;
BEGIN
    SELECT con.conname INTO cname
    FROM pg_constraint con
    JOIN pg_attribute att
      ON att.attrelid = con.conrelid AND att.attnum = ANY (con.conkey)
    WHERE con.conrelid = 'season_pass_purchase'::regclass
      AND con.contype = 'f'
      AND att.attname = 'purchaser_user_id';
    IF cname IS NOT NULL THEN
        EXECUTE format('ALTER TABLE season_pass_purchase DROP CONSTRAINT %I', cname);
    END IF;
END $$;

ALTER TABLE season_pass_purchase
    ADD CONSTRAINT season_pass_purchase_purchaser_user_id_fkey
    FOREIGN KEY (purchaser_user_id) REFERENCES users(id) ON DELETE RESTRICT;
