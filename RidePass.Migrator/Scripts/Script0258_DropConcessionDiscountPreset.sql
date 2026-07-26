-- Drops the F&B-only discount table, now that every counter reads the tenant-wide list.
--
-- concession_discount_preset was the first version of this idea: a list of named discounts a
-- cashier could apply, but only at the food counter. Script0251 generalised it into discount_preset
-- with a `surfaces` array, so one "Military 10%" can cover tickets, retail and food at once, and
-- backfilled every row from this table into that one keyed on (tenant_id, lower(name)).
--
-- This is the contract half of that expand-then-contract. It is safe now because nothing reads the
-- old table any more: the only two remaining mentions in the codebase are comments, one of which
-- says outright that it is dropped in a later migration. DbUp runs scripts in order, so the
-- Script0251 backfill has always already run by the time this does, and no tenant can lose a
-- discount it had configured.
--
-- Ordering note: the drop is deliberately in its own migration rather than tacked onto Script0251.
-- Had they shipped together, a deploy that applied the schema before the new code rolled out would
-- have left the running app reading a table that no longer existed.

DROP TABLE IF EXISTS concession_discount_preset;
