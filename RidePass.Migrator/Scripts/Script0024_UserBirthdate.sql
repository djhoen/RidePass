-- Birthdate is required for newly created riders. Existing users are left NULL —
-- the column is nullable so backfill happens organically (e.g. on first counter
-- visit) without breaking pre-existing accounts.

ALTER TABLE users
    ADD COLUMN birthdate date NULL;
