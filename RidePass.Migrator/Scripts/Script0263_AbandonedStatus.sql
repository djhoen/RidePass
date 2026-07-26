-- Splits the reconciler's forced 'failed' outcome into two honest states.
--
-- webapi/Workers/PendingPurchaseReconciler.cs ticks every 5 minutes and, after a 2 hour
-- AbandonCutoff, cancels the dangling Stripe PaymentIntent and finalizes the purchase as
-- 'failed'. The reconciler already knows this is an abandonment, not a decline (it logs exactly
-- that), but 'failed' has always been the only outcome the finalizer could write. In production
-- every 'failed' row died 2h00-2h05 after creation. Zero were real declines.
--
-- 'failed'    stays reserved for a genuine payment_intent.payment_failed webhook from Stripe
--             (PaymentController.cs), i.e. a payment attempt that actually happened and was
--             declined. That codepath is untouched by this migration.
-- 'abandoned' means no payment attempt ever completed and the intent is dead: either the
--             reconciler timed out a PaymentIntent, or checkout was abandoned before a
--             PaymentIntent even existed. This migration adds the value and backfills both
--             shapes from existing data. It does not touch reporting: everything that reads
--             these tables already filters status IN ('paid', 'redeemed') or similar allow-lists,
--             so 'abandoned' drops out of revenue automatically, the same way 'failed' already
--             does, and no ledger row is ever written for either.
--
-- Widening a CHECK never invalidates existing rows, so this is a plain DROP+ADD, no NOT VALID
-- dance needed. Five of these constraints were declared inline at CREATE TABLE and carry the
-- Postgres-generated name <table>_status_check; the idempotency guard (skip if 'abandoned' is
-- already in the constraint definition) makes this rerunnable.
--
-- shop_sale, shop_rental and package_purchase do not exist on every database (stage is missing
-- the bike shop tables), so each table's block is guarded by a to_regclass existence check
-- rather than assuming the table is there.

DO $$
BEGIN
    IF to_regclass('public.event_ticket_purchase') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'event_ticket_purchase_status_check'
              AND conrelid = 'event_ticket_purchase'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE event_ticket_purchase DROP CONSTRAINT IF EXISTS event_ticket_purchase_status_check;
            ALTER TABLE event_ticket_purchase
                ADD CONSTRAINT event_ticket_purchase_status_check
                CHECK (status = ANY (ARRAY['pending','paid','failed','refunded','redeemed','cancelled','abandoned']));
        END IF;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.event_extra_purchase') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'event_extra_purchase_status_check'
              AND conrelid = 'event_extra_purchase'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE event_extra_purchase DROP CONSTRAINT IF EXISTS event_extra_purchase_status_check;
            ALTER TABLE event_extra_purchase
                ADD CONSTRAINT event_extra_purchase_status_check
                CHECK (status = ANY (ARRAY['pending','paid','redeemed','cancelled','failed','refunded','abandoned']));
        END IF;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.season_pass_purchase') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'season_pass_purchase_status_check'
              AND conrelid = 'season_pass_purchase'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE season_pass_purchase DROP CONSTRAINT IF EXISTS season_pass_purchase_status_check;
            ALTER TABLE season_pass_purchase
                ADD CONSTRAINT season_pass_purchase_status_check
                CHECK (status = ANY (ARRAY['pending','paid','failed','cancelled','refunded','upgraded','abandoned']));
        END IF;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.membership_purchase') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'membership_purchase_status_check'
              AND conrelid = 'membership_purchase'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE membership_purchase DROP CONSTRAINT IF EXISTS membership_purchase_status_check;
            ALTER TABLE membership_purchase
                ADD CONSTRAINT membership_purchase_status_check
                CHECK (status = ANY (ARRAY['pending','paid','failed','cancelled','refunded','abandoned']));
        END IF;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.shop_sale') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'shop_sale_status_check'
              AND conrelid = 'shop_sale'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE shop_sale DROP CONSTRAINT IF EXISTS shop_sale_status_check;
            ALTER TABLE shop_sale
                ADD CONSTRAINT shop_sale_status_check
                CHECK (status = ANY (ARRAY['pending','paid','failed','refunded','abandoned']));
        END IF;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.shop_rental') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'shop_rental_status_check'
              AND conrelid = 'shop_rental'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE shop_rental DROP CONSTRAINT IF EXISTS shop_rental_status_check;
            ALTER TABLE shop_rental
                ADD CONSTRAINT shop_rental_status_check
                CHECK (status = ANY (ARRAY['pending','paid','out','returned','damaged','cancelled','failed','abandoned']));
        END IF;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.concession_sale') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'concession_sale_status_check'
              AND conrelid = 'concession_sale'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE concession_sale DROP CONSTRAINT IF EXISTS concession_sale_status_check;
            ALTER TABLE concession_sale
                ADD CONSTRAINT concession_sale_status_check
                CHECK (status = ANY (ARRAY['pending','paid','failed','refunded','abandoned']));
        END IF;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.package_purchase') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'package_purchase_status_check'
              AND conrelid = 'package_purchase'::regclass
              AND pg_get_constraintdef(oid) LIKE '%abandoned%'
        ) THEN
            ALTER TABLE package_purchase DROP CONSTRAINT IF EXISTS package_purchase_status_check;
            ALTER TABLE package_purchase
                ADD CONSTRAINT package_purchase_status_check
                CHECK (status = ANY (ARRAY['pending','paid','cancelled','failed','abandoned']));
        END IF;
    END IF;
END $$;

-- Backfill. Both branches only ever touch rows still sitting in the old status, so re-running
-- this script is a no-op the second time.
--
-- (a) The reconciler's exact 2h signature: a 'failed' row whose gap between created_at and
--     updated_at lands around the 2 hour AbandonCutoff (119 minutes to 3 hours, to cover the
--     5 minute tick interval plus worker jitter without reaching into the range a same-day
--     manual investigation or slow webhook retry could plausibly produce). Matches 15 rows in
--     production (12 event_ticket_purchase + 3 event_extra_purchase), zero of them suspected
--     declines. concession_sale and package_purchase have no updated_at column, so this branch
--     does not apply to them.
--
-- (b) A checkout abandoned before a PaymentIntent ever existed: still 'pending', no Stripe
--     intent id, and old enough that it is not just a customer mid-checkout right now. Matches
--     6 rows in production (4 event_ticket_purchase + 2 event_extra_purchase). package_purchase
--     stores this id under the column name payment_intent_id rather than
--     stripe_payment_intent_id.

DO $$
BEGIN
    IF to_regclass('public.event_ticket_purchase') IS NOT NULL THEN
        UPDATE event_ticket_purchase
        SET status = 'abandoned'
        WHERE status = 'failed'
          AND updated_at - created_at BETWEEN interval '119 minutes' AND interval '3 hours';

        UPDATE event_ticket_purchase
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND stripe_payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.event_extra_purchase') IS NOT NULL THEN
        UPDATE event_extra_purchase
        SET status = 'abandoned'
        WHERE status = 'failed'
          AND updated_at - created_at BETWEEN interval '119 minutes' AND interval '3 hours';

        UPDATE event_extra_purchase
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND stripe_payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.season_pass_purchase') IS NOT NULL THEN
        UPDATE season_pass_purchase
        SET status = 'abandoned'
        WHERE status = 'failed'
          AND updated_at - created_at BETWEEN interval '119 minutes' AND interval '3 hours';

        UPDATE season_pass_purchase
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND stripe_payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.membership_purchase') IS NOT NULL THEN
        UPDATE membership_purchase
        SET status = 'abandoned'
        WHERE status = 'failed'
          AND updated_at - created_at BETWEEN interval '119 minutes' AND interval '3 hours';

        UPDATE membership_purchase
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND stripe_payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.shop_sale') IS NOT NULL THEN
        UPDATE shop_sale
        SET status = 'abandoned'
        WHERE status = 'failed'
          AND updated_at - created_at BETWEEN interval '119 minutes' AND interval '3 hours';

        UPDATE shop_sale
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND stripe_payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.shop_rental') IS NOT NULL THEN
        UPDATE shop_rental
        SET status = 'abandoned'
        WHERE status = 'failed'
          AND updated_at - created_at BETWEEN interval '119 minutes' AND interval '3 hours';

        UPDATE shop_rental
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND stripe_payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;

-- concession_sale has no updated_at column (created_at + paid_at only), so the failed-row
-- signature in (a) does not apply here; only the pre-PaymentIntent abandonment in (b) does.
DO $$
BEGIN
    IF to_regclass('public.concession_sale') IS NOT NULL THEN
        UPDATE concession_sale
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND stripe_payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;

-- package_purchase has no updated_at column either, and its Stripe intent id column is named
-- payment_intent_id, not stripe_payment_intent_id.
DO $$
BEGIN
    IF to_regclass('public.package_purchase') IS NOT NULL THEN
        UPDATE package_purchase
        SET status = 'abandoned'
        WHERE status = 'pending'
          AND payment_intent_id IS NULL
          AND created_at < now() - interval '2 hours';
    END IF;
END $$;
