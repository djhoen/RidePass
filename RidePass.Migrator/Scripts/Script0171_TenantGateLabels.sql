-- Per-tenant wording for the gate-fee section headings riders see at checkout and
-- in the event pricing list. Some tracks don't call facility admission a "gate fee"
-- (e.g. they sell it as "Passes"), and the hardcoded "Rider Gate" / "Spectator Gate"
-- headings confused their riders. NULL means "use the platform default" so tenants
-- that never touch the setting keep today's wording, and clearing the field in the
-- admin UI reverts to the default rather than storing an empty string.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS rider_gate_label text NULL,
    ADD COLUMN IF NOT EXISTS spectator_gate_label text NULL;
