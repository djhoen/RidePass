-- Wristband association: link a serialized wristband (QR payload or printed number) to an event
-- entrant at the gate.
--
-- The band's code carries no meaning on its own — vendors sell bands with sequential numbers or
-- arbitrary serialized QR payloads, and cheap number packs even repeat ranges across packs. The
-- association at check-in is what gives a band meaning: staff scan/enter the entrant's ticket,
-- then the band, and from that moment scanning the wrist IS scanning the entrant (re-entry,
-- staging, perks). Codes are therefore only required to be unique per EVENT, which is exactly the
-- guarantee the unique index below provides; band 0347 today and band 0347 next Saturday never
-- collide.
--
-- One band per ticket: linking a new band to an already-banded entrant REPLACES the old link
-- (the lost-band case — the old band must stop resolving), enforced by the ticket unique index
-- with delete-then-insert semantics in the repository.
--
-- Additive + idempotent.

-- Tenant-controlled feature, off by default and available to every tenant (no platform gate):
-- a track that buys bands flips it on under Settings -> Features and the gate grows the band UI.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS wristbands_enabled boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS event_wristband (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    event_id           uuid        NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    ticket_id          uuid        NOT NULL REFERENCES event_ticket_purchase(id) ON DELETE CASCADE,
    -- The scanned QR payload or typed band number, stored as entered (trimmed); matched
    -- case-insensitively so a hand-typed hex serial can't miss on casing.
    code               text        NOT NULL,
    linked_by_user_id  uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    linked_at          timestamptz NOT NULL DEFAULT now()
);

-- A band can only mean one entrant per event.
CREATE UNIQUE INDEX IF NOT EXISTS uk_event_wristband_code
    ON event_wristband (tenant_id, event_id, lower(code));
-- An entrant wears one band (replacement deletes the old row first).
CREATE UNIQUE INDEX IF NOT EXISTS uk_event_wristband_ticket
    ON event_wristband (ticket_id);
-- Resolve hot path: band scanned with no event context — match by tenant + code, then pick the
-- current event among matches.
CREATE INDEX IF NOT EXISTS idx_event_wristband_lookup
    ON event_wristband (tenant_id, lower(code));
