-- Tips claims cutover: settling tips through turn-ins starts at this instant.
--
-- Script0296 made "tips to settle" every paid, tipped sale no turn-in has claimed, with no lower
-- bound, so a tab closed after a worker's last close-out is picked up by their next one. Read
-- against a database that already holds a history of tipped sales, that same rule would hand each
-- worker their entire past as "unclaimed" on their first close-out after the deploy, and record
-- all of it as house-owes, when every one of those tips was in fact paid out by hand before
-- RidePass tracked them. So each tenant carries the instant their claims began. Everything tipped
-- before tips_claims_from_utc was handled outside RidePass and must never be claimed, counted on
-- a shift review, or shown as unclaimed on Tips owed; CashRepository applies the bound in the one
-- predicate all three readers share.
--
-- Existing tenants are stamped with now() exactly once, when the column is first created, and
-- that is why the stamp lives inside the same guard as the ADD COLUMN rather than behind a plain
-- "WHERE ... IS NULL": a tenant created after this script has NULL, which the readers treat as
-- unbounded, and that is correct because all of their tips are post-feature. A re-run must not
-- turn those NULLs into a cutover that would hide real, unsettled tips. Rerunnable: the guard
-- makes the whole block a no-op once the column exists.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'tenant' AND column_name = 'tips_claims_from_utc'
    ) THEN
        ALTER TABLE tenant ADD COLUMN IF NOT EXISTS tips_claims_from_utc timestamptz NULL;
        -- EXECUTE so the statement is planned after the column exists in this same transaction.
        EXECUTE 'UPDATE tenant SET tips_claims_from_utc = now() WHERE tips_claims_from_utc IS NULL';
    END IF;
END $$;
