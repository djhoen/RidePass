-- Rider address — captured on the Buy Race Entry "Racer Info" step and used
-- on race-day rosters / receipts. All fields nullable since address is not
-- required for every customer (e.g. pure spectator-side guest checkouts go
-- through different flows that don't touch the user row).

ALTER TABLE users
    ADD COLUMN address_line  text NULL,
    ADD COLUMN address_line2 text NULL,
    ADD COLUMN city          text NULL,
    ADD COLUMN state         text NULL,
    ADD COLUMN postal_code   text NULL,
    ADD COLUMN country       text NULL DEFAULT 'US';
