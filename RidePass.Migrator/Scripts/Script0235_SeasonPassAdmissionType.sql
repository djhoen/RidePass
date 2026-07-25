-- Tracks split into two operating models for how a season pass gets someone onto the
-- hill, and until now the app only implemented one of them. Some tracks want a pass
-- holder to sign up for a specific event before the pass admits them, so the gate can
-- hold a roster and a capacity. Others (Highland Bike Park is the motivating case) just
-- open the lift: the rider shows up on any operating day, staff scan the pass, hand over
-- a wristband, and that is the whole transaction. This column is the tenant's answer to
-- which of those two the gate should enforce.
--
--   1 = event sign-up required. A pass holder must hold a reservation for a specific
--       event before the gate admits them. RedeemPassAtGate refuses a walk-up scan.
--   2 = walk-up. The pass alone admits on scan, whether or not a calendar event is
--       running that day.
--
-- The default is 2 on purpose. Every tenant in production today experiences walk-up
-- behavior already: RedeemPassAtGate redeems against whatever event is scheduled for the
-- tenant's local today, with no reservation requirement of its own. Defaulting to 2
-- therefore changes nobody's observed behavior when this script runs, and mode 1 becomes
-- a deliberate opt-in from Admin, Settings, Features once the enforcement code ships.
-- Between migrating and deploying, the running app never reads the column at all.
--
-- Not to be confused with require_reservation_for_passes (Script0005), which sounds
-- adjacent but is not: that flag decides whether a rider must book a slot when BUYING a
-- day pass product. This one decides what happens at the GATE when an already-purchased
-- season pass is scanned. Nothing reads the two together.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS season_pass_admission_type_id int NOT NULL DEFAULT 2;

-- Guarded by name rather than IF NOT EXISTS, which ADD CONSTRAINT does not support.
-- Existing rows all land on the DEFAULT of 2, so the constraint validates clean.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_season_pass_admission_type'
    ) THEN
        ALTER TABLE tenant
            ADD CONSTRAINT chk_tenant_season_pass_admission_type
            CHECK (season_pass_admission_type_id IN (1, 2));
    END IF;
END $$;
