-- The day_pass → pass refactor (Script0057) renamed every table and most columns,
-- but the dispute.day_pass_purchase_id column was missed. The C# DisputeRepository
-- queries `d.pass_purchase_id`, so admin Disputes / dashboard reads explode with
-- "column d.pass_purchase_id does not exist". Rename the column + named constraint
-- + index so everything lines up. Idempotent so re-runs after a half-applied retry
-- don't fail.

DO $$
BEGIN
    -- Rename the column itself (matches what DisputeRepository SELECTs).
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'dispute' AND column_name = 'day_pass_purchase_id'
    ) THEN
        ALTER TABLE dispute RENAME COLUMN day_pass_purchase_id TO pass_purchase_id;
    END IF;

    -- Drop+recreate the CHECK constraint that referenced the old column name.
    IF EXISTS (
        SELECT 1 FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        WHERE cls.relname = 'dispute'
          AND con.conname = 'dispute_exactly_one_source_check'
    ) THEN
        ALTER TABLE dispute DROP CONSTRAINT dispute_exactly_one_source_check;
    END IF;

    -- A couple of older databases may have an unnamed inline CHECK from Script0010.
    -- Sweep any CHECK on the table that mentions the old column.
    DECLARE
        cn text;
    BEGIN
        FOR cn IN
            SELECT con.conname FROM pg_constraint con
            JOIN pg_class cls ON cls.oid = con.conrelid
            WHERE cls.relname = 'dispute' AND con.contype = 'c'
              AND pg_get_constraintdef(con.oid) ILIKE '%day_pass_purchase_id%'
        LOOP
            EXECUTE format('ALTER TABLE dispute DROP CONSTRAINT %I', cn);
        END LOOP;
    END;
END $$;

-- Re-add the source-cardinality CHECK using the new column name. Two columns
-- mutually exclusive (or both null for unlinked Stripe disputes — Script0010 allowed
-- the both-null case to support that).
ALTER TABLE dispute
    ADD CONSTRAINT dispute_exactly_one_source_check CHECK (
        (pass_purchase_id IS NOT NULL AND event_ticket_purchase_id IS NULL) OR
        (pass_purchase_id IS NULL AND event_ticket_purchase_id IS NOT NULL) OR
        (pass_purchase_id IS NULL AND event_ticket_purchase_id IS NULL)
    );

-- Rename the FK index too if it exists, so EXPLAIN / pg_indexes reads cleanly.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_dispute_day_pass_purchase_id') THEN
        ALTER INDEX idx_dispute_day_pass_purchase_id RENAME TO idx_dispute_pass_purchase_id;
    END IF;
END $$;
