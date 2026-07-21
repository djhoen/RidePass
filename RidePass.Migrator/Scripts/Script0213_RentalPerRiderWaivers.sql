-- Per-rider waivers on a rental.
--
-- The hole: shop_rental.waiver_signature_id holds ONE signature, and SignWaiver overwrites it.
-- Rent three bikes for three kids and the second signature replaces the first, so check-out passes
-- with one waiver on file and three unwaivered riders leave on the gear. The readiness check
-- compounds it by falling back to "does the renter have any signature" when the column is null.
--
-- The signing payload was never the problem: SignRegistrant already captures attendee name,
-- birthdate and parent-guardian details per person. Only the storage was single-valued. So this
-- keeps the existing flow and makes it many-per-rental, gated on how many riders the booking is for.
--
-- riders_required is explicit rather than derived, because units on a booking are not people: a
-- bike plus a helmet is two units and one rider, while two bikes is two riders. The counter sets
-- it (defaulted from the largest line quantity, which is right in the common cases).

-- How many people must sign before this gear goes out.
ALTER TABLE shop_rental
    ADD COLUMN IF NOT EXISTS riders_required int NOT NULL DEFAULT 1
        CHECK (riders_required >= 1);

COMMENT ON COLUMN shop_rental.riders_required IS
    'Number of riders who must each sign the waiver before check-out. Units on the booking are not people (bike + helmet = 1 rider), so this is set at booking rather than derived.';

-- One row per signature collected against the rental. RESTRICT on the signature so a signed
-- waiver can never be deleted out from under the rental that relies on it.
CREATE TABLE IF NOT EXISTS shop_rental_waiver (
    rental_id    uuid        NOT NULL REFERENCES shop_rental(id) ON DELETE CASCADE,
    signature_id uuid        NOT NULL REFERENCES rider_waiver_signature(id) ON DELETE RESTRICT,
    created_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (rental_id, signature_id)
);

CREATE INDEX IF NOT EXISTS idx_shop_rental_waiver_rental ON shop_rental_waiver (rental_id);

-- Backfill: fold the existing single signature into the join so rentals already signed stay
-- signed and don't suddenly read as unsigned at the counter.
INSERT INTO shop_rental_waiver (rental_id, signature_id)
SELECT r.id, r.waiver_signature_id
FROM shop_rental r
WHERE r.waiver_signature_id IS NOT NULL
ON CONFLICT DO NOTHING;

-- shop_rental.waiver_signature_id is deliberately KEPT. It still records the first/primary signer
-- and other code reads it; the join table is the source of truth for "is everyone signed".
