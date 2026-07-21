-- Remote signing for rentals: a token on the rental, mirroring the work order's
-- deposit_request_token, so a renter can sign the agreement and the waiver from the emailed
-- link after booking online instead of only at the counter.
--
-- The token IS the credential (same posture as the deposit payment link and the redemption QR):
-- unguessable, per rental, and it only ever reaches the signing page. Nothing about it can move
-- money or reveal another rental.
--
-- Additive and rerunnable. The DEFAULT backfills every existing rental with its own token.

ALTER TABLE shop_rental
    ADD COLUMN IF NOT EXISTS signature_request_token   uuid        NOT NULL DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS signature_request_sent_at timestamptz NULL;

-- The public page looks a rental up by this alone, so it has to be unique and indexed.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_rental_signature_token
    ON shop_rental (signature_request_token);
