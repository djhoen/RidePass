-- Gate Sale: day passes for "another rider" (a friend, a son's friend) on the payer's order.
--
-- The payer's one payment buys tickets for other people. Each of those riders gets a profile:
--   * an adult (or a minor with their own email) is a platform rider account (users, tenant_id NULL),
--     reused by email so an existing account is never duplicated;
--   * a minor is linked to their parent / guardian's account by a rider_dependent row. There was no
--     dependent / family model before this: the app modelled a child only as a named rider on a
--     credential under the buying account (a rider key by name). rider_dependent makes the child a durable
--     profile: guardian account + normalized rider name, unique per tenant, so the next sale for
--     the same child under the same guardian finds the same row. rider_user_id is set when the minor
--     also has an account of their own; the emergency contact defaults to the guardian. A ticket that
--     names a dependent is keyed by the dependent's id at the gate, never matched to the payer by name.
--
-- event_ticket_purchase.rider_user_id / rider_dependent_id record whose profile a ticket is for (the
-- purchaser stays the payer). waiver_sign_request.recipient_user_id records the account a signing link
-- was sent to (the guardian for a minor, the rider for an adult), so the signature is attributable to
-- an existing account without anyone logging in.
--
-- Additive and nullable: existing rows keep NULL and behave as before.
-- Rerunnable: CREATE TABLE / ADD COLUMN / CREATE INDEX IF NOT EXISTS.

CREATE TABLE IF NOT EXISTS rider_dependent (
    id                 uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    guardian_user_id   uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    rider_user_id      uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    first_name         text        NOT NULL,
    last_name          text        NOT NULL,
    birthdate          date        NULL,
    -- IdCheckRules.Normalize("first last"): the same child under the same guardian is one row. The gate
    -- keys a dependent's ID checks and photos by this row's id ("dependent:<id>"), never by name.
    rider_key          text        NOT NULL,
    created_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);

-- The rider's emergency contact (tenants that require one): the guardian unless staff enter another.
ALTER TABLE rider_dependent
    ADD COLUMN IF NOT EXISTS emergency_contact_name  text NULL,
    ADD COLUMN IF NOT EXISTS emergency_contact_phone text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_rider_dependent_guardian_key
    ON rider_dependent (tenant_id, guardian_user_id, rider_key);

DROP TRIGGER IF EXISTS trg_rider_dependent_updated_at ON rider_dependent;
CREATE TRIGGER trg_rider_dependent_updated_at
    BEFORE UPDATE ON rider_dependent
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS rider_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS rider_dependent_id uuid NULL REFERENCES rider_dependent(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_event_ticket_purchase_rider_user
    ON event_ticket_purchase (tenant_id, rider_user_id)
    WHERE rider_user_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_event_ticket_purchase_rider_dependent
    ON event_ticket_purchase (rider_dependent_id)
    WHERE rider_dependent_id IS NOT NULL;

ALTER TABLE waiver_sign_request
    ADD COLUMN IF NOT EXISTS recipient_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;
