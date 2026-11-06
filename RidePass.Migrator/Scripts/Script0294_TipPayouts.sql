-- F&B tips: shift close, tip-out from the drawer, house-owes, manager payout.
--
-- Staff are tipped on the sale (concession_sale.tip_cents, attributed to sold_by_user_id) and are
-- paid those tips IN CASH FROM THE DRAWER when they close out, the way Toast's Shift Review and
-- Square's Cash Out work. The turn-in (Script0131) is already the per-worker close, so the shift's
-- tip numbers are snapshotted onto it: tips earned this shift (card / cash split kept for the
-- review screen), cash tips the worker declares they received outside the register, how much they
-- took out of the drawer, and the shortfall the house still owes them when the drawer could not
-- cover it. Snapshotted rather than recomputed because a later refund or re-ring must not rewrite
-- what a worker was actually handed at the end of a shift.
--
-- tip_payout is the payout log: one row per time tips were handed to a staff member. 'drawer' rows
-- are written at shift close (the worker took the cash out of their own drawer, paid_by is the
-- worker); 'cash' and 'payroll' rows are a manager settling house-owes afterwards. What a worker is
-- still owed is DERIVED, never stored:
--
--     SUM(cash_turn_in.house_owes_cents) - SUM(tip_payout.amount_cents WHERE source IN ('cash', 'payroll'))
--
-- so there is no balance column that can drift from its own history.
--
-- Every cash payout (drawer or manager) also writes a tenant_ledger_entry with entry_kind
-- 'tip_payout': cash leaving a drawer against Tips Payable, negative gross, net 0, so the
-- journal (JournalEntryBuilder.AccrueSale) posts DR Tips payable / CR Cash on hand without a
-- special case. A drawer's expected cash is not read off that row: the turn-in's own
-- tips_from_drawer_cents is what left the drawer, and the reconciliation subtracts it directly.
-- Payroll payouts get a ledger row too, under the 'payroll' tender Script0296 adds. Both CHECKs
-- are widened below in the guarded form Script0179 onwards uses; the kind lists are the ones
-- Script0106 (entry_kind) and Script0195 (source_kind) left in place, plus 'tip_payout'.

-- Shift-close tip fields on the turn-in (Toast "shift review" numbers, snapshotted at close).
ALTER TABLE cash_turn_in ADD COLUMN IF NOT EXISTS tips_earned_cents        int NOT NULL DEFAULT 0 CHECK (tips_earned_cents >= 0);
ALTER TABLE cash_turn_in ADD COLUMN IF NOT EXISTS card_tips_cents          int NOT NULL DEFAULT 0 CHECK (card_tips_cents >= 0);
ALTER TABLE cash_turn_in ADD COLUMN IF NOT EXISTS cash_tips_declared_cents int NOT NULL DEFAULT 0 CHECK (cash_tips_declared_cents >= 0);
ALTER TABLE cash_turn_in ADD COLUMN IF NOT EXISTS tips_from_drawer_cents   int NOT NULL DEFAULT 0 CHECK (tips_from_drawer_cents >= 0);
ALTER TABLE cash_turn_in ADD COLUMN IF NOT EXISTS house_owes_cents         int NOT NULL DEFAULT 0 CHECK (house_owes_cents >= 0);

-- Every payment of tips to a staff member. 'drawer' rows are written at shift close
-- (the worker took the cash out of their own drawer); 'cash' and 'payroll' rows are a
-- manager settling house-owes later.
CREATE TABLE IF NOT EXISTS tip_payout (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id          uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,   -- recipient
    cash_turn_in_id  uuid        NULL REFERENCES cash_turn_in(id) ON DELETE SET NULL,
    amount_cents     int         NOT NULL CHECK (amount_cents > 0),
    source           text        NOT NULL CHECK (source IN ('drawer', 'cash', 'payroll')),
    paid_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,      -- worker (drawer) or manager; null once that account is removed: the payout is the record of money handed over and must outlive the payer
    ledger_entry_id  uuid        NULL REFERENCES tenant_ledger_entry(id) ON DELETE SET NULL,
    note             text        NULL,
    created_at       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_tip_payout_tenant_user ON tip_payout (tenant_id, user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tip_payout_tenant_time ON tip_payout (tenant_id, created_at DESC);

-- Ledger: a tip payout is cash leaving the drawer against Tips Payable.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_entry_kind_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_entry_kind_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_entry_kind_check
        CHECK (entry_kind IN (
            'sale', 'refund', 'dispute_loss', 'dispute_fee', 'adjustment', 'sms_charge', 'email_charge',
            'tip_payout'
        ));
END $$;

-- The payout row points at its tip_payout through (source_kind, source_id), the same way every
-- other ledger row points at what it books.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_source_kind_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
        CHECK (source_kind IS NULL OR source_kind IN (
            'pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras',
            'concession', 'tenant_billing_event', 'email_campaign', 'rental_deposit',
            'shop_sale', 'shop_rental', 'shop_rental_deposit', 'shop_wo_deposit', 'credit_tender',
            'tip_payout'
        ));
END $$;
