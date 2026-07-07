-- Link each event ticket to the rider_waiver_signature row captured for its rider, so the check-in
-- gate and the "who has signed" report read one normalized signature store regardless of how the
-- ticket was sold. Until now the two sale paths diverged: the counter wrote a user-based
-- rider_waiver_signature row, while the unified online checkout stored the signature inline on the
-- ticket (waiver_signature_data_url) and never wrote a signature row. Both now write a signature row
-- and point the ticket at it. NULL = no signature captured yet.
-- Additive and rerunnable; no backfill (grandfathered tickets predate per-rider signing).

ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS waiver_signature_id uuid NULL REFERENCES rider_waiver_signature(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_event_ticket_waiver_sig
    ON event_ticket_purchase (waiver_signature_id)
    WHERE waiver_signature_id IS NOT NULL;
