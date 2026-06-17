-- LoamPassMx integration (RidePass side).
-- Super admin marks a track as a LoamPassMx track by setting its LoamMx destination id.
-- Per event type, the tenant chooses whether a Loam Pass credit is accepted for entry;
-- practice is always accepted (enforced in app code, not just data). A credit-paid admission
-- is recorded with the 'loampass_credits' payment method at $0 (the track is reimbursed off-platform).

-- Which LoamMx destination this RidePass tenant maps to. NULL = not a LoamPassMx track.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS loampass_mx_destination_id text NULL;

-- Per-event-type opt-in for Loam Pass redemption (practice forced on in app code).
ALTER TABLE tenant_event_type ADD COLUMN IF NOT EXISTS allow_loampass_redemption boolean NOT NULL DEFAULT false;
-- Tidy existing practice rows to reflect the always-on rule (enforcement is code-based regardless).
UPDATE tenant_event_type SET allow_loampass_redemption = true WHERE code = 'practice';

-- Allow 'loampass_credits' as a payment method everywhere a sale is recorded. Preserves the
-- existing values (stripe, cash, voucher, stripe_connect from Script0036) and adds the new one.
-- Note: day_pass_purchase was renamed to pass_purchase (Script0056/0057) but its
-- payment_method check constraint kept the original name; drop by both names to be safe.
ALTER TABLE tenant_ledger_entry   DROP CONSTRAINT IF EXISTS tenant_ledger_entry_payment_method_check;
ALTER TABLE pass_purchase         DROP CONSTRAINT IF EXISTS day_pass_purchase_payment_method_check;
ALTER TABLE pass_purchase         DROP CONSTRAINT IF EXISTS pass_purchase_payment_method_check;
ALTER TABLE event_ticket_purchase DROP CONSTRAINT IF EXISTS event_ticket_purchase_payment_method_check;
ALTER TABLE season_pass_purchase  DROP CONSTRAINT IF EXISTS season_pass_purchase_payment_method_check;

ALTER TABLE tenant_ledger_entry   ADD CONSTRAINT tenant_ledger_entry_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits'));
ALTER TABLE pass_purchase         ADD CONSTRAINT pass_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits'));
ALTER TABLE event_ticket_purchase ADD CONSTRAINT event_ticket_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits'));
ALTER TABLE season_pass_purchase  ADD CONSTRAINT season_pass_purchase_payment_method_check
    CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits'));
