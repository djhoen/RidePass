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
ALTER TABLE rider_day_band
    ADD COLUMN IF NOT EXISTS event_ticket_purchase_id uuid NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE;
CREATE INDEX IF NOT EXISTS idx_rider_day_band_ticket
    ON rider_day_band (event_ticket_purchase_id) WHERE event_ticket_purchase_id IS NOT NULL;

-- A ticket that stops being redeemed (un-checked at the gate, refunded, cancelled) no longer counts toward
-- any band; the band itself stays (the rider still wears it). Every path, current and future.
CREATE OR REPLACE FUNCTION rider_day_band_unlink_ticket() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    DELETE FROM rider_day_band_admission WHERE event_ticket_purchase_id = NEW.id;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_rider_day_band_unlink_ticket ON event_ticket_purchase;
CREATE TRIGGER trg_rider_day_band_unlink_ticket
    AFTER UPDATE OF status ON event_ticket_purchase
    FOR EACH ROW WHEN (OLD.status = 'redeemed' AND NEW.status IS DISTINCT FROM 'redeemed')
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
