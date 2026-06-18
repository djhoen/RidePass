-- Unified event checkout (payment first, registration after).
--
-- The new single-flow checkout takes payment up front, then collects per-ticket
-- "registration" afterward: the rider's identity + a signed waiver (when the event
-- requires one), plus race number / bike for race entries. Because a guest can buy
-- several entries for riders who are NOT accounts, each ticket has to carry its own
-- rider + waiver. Waivers were previously keyed by user_id (rider_waiver_signature);
-- those stay for account-based signing, but a per-ticket guest rider's signature is
-- captured on the ticket row itself.
--
-- event_ticket_purchase already backs both rider entries and gate/spectator passes
-- (by tier kind), so these columns cover spectator waivers too. race_number already
-- exists (Script0079); we add the rest.

ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS rider_first_name          text        NULL,
    ADD COLUMN IF NOT EXISTS rider_last_name           text        NULL,
    ADD COLUMN IF NOT EXISTS rider_birthdate           date        NULL,
    ADD COLUMN IF NOT EXISTS bike                      text        NULL,
    ADD COLUMN IF NOT EXISTS waiver_id                 uuid        NULL REFERENCES tenant_waiver(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS waiver_signed_at          timestamptz NULL,
    ADD COLUMN IF NOT EXISTS waiver_signature_data_url text        NULL,
    ADD COLUMN IF NOT EXISTS parent_guardian_name      text        NULL,   -- minor waivers
    -- A ticket is registration-complete once its rider identity + any required waiver
    -- are captured. Gate check-in reads this to flag incomplete entries.
    ADD COLUMN IF NOT EXISTS registration_complete     boolean     NOT NULL DEFAULT false;

-- Grandfather existing tickets: they predate the post-payment registration step and
-- already carried their waiver/rider via the old per-user flow, so mark them complete
-- instead of having them suddenly read as "unsigned" at the gate.
UPDATE event_ticket_purchase SET registration_complete = true WHERE registration_complete = false;
