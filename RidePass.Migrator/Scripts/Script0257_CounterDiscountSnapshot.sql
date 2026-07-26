-- Lets the gate counter take a staff-applied discount (Script0251's tenant-wide list) and records
-- which one, on every kind of thing the counter sells.
--
-- The gate is shaped differently from the other two counters. A bike shop sale and an F&B order each
-- write ONE header row, so their discount snapshot (Script0252, Script0160) has an obvious home. A
-- counter sale has no header row at all: it writes straight into event_ticket_purchase,
-- event_extra_purchase, membership_purchase and shop_rental, and the "sale" exists only as the set of
-- rows produced together. So the snapshot has to go on each of them.
--
-- That turns out to be the RIGHT granularity rather than a workaround, because a counter cart is
-- mixed: one sale can hold a race entry, a membership and a rental bike. A discount scoped to event
-- tickets must come off the tickets and leave the membership alone, so "how much came off" is a
-- per-row fact and could not be expressed by a single sale-level column even if there were one.
--
-- discount_cents is NOT NULL DEFAULT 0, which is exactly what every existing row means: nothing came
-- off. The other three are nullable because they are only meaningful when a staff discount was
-- actually applied. All four are additive, so the currently-deployed app keeps inserting without them.
--
-- Why the label is stored next to the id, same reasoning as Script0252: a track that renames
-- "Military 10%" to "Military 15%" must not silently rewrite what last season's receipts say, and the
-- ON DELETE SET NULL below would otherwise erase the reason entirely.

ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS discount_cents int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_preset_id uuid NULL REFERENCES discount_preset(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS discount_label text NULL,
    ADD COLUMN IF NOT EXISTS discount_authorized_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE event_extra_purchase
    ADD COLUMN IF NOT EXISTS discount_cents int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_preset_id uuid NULL REFERENCES discount_preset(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS discount_label text NULL,
    ADD COLUMN IF NOT EXISTS discount_authorized_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE membership_purchase
    ADD COLUMN IF NOT EXISTS discount_cents int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_preset_id uuid NULL REFERENCES discount_preset(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS discount_label text NULL,
    ADD COLUMN IF NOT EXISTS discount_authorized_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE shop_rental
    ADD COLUMN IF NOT EXISTS discount_cents int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_preset_id uuid NULL REFERENCES discount_preset(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS discount_label text NULL,
    ADD COLUMN IF NOT EXISTS discount_authorized_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

-- Answering "what did the military rate cost us, and on what" means sweeping four tables by preset.
-- Partial because the overwhelming majority of rows carry no staff discount at all.
CREATE INDEX IF NOT EXISTS idx_event_ticket_purchase_discount_preset
    ON event_ticket_purchase (tenant_id, discount_preset_id)
    WHERE discount_preset_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_event_extra_purchase_discount_preset
    ON event_extra_purchase (tenant_id, discount_preset_id)
    WHERE discount_preset_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_membership_purchase_discount_preset
    ON membership_purchase (tenant_id, discount_preset_id)
    WHERE discount_preset_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_shop_rental_discount_preset
    ON shop_rental (tenant_id, discount_preset_id)
    WHERE discount_preset_id IS NOT NULL;
