-- F&B open tabs. A bar/restaurant tab is a concession_sale that is rung up in ROUNDS and paid
-- once at the end, so the design keeps a tab inside the existing sale row instead of inventing a
-- parallel table: a tab is a concession_sale in the new status 'open', its rounds are groups of
-- concession_sale_line rows tagged with a round number, and closing the tab moves the row to
-- 'paid' through the same cash / card-present / store-credit paths a counter sale uses. That is
-- what keeps tax, discounts, the ledger, reports and refunds untouched: every downstream query
-- already keys on status = 'paid', and an open tab is simply not paid yet. 'voided' is the
-- terminal state for a tab that was torn up (manager PIN + reason); its fired items are not
-- restocked because the kitchen already made them.
--
-- The optional card hold is a card-present PaymentIntent with capture_method = manual for the
-- tenant's configured hold amount. Only its id, amount and the card's brand / last4 / cardholder
-- name are stored, so the POS can label the tab ("Visa ••4242 · SMITH") and capture at close.
--
-- Everything here is additive: nullable columns, defaults on the NOT NULL ones so the deployed
-- app keeps inserting lines without naming them, and a DO-block guard around the status CHECK
-- (Postgres has no ADD CONSTRAINT IF NOT EXISTS). The guard matches on the constraint's current
-- definition so re-running is a no-op once 'open' is in the list.

ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS tab_name               text        NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS tab_opened_at          timestamptz NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS tab_closed_at          timestamptz NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS voided_at              timestamptz NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS void_reason            text        NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS voided_by_name         text        NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS hold_payment_intent_id text        NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS hold_amount_cents      int         NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS hold_card_brand        text        NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS hold_card_last4        text        NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS hold_cardholder_name   text        NULL;

-- Status CHECK gains 'open' and 'voided'. Script0263 last rewrote this constraint (to add
-- 'abandoned'); the same drop-and-re-add shape is used here, guarded on the definition so the
-- script is rerunnable.
DO $$
BEGIN
    IF to_regclass('public.concession_sale') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'concession_sale_status_check'
              AND conrelid = 'concession_sale'::regclass
              AND pg_get_constraintdef(oid) LIKE '%voided%'
        ) THEN
            ALTER TABLE concession_sale DROP CONSTRAINT IF EXISTS concession_sale_status_check;
            ALTER TABLE concession_sale
                ADD CONSTRAINT concession_sale_status_check
                CHECK (status = ANY (ARRAY['pending','paid','failed','refunded','abandoned','open','voided']));
        END IF;
    END IF;
END $$;

-- The POS polls the open-tab list every few seconds; a partial index over just the open rows
-- keeps that an index scan ordered by age, like idx_concession_sale_kitchen does for the KDS.
CREATE INDEX IF NOT EXISTS idx_concession_sale_open_tab
    ON concession_sale (tenant_id, tab_opened_at)
    WHERE status = 'open';

-- Rounds. Existing lines (and lines written by the deployed app, which never names these
-- columns) are round 1 fired when they were inserted. served_at is set when the kitchen marks
-- a tab round picked up; a line with served_at drops off the cook screen. Normal orders keep
-- using the sale-level Complete, so served_at stays NULL for them.
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS round_number int         NOT NULL DEFAULT 1;
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS fired_at     timestamptz NOT NULL DEFAULT now();
ALTER TABLE concession_sale_line ADD COLUMN IF NOT EXISTS served_at    timestamptz NULL;

-- Tenant settings: tabs on by default (the POS shows the Open tab action), hold defaults to $50.
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS tabs_enabled   boolean NOT NULL DEFAULT true;
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS tab_hold_cents int     NOT NULL DEFAULT 5000;
