-- Staff notes on a rental booking.
--
-- shop_rental already has condition_notes, but that is a DIFFERENT thing: it is written once at
-- return time to record how the gear came back (ReturnShopRentalRequest), and it is the damage
-- record. Overloading it for booking notes would mean the note a booking clerk left at 9am gets
-- overwritten by the mechanic describing a bent rotor at 5pm.
--
-- Append-only thread rather than one editable field, mirroring shop_work_order_note (Script0220)
-- because a rental has the same shape of problem: it spans booking -> paid -> out -> returned,
-- with different staff touching it at each step. "Comped, do not charge" and "friend of the
-- owner, bring the good bike" are notes whose VALUE is knowing who said it and when. A single
-- text box is last-write-wins and silently loses that.
--
-- Deliberately internal-only. Work orders needed a separate customer_notes field because their
-- notes print on the claim tag and the bill; nothing here prints, so adding a customer-facing
-- field now would be inventing a requirement. If rental agreements later need customer-visible
-- text, that is an additive column on shop_rental, not a change to this table.
--
-- Additive and rerunnable.

CREATE TABLE IF NOT EXISTS shop_rental_note (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Carried directly rather than only via the parent, so every read scopes by tenant without a
    -- join. Same reasoning as shop_work_order_note.
    tenant_id          uuid NOT NULL REFERENCES tenant(id),
    rental_id          uuid NOT NULL REFERENCES shop_rental(id) ON DELETE CASCADE,
    body               text NOT NULL,
    created_by_user_id uuid REFERENCES users(id),
    created_at         timestamptz NOT NULL DEFAULT now()
);

-- The thread is read newest-first per rental.
CREATE INDEX IF NOT EXISTS ix_shop_rental_note_rental
    ON shop_rental_note (rental_id, created_at DESC);
