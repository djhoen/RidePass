-- Staff codes on the POS, tips decoupled from the drawer, "paid later" mode, and the tip jar.
--
-- Highland runs one shared register. Each person punches a short code before ringing an order, so
-- the tip on that order belongs to whoever punched in rather than to whichever account happens to
-- be signed in on the terminal. At the end of the shift each person does their own shift report,
-- and card tips are NOT taken out of the drawer: a manager pays them later. The drawer itself is
-- counted once, at close. Script0294/0296 tied a person's tip settlement to a cash turn-in, which
-- only fits when every person has their own drawer. So the unit of "a person's shift report" is
-- now its own row, tip_shift, and a turn-in goes back to being a blind count of a drawer and
-- nothing else.
--
-- tip_shift is what a shift close writes: who (user_id), which terminal account closed it, the
-- drawer the tips came out of when they did (cash_session_id, drawer_at_close mode only), the
-- business date, and the same snapshot figures the turn-in used to carry. A tip is CLAIMED by
-- exactly one tip_shift (concession_sale.tip_shift_id replaces tip_turn_in_id) and a drawer
-- payout points at the shift it was taken on (tip_payout.tip_shift_id). What the house owes a
-- worker is still derived, never stored:
--
--     SUM(tip_shift.house_owes_cents) - SUM(tip_payout.amount_cents WHERE source IN ('cash', 'payroll'))
--
-- Settlement history already written against cash_turn_in is MOVED, not re-derived: every
-- turn-in that carried tips (or claimed a sale) gets a tip_shift carrying its figures, and the
-- sales and payouts that pointed at the turn-in are re-pointed at the shift. The new tip_shift
-- takes the turn-in's own id, which is what lets the sale and payout re-pointing be a single
-- UPDATE each without a correlation table, and cash_turn_in.tip_shift_id records that the move
-- happened so a rerun is a no-op. tips_earned / house_owes stay on the moved turn-ins as history
-- (no longer written; new rows carry 0), but tips_from_drawer_cents is ZEROED on them: the
-- reconciliation subtracts a drawer's tip-outs from tip_shift now, and a figure left on the
-- turn-in as well would be subtracted twice.
--
-- Expand, do not contract: concession_sale.tip_turn_in_id and its partial index are left in
-- place, unread by the new build, so the build that still reads them keeps working during the
-- deploy. The new claim predicate gets its own index. See the note at the end for the drop.
--
-- concession_sale.rung_by_terminal_user_id keeps the signed-in terminal account on every sale,
-- so an audit can always tell the terminal from the cashier who punched in.
--
-- concession_menu_settings gains the two switches: staff_code_mode (off | per_shift | per_order)
-- and tips_payout_mode (manager_later | drawer_at_close). The defaults keep every existing
-- tenant exactly where it was on the register (no code prompt) and move tip payout to
-- "later by a manager", which is what the shift report means from now on.
--
-- The tip jar is cash outside the register that the staff split and take immediately after the
-- shift; today it lives on a paper sheet. tip_jar_split / tip_jar_share record that split per
-- business date so the daily tips sheet is complete. No ledger row and no liability: the jar is
-- money the staff already took.

-- ── Settings ───────────────────────────────────────────────────────────────────────────────────

ALTER TABLE concession_menu_settings
    ADD COLUMN IF NOT EXISTS staff_code_mode text NOT NULL DEFAULT 'off'
        CHECK (staff_code_mode IN ('off', 'per_shift', 'per_order'));
ALTER TABLE concession_menu_settings
    ADD COLUMN IF NOT EXISTS tips_payout_mode text NOT NULL DEFAULT 'manager_later'
        CHECK (tips_payout_mode IN ('manager_later', 'drawer_at_close'));

-- ── A person's shift report ────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS tip_shift (
    id                       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id                  uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,          -- the person
    closed_by_user_id        uuid        NULL REFERENCES users(id) ON DELETE SET NULL,             -- terminal account
    cash_session_id          uuid        NULL REFERENCES cash_session(id) ON DELETE SET NULL,      -- drawer the tips came out of (drawer_at_close only)
    business_date            date        NOT NULL,
    closed_at                timestamptz NOT NULL DEFAULT now(),
    tips_earned_cents        int         NOT NULL DEFAULT 0 CHECK (tips_earned_cents >= 0),
    card_tips_cents          int         NOT NULL DEFAULT 0 CHECK (card_tips_cents >= 0),
    cash_tips_declared_cents int         NOT NULL DEFAULT 0 CHECK (cash_tips_declared_cents >= 0),
    tips_from_drawer_cents   int         NOT NULL DEFAULT 0 CHECK (tips_from_drawer_cents >= 0),
    house_owes_cents         int         NOT NULL DEFAULT 0 CHECK (house_owes_cents >= 0),
    note                     text        NULL
);
CREATE INDEX IF NOT EXISTS idx_tip_shift_tenant_user ON tip_shift (tenant_id, user_id, closed_at DESC);
CREATE INDEX IF NOT EXISTS idx_tip_shift_tenant_date ON tip_shift (tenant_id, business_date);
-- The reconciliation sums tips_from_drawer_cents per drawer.
CREATE INDEX IF NOT EXISTS idx_tip_shift_session ON tip_shift (tenant_id, cash_session_id) WHERE cash_session_id IS NOT NULL;

-- Idempotent close: the register mints a fresh uuid each time the close dialog opens and sends
-- it with the submit. A retry after a timeout carries the same id, finds the shift the first
-- submit wrote, and gets it back without claiming or writing anything. Unique per tenant so the
-- database itself refuses a second row for one submit; NULL for closes made without an id.
ALTER TABLE tip_shift ADD COLUMN IF NOT EXISTS client_request_id uuid NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_tip_shift_client_request
    ON tip_shift (tenant_id, client_request_id) WHERE client_request_id IS NOT NULL;

-- A claimed sale pins its shift (RESTRICT, as 0296 did for the turn-in): deleting a shift that
-- settled tips would hand them back to the pool and settle them twice.
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS tip_shift_id uuid NULL REFERENCES tip_shift(id) ON DELETE RESTRICT;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS rung_by_terminal_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE tip_payout ADD COLUMN IF NOT EXISTS tip_shift_id uuid NULL REFERENCES tip_shift(id) ON DELETE SET NULL;
-- The move marker: set on every turn-in whose tip history has been copied to a tip_shift.
ALTER TABLE cash_turn_in ADD COLUMN IF NOT EXISTS tip_shift_id uuid NULL REFERENCES tip_shift(id) ON DELETE SET NULL;

-- ── Move the settlement history off cash_turn_in ───────────────────────────────────────────────
-- Guarded on tip_turn_in_id still existing: once it is dropped below there is nothing left to
-- move, and the statements inside would not even parse. Inside, every statement is bounded by
-- the marker, so a partial earlier run completes rather than duplicating.

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'concession_sale' AND column_name = 'tip_turn_in_id') THEN

        -- One tip_shift per turn-in that settled anything, carrying the turn-in's own id and
        -- figures; the business date is the turn-in's local date at the tenant.
        INSERT INTO tip_shift (id, tenant_id, user_id, closed_by_user_id, cash_session_id, business_date, closed_at,
                               tips_earned_cents, card_tips_cents, cash_tips_declared_cents,
                               tips_from_drawer_cents, house_owes_cents)
        SELECT t.id, t.tenant_id, t.worker_user_id, t.worker_user_id, t.cash_session_id,
               (t.submitted_at AT TIME ZONE COALESCE(NULLIF(tn.timezone, ''), 'UTC'))::date,
               t.submitted_at,
               t.tips_earned_cents, t.card_tips_cents, t.cash_tips_declared_cents,
               t.tips_from_drawer_cents, t.house_owes_cents
        FROM cash_turn_in t
        JOIN tenant tn ON tn.id = t.tenant_id
        WHERE t.tip_shift_id IS NULL
          AND (t.tips_earned_cents > 0 OR t.house_owes_cents > 0 OR t.tips_from_drawer_cents > 0
               OR EXISTS (SELECT 1 FROM concession_sale cs WHERE cs.tenant_id = t.tenant_id AND cs.tip_turn_in_id = t.id))
        ON CONFLICT (id) DO NOTHING;

        -- Sales the turn-in claimed now belong to the shift with the same id.
        UPDATE concession_sale cs
        SET tip_shift_id = cs.tip_turn_in_id
        FROM cash_turn_in t
        WHERE t.id = cs.tip_turn_in_id
          AND t.tenant_id = cs.tenant_id
          AND t.tip_shift_id IS NULL
          AND cs.tip_shift_id IS NULL
          AND EXISTS (SELECT 1 FROM tip_shift s WHERE s.id = cs.tip_turn_in_id AND s.tenant_id = cs.tenant_id);

        -- Drawer payouts taken on the turn-in point at the shift too.
        UPDATE tip_payout p
        SET tip_shift_id = p.cash_turn_in_id
        FROM cash_turn_in t
        WHERE t.id = p.cash_turn_in_id
          AND t.tenant_id = p.tenant_id
          AND t.tip_shift_id IS NULL
          AND p.tip_shift_id IS NULL
          AND EXISTS (SELECT 1 FROM tip_shift s WHERE s.id = p.cash_turn_in_id AND s.tenant_id = p.tenant_id);

        -- Mark the turn-ins whose history has moved. Last, so the two re-pointing UPDATEs above
        -- can still see which turn-ins this run is moving.
        UPDATE cash_turn_in t
        SET tip_shift_id = t.id
        WHERE t.tip_shift_id IS NULL
          AND EXISTS (SELECT 1 FROM tip_shift s WHERE s.id = t.id AND s.tenant_id = t.tenant_id);
    END IF;
END $$;

-- The drawer tip-out now lives on the shift (tip_shift.tips_from_drawer_cents, keyed by
-- cash_session_id) and that is the only place the reconciliation reads it from. A moved turn-in
-- must not carry the figure as well. Outside the guard above on purpose: it keys on the marker,
-- not on the old column, so it also corrects a database that took an earlier draft of this
-- script. Rerunnable: once zeroed, nothing matches.
UPDATE cash_turn_in
SET tips_from_drawer_cents = 0
WHERE tip_shift_id IS NOT NULL
  AND tips_from_drawer_cents <> 0;

-- ── The claim predicate's index ────────────────────────────────────────────────────────────────
-- Its own name: idx_concession_sale_unclaimed_tips (on tip_turn_in_id) stays for the old build
-- until the column goes. CashRepository.UnclaimedTipPredicate matches this one exactly.

CREATE INDEX IF NOT EXISTS idx_concession_sale_unsettled_tips
    ON concession_sale (tenant_id, sold_by_user_id)
    WHERE status = 'paid' AND tip_shift_id IS NULL AND tip_cents > 0;

-- Only a database that took the pre-review draft of this script (ridepass_dev) carries the OLD
-- index name with the NEW predicate; that duplicate goes so dev and production end up alike. On
-- production the old-named index still says tip_turn_in_id and is left alone.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_indexes
               WHERE schemaname = 'public' AND indexname = 'idx_concession_sale_unclaimed_tips'
                 AND indexdef LIKE '%tip_shift_id%') THEN
        DROP INDEX idx_concession_sale_unclaimed_tips;
    END IF;
END $$;

-- ── Tip jar ────────────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS tip_jar_split (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    business_date      date        NOT NULL,
    total_cents        int         NOT NULL CHECK (total_cents > 0),
    note               text        NULL,
    created_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS tip_jar_share (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id    uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    split_id     uuid NOT NULL REFERENCES tip_jar_split(id) ON DELETE CASCADE,
    user_id      uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    amount_cents int  NOT NULL CHECK (amount_cents >= 0)
);
CREATE INDEX IF NOT EXISTS idx_tip_jar_split_tenant_date ON tip_jar_split (tenant_id, business_date);
CREATE INDEX IF NOT EXISTS idx_tip_jar_share_tenant_user ON tip_jar_share (tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_tip_jar_share_split ON tip_jar_share (split_id);

-- ── Contract step, for a later script ──────────────────────────────────────────────────────────
-- concession_sale.tip_turn_in_id and idx_concession_sale_unclaimed_tips are no longer read by
-- any code at or after this script, but the build that reads them (the Script0294-0297 tips
-- release) may still be serving traffic while this one rolls out. A later script (Script0300 or
-- after; 0299 is already taken), once that build is gone from every environment, runs:
--
--     DROP INDEX IF EXISTS idx_concession_sale_unclaimed_tips;
--     ALTER TABLE concession_sale DROP COLUMN IF EXISTS tip_turn_in_id;
--
-- Nothing here depends on the column; a database where it is already gone takes this script
-- unchanged (the data move is guarded on the column's existence).
