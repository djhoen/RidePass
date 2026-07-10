-- Per-event override for the gate-fee section headings riders see at checkout and
-- in event pricing. Script0171 added the tenant-wide labels; tracks also want to
-- vary the wording per event (a race says "Rider Gate", an open-ride day sells the
-- same tiers as "Passes"). Resolution is event override -> tenant setting ->
-- platform default, so NULL here means "inherit".

ALTER TABLE event
    ADD COLUMN IF NOT EXISTS rider_gate_label text NULL,
    ADD COLUMN IF NOT EXISTS spectator_gate_label text NULL;
