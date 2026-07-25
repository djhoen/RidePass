-- Stored ID/age verification for the person a credential admits, plus a tenant switch that
-- makes a verified ID (and a signed waiver) a precondition for issuing a wristband.
--
-- Why this is not just tenant.require_id_at_checkin (Script0120): that setting is a per-scan
-- ATTESTATION. The gate worker ticks a box, RedemptionController checks the boolean, and it is
-- thrown away. Nothing is recorded against the rider, so the next scan asks again and no one can
-- ever see "this person's ID was already checked". Highland needs the opposite: verify once, see
-- the result on every subsequent scan, and gate the wristband on it. Script0120's setting is left
-- exactly as it is (it is off for every tenant in production) and this is a separate switch.
--
-- Why the columns land in THREE places rather than only on users. The account that bought a
-- credential is not necessarily the person it admits. SeasonPassCheckInContext.HolderUserId
-- says it outright: "a parent buys passes for their kids". The admitted
-- person lives in season_pass_purchase.holder_first_name/last_name/holder_birthdate (and the
-- event_ticket_purchase.rider_* equivalents) and frequently has no users row at all. A
-- users-only flag would leave exactly those riders permanently unverifiable, which at a track
-- that gates wristbands on verification means permanently unadmittable.
--
-- So: the credential row always carries the verification, and users carries it too when the
-- buyer demonstrably IS the holder (the app decides that; see RiderIdVerification). Reading
-- prefers the users row because it is the durable one that carries to future purchases.
--
-- id_verified_dob records what the ID actually SAID, which is the age evidence. It is kept
-- separate from the self-reported birthdate already on the row: the whole point of the check is
-- that one of them was confirmed against a document and the other was typed into a web form.
--
-- Verification does not expire. There is no "verified until" column by design; if that is ever
-- wanted it is an additive change on top of this.
--
-- Additive and idempotent throughout. No backfill: every existing row is simply unverified,
-- which is the correct starting state.

-- ── The durable person record ────────────────────────────────────────────────
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS id_verified_at         timestamptz NULL,
    ADD COLUMN IF NOT EXISTS id_verified_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS id_verified_dob        date        NULL;

-- ── The season pass holder, who may have no account ──────────────────────────
ALTER TABLE season_pass_purchase
    ADD COLUMN IF NOT EXISTS id_verified_at         timestamptz NULL,
    ADD COLUMN IF NOT EXISTS id_verified_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS id_verified_dob        date        NULL;

-- ── The ticketed rider, same reasoning. Present so the wristband gate has no bypass: without
-- it a worker could satisfy "verified" by linking a ticket-anchored band instead of a
-- pass-anchored one.
ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS id_verified_at         timestamptz NULL,
    ADD COLUMN IF NOT EXISTS id_verified_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS id_verified_dob        date        NULL;

-- ── The tenant switch ────────────────────────────────────────────────────────
-- When on: the gate screen shows waiver and ID status on every scan, and a wristband cannot be
-- linked until both are satisfied. Default FALSE so no existing track changes behaviour.
ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS require_id_for_wristband boolean NOT NULL DEFAULT false;
