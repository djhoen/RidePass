-- Spectator waiver signatures.
--
-- Until now, rider_waiver_signature was always tied to a registered user (user_id
-- NOT NULL). Spectator buy flows let guest purchasers sign for themselves and on
-- behalf of children at purchase time, with no account ever being created.
--
-- Changes:
--   * user_id becomes nullable so guest signatures can land here.
--   * The (user_id, waiver_id) unique key becomes a partial unique that only
--     enforces uniqueness for registered-user signatures. A guest purchaser
--     may legitimately submit several signatures (one per spectator) against
--     the same waiver in a single purchase.
--   * New columns capture who actually signed and who they signed for:
--       signer_email / signer_name      — the purchaser (parent for minors)
--       spectator_first_name/last_name  — the actual person attending
--       spectator_birthdate             — used to derive minor status at sign time

ALTER TABLE rider_waiver_signature
    ALTER COLUMN user_id DROP NOT NULL,
    ADD COLUMN signer_email           text NULL,
    ADD COLUMN signer_name            text NULL,
    ADD COLUMN spectator_first_name   text NULL,
    ADD COLUMN spectator_last_name    text NULL,
    ADD COLUMN spectator_birthdate    date NULL;

ALTER TABLE rider_waiver_signature
    DROP CONSTRAINT uk_rider_waiver_once;

CREATE UNIQUE INDEX uk_rider_waiver_once_user
    ON rider_waiver_signature (user_id, waiver_id)
    WHERE user_id IS NOT NULL;

-- Lookup index for guest-email checks ("has this email already signed THIS waiver
-- for themselves, i.e. without a child on the row?")
CREATE INDEX idx_rider_waiver_sig_signer_email
    ON rider_waiver_signature (waiver_id, lower(signer_email))
    WHERE signer_email IS NOT NULL;
