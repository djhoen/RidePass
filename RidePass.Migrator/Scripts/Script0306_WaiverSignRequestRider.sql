-- Gate check-in: an adult-waiver signing link carries the RIDER it is for.
--
-- A rider who turns 18 on a waiver from their minor years must sign again as an adult. Signed
-- Waivers can send them a link ahead of their next visit. When they have an upcoming ticket or pass,
-- the link targets that credential (Script0304). When they don't, the link still has to produce a
-- signature the gate will recognise as theirs, so the request records who it is for: the rider's
-- account and their name and birthdate as the venue holds them. SignByToken signs AS that rider
-- (the form cannot change the name or birthdate, and the parent path is refused) and attaches the
-- signature to the rider's account. rider_user_id is also the key for "this rider already has an
-- open link", so two riders sharing an email each get their own.
--
-- Additive and nullable: every existing request keeps all four NULL and behaves as before; every
-- existing signature keeps former_user_id NULL.
-- Rerunnable: ADD COLUMN IF NOT EXISTS / CREATE INDEX IF NOT EXISTS.

ALTER TABLE waiver_sign_request
    ADD COLUMN IF NOT EXISTS rider_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS rider_first_name text NULL,
    ADD COLUMN IF NOT EXISTS rider_last_name text NULL,
    ADD COLUMN IF NOT EXISTS rider_birthdate date NULL;

CREATE INDEX IF NOT EXISTS idx_waiver_sign_request_rider
    ON waiver_sign_request (tenant_id, rider_user_id)
    WHERE rider_user_id IS NOT NULL;

-- An account signature replaced by a newer one (a rider who has turned 18 re-signing over their
-- guardian's signature) is kept as the legal record but leaves the account slot (user_id is unique per
-- document). former_user_id keeps it attributable to the account it was on, so Signed Waivers still
-- lists it under that person. NULL for every signature never detached.
ALTER TABLE rider_waiver_signature
    ADD COLUMN IF NOT EXISTS former_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_rider_waiver_sig_former_user
    ON rider_waiver_signature (tenant_id, former_user_id)
    WHERE former_user_id IS NOT NULL;

-- Signed Waivers resolves a signature's rider account through the season pass it backs.
CREATE INDEX IF NOT EXISTS idx_season_pass_waiver_sig
    ON season_pass_purchase (waiver_signature_id)
    WHERE waiver_signature_id IS NOT NULL;
