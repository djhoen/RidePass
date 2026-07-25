-- Script0189 gave the gate wristbands, but only for event tickets: event_wristband.ticket_id
-- is a NOT NULL FK to event_ticket_purchase. That leaves the season pass holder, who is
-- exactly the rider a walk-up track most wants to band, with no way to wear one. Scan the
-- pass, admit the rider, and then there is nothing to link the band to, because a pass
-- admission writes a season_pass_reservation and not a ticket.
--
-- This migration widens the anchor. A band now points at ONE admission, which is either an
-- event ticket (as before) or a season pass admission, meaning a season_pass_reservation row
-- in status checked_in. Anchoring to the admission rather than to the pass purchase is
-- deliberate: it scopes the band to a single day or event, the same way a ticket-anchored
-- band is scoped, instead of making one band mean a whole season.
--
-- A pass-linked band inherits its scope from the admission that issued it. When an event was
-- running, the band carries that event_id and behaves identically to a ticket band. When the
-- rider walked in on a day with nothing on the calendar (the Script0236 no-event path), there
-- is no event to scope by, so the band carries valid_on_date instead.
--
-- Additive and idempotent throughout. Depends on Script0189 for the table and on Script0236
-- for season_pass_reservation.check_in_date; it does not touch season_pass_reservation itself.

-- A band no longer requires a ticket, since a pass admission can anchor it instead. Dropping
-- a NOT NULL that is already dropped is a silent no-op in Postgres, so this is rerunnable.
ALTER TABLE event_wristband ALTER COLUMN ticket_id DROP NOT NULL;

-- A band no longer requires an event either, because a no-event walk-up admission has none.
ALTER TABLE event_wristband ALTER COLUMN event_id DROP NOT NULL;

-- The new anchor: which pass admission this band means.
ALTER TABLE event_wristband
    ADD COLUMN IF NOT EXISTS season_pass_reservation_id uuid NULL
        REFERENCES season_pass_reservation(id) ON DELETE CASCADE;

-- The tenant-local admission date, copied from the reservation's check_in_date when the band
-- is linked. NULL on every event-anchored row, ticket rows and event-day pass rows alike;
-- only a no-event pass row carries it, and it is what the walk-up uniqueness index keys on.
ALTER TABLE event_wristband ADD COLUMN IF NOT EXISTS valid_on_date date NULL;

-- Exactly one anchor per row: a ticket or a pass admission, never both and never neither.
-- Every existing row satisfies this already (ticket_id NOT NULL and the new column NULL,
-- so 1 + 0 = 1), so the constraint validates clean against current data.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_event_wristband_anchor') THEN
        ALTER TABLE event_wristband ADD CONSTRAINT chk_event_wristband_anchor
            CHECK (((ticket_id IS NOT NULL)::int + (season_pass_reservation_id IS NOT NULL)::int) = 1);
    END IF;
END $$;

-- A row must be scoped to something, an event or a calendar date, never neither. Also true
-- of every existing row, all of which have event_id set.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_event_wristband_scope') THEN
        ALTER TABLE event_wristband ADD CONSTRAINT chk_event_wristband_scope
            CHECK (event_id IS NOT NULL OR valid_on_date IS NOT NULL);
    END IF;
END $$;

-- Script0189 made a code unique per event because cheap band packs repeat their number
-- ranges, so a code only means something within one scope unit. A no-event admission has no
-- event to be that unit, so the unit becomes tenant plus calendar day: band 0347 today and
-- band 0347 tomorrow never collide, which is the same guarantee by the same reasoning.
-- The original uk_event_wristband_code keeps covering every row that does carry an event.
CREATE UNIQUE INDEX IF NOT EXISTS uk_event_wristband_code_walkup
    ON event_wristband (tenant_id, valid_on_date, lower(code))
    WHERE event_id IS NULL;

-- One admission wears one band, mirroring uk_event_wristband_ticket. Linking a new band to an
-- already-banded admission replaces the old row (delete then insert in the repository) so a
-- lost band stops resolving the moment its replacement is issued. The original ticket index
-- is unaffected by ticket_id going nullable, since unique indexes treat NULLs as distinct and
-- pass-anchored rows therefore never collide under it.
CREATE UNIQUE INDEX IF NOT EXISTS uk_event_wristband_reservation
    ON event_wristband (season_pass_reservation_id)
    WHERE season_pass_reservation_id IS NOT NULL;
