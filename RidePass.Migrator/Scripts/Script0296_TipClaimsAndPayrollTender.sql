-- F&B tips, second pass: settle every tip on exactly one turn-in, and let a payroll payout reach
-- the books.
--
-- Script0294 attributed a shift's tips by TIME: whatever the worker's paid sales said between the
-- session opening and the turn-in. That loses money at both edges. A tab that worker A opened and
-- then closed after A had already turned in lands on nobody, and a worker who never opened a
-- drawer re-reports the whole business day's tips on every shift review. So a tip is now CLAIMED:
-- the turn-in that settles it writes its id onto the sale (tip_turn_in_id), and "tips to settle"
-- is simply every paid, tipped sale of the worker's that no turn-in has claimed yet, with no lower
-- bound. A tab closed after the close-out is picked up by the next one, and nothing can be settled
-- twice. The partial index is the exact shape of that lookup, so the pool stays cheap to read
-- however many sales a tenant accumulates.
--
-- Sales already settled under the time-window rule are backfilled onto the turn-in whose session
-- window held them (the same attribution those turn-ins snapshotted into tips_earned_cents), so a
-- database where Script0294 has been exercised does not settle those tips a second time. Rows no
-- window held stay unclaimed, which is the new rule doing its job. Rerunnable: the UPDATE only
-- touches rows that are still unclaimed.
--
-- 'payroll' joins the ledger's payment methods: tips settled through payroll leave Tips payable
-- and sit in a wages-payable style account that the payroll run clears (JournalEntryBuilder posts
-- DR Tips payable / CR Tips moved to payroll). The row moves no cash, so the cash-only drawer
-- arithmetic in TenantLedgerRepository.SumCashNetForWorker never sees it.

ALTER TABLE concession_sale
    ADD COLUMN IF NOT EXISTS tip_turn_in_id uuid NULL REFERENCES cash_turn_in(id) ON DELETE RESTRICT;

CREATE INDEX IF NOT EXISTS idx_concession_sale_unclaimed_tips
    ON concession_sale (tenant_id, sold_by_user_id)
    WHERE status = 'paid' AND tip_turn_in_id IS NULL AND tip_cents > 0;

-- A claimed sale pins its turn-in. Deleting a turn-in that settled tips would silently hand those
-- tips back to the pool and settle them a second time on the next close-out, so the reference is
-- RESTRICT, never SET NULL. A database that took this script while it still said SET NULL (only
-- ridepass_dev) is corrected here: the FK on the column is found by what it constrains and
-- replaced when it does not restrict. No-op once it does.
DO $$
DECLARE lenient_fk text;
BEGIN
    SELECT c.conname INTO lenient_fk
    FROM pg_constraint c
    WHERE c.conrelid = 'concession_sale'::regclass
      AND c.contype = 'f'
      AND c.confdeltype <> 'r'
      AND c.conkey = ARRAY[(SELECT a.attnum FROM pg_attribute a
                            WHERE a.attrelid = 'concession_sale'::regclass AND a.attname = 'tip_turn_in_id')];
    IF lenient_fk IS NOT NULL THEN
        EXECUTE format('ALTER TABLE concession_sale DROP CONSTRAINT %I', lenient_fk);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'concession_sale'::regclass AND conname = 'concession_sale_tip_turn_in_id_fkey') THEN
        ALTER TABLE concession_sale ADD CONSTRAINT concession_sale_tip_turn_in_id_fkey
            FOREIGN KEY (tip_turn_in_id) REFERENCES cash_turn_in(id) ON DELETE RESTRICT;
    END IF;
END $$;

-- Backfill: tips the time-window rule already settled belong to that turn-in. The earliest
-- matching turn-in wins when a worker had an event session and a no-event session open at once.
UPDATE concession_sale cs
SET tip_turn_in_id = (
    SELECT t.id
    FROM cash_turn_in t
    JOIN cash_session s ON s.id = t.cash_session_id AND s.tenant_id = t.tenant_id
    WHERE t.tenant_id = cs.tenant_id
      AND t.worker_user_id = cs.sold_by_user_id
      AND COALESCE(cs.paid_at, cs.created_at) >= s.opened_at
      AND COALESCE(cs.paid_at, cs.created_at) <= t.submitted_at
    ORDER BY t.submitted_at
    LIMIT 1)
WHERE cs.tip_turn_in_id IS NULL
  AND cs.status = 'paid'
  AND cs.tip_cents > 0
  AND cs.sold_by_user_id IS NOT NULL
  AND EXISTS (
      SELECT 1
      FROM cash_turn_in t
      JOIN cash_session s ON s.id = t.cash_session_id AND s.tenant_id = t.tenant_id
      WHERE t.tenant_id = cs.tenant_id
        AND t.worker_user_id = cs.sold_by_user_id
        AND COALESCE(cs.paid_at, cs.created_at) >= s.opened_at
        AND COALESCE(cs.paid_at, cs.created_at) <= t.submitted_at);

-- 'payroll' as a ledger tender. The list is the one Script0195 left in place, plus the new value.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_payment_method_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_payment_method_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_payment_method_check
        CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits', 'stripe_direct', 'credit', 'payroll'));
END $$;
