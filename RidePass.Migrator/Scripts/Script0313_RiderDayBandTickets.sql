-- One band per rider per day across tickets and passes (item 10; Script0312 made the band).
--
-- A ticket rider is banded on the same rider_day_band as a pass holder: the band is keyed by the rider
-- (TicketRiderKeys / RiderPhotoRules), so a rider who wears a band today (from a pass or another ticket)
-- has every other admission that day tied to it, and a multi-day ticket is tied to each day's band through
-- rider_day_band_admission (one row per band, i.e. per business date).
--
-- event_wristband ticket rows are NOT backfilled: the rider key a band is keyed by is computed in C#
-- (IdCheckRules name normalization, dependents, rider accounts) and a SQL copy of that rule could split one
-- rider into two bands. Existing rows keep resolving, and keep their codes taken, through the shared
-- "code in use on a date" window (IRiderDayBands.CodeInUse) until their events are over.
--
-- Additive and rerunnable.

-- The ticket a band was issued through (the pass is season_pass_purchase_id), so a scan opens that order.
-- ON DELETE SET NULL: the band is the rider's, not the ticket's; it outlives a deleted ticket.
ALTER TABLE rider_day_band
    ADD COLUMN IF NOT EXISTS event_ticket_purchase_id uuid NULL REFERENCES event_ticket_purchase(id) ON DELETE SET NULL;
-- Review round (amended in place, unpushed): a database that ran the first version has this FK as CASCADE.
DO $$
DECLARE c text;
BEGIN
    SELECT conname INTO c FROM pg_constraint
     WHERE conrelid = 'rider_day_band'::regclass AND contype = 'f' AND confdeltype = 'c'
       AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'rider_day_band'::regclass
                                                       AND attname = 'event_ticket_purchase_id')];
    IF c IS NOT NULL THEN
        EXECUTE format('ALTER TABLE rider_day_band DROP CONSTRAINT %I', c);
        ALTER TABLE rider_day_band ADD CONSTRAINT rider_day_band_event_ticket_purchase_id_fkey
            FOREIGN KEY (event_ticket_purchase_id) REFERENCES event_ticket_purchase(id) ON DELETE SET NULL;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS idx_rider_day_band_ticket
    ON rider_day_band (event_ticket_purchase_id) WHERE event_ticket_purchase_id IS NOT NULL;

-- A ticket that leaves an admissible state (un-redeemed at the gate; or no longer paid or redeemed at all:
-- refunded, cancelled, ...) no longer counts toward any band; the band itself stays (the rider still wears it).
-- Every path, current and future.
CREATE OR REPLACE FUNCTION rider_day_band_unlink_ticket() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    DELETE FROM rider_day_band_admission WHERE event_ticket_purchase_id = NEW.id;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_rider_day_band_unlink_ticket ON event_ticket_purchase;
CREATE TRIGGER trg_rider_day_band_unlink_ticket
    AFTER UPDATE OF status ON event_ticket_purchase
    FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status
                       AND (OLD.status = 'redeemed' OR NEW.status NOT IN ('paid', 'redeemed')))
    EXECUTE FUNCTION rider_day_band_unlink_ticket();

-- A day's attendance removed from a multi-day ticket: that day's band no longer covers it.
CREATE OR REPLACE FUNCTION rider_day_band_unlink_attendance() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    DELETE FROM rider_day_band_admission a
     USING rider_day_band b
     WHERE a.band_id = b.id AND a.event_ticket_purchase_id = OLD.ticket_id AND b.business_date = OLD.on_date;
    RETURN OLD;
END $$;

DROP TRIGGER IF EXISTS trg_rider_day_band_unlink_attendance ON event_ticket_attendance;
CREATE TRIGGER trg_rider_day_band_unlink_attendance
    AFTER DELETE ON event_ticket_attendance
    FOR EACH ROW EXECUTE FUNCTION rider_day_band_unlink_attendance();

-- Review round (amended in place, unpushed):
-- * A reservation comes off its band on ANY change away from checked_in (replaces Script0312's trigger, which
--   fired only from checked_in), so a cancelled reservation never keeps a link.
-- * rider_day_band_admission.unlinked_at: "Unlink" at the gate keeps the row (unlinked) so a later band on the
--   same admission is recognised as a correction of the band that covered it; every reader ignores it.
ALTER TABLE rider_day_band_admission ADD COLUMN IF NOT EXISTS unlinked_at timestamptz NULL;

DROP TRIGGER IF EXISTS trg_rider_day_band_unlink_reservation ON season_pass_reservation;
CREATE TRIGGER trg_rider_day_band_unlink_reservation
    AFTER UPDATE OF status ON season_pass_reservation
    FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status AND NEW.status IS DISTINCT FROM 'checked_in')
    EXECUTE FUNCTION rider_day_band_unlink_reservation();
