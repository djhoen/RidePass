-- Gate check-in: a waiver signing link sent FOR a specific ticket or season pass.
--
-- The gate blocks a rider under 18 whose waiver they signed themselves (a parent or guardian has
-- to sign). Staff fix it on the spot by sending the existing waiver_sign_request link to the
-- guardian. When that link is signed, the new signature must be linked to the rider's ticket(s)
-- or pass, or the gate would keep reading the old self-signed one. These columns record which
-- ticket / pass a request was sent for; SignByToken signs for that rider (their name and the
-- birthdate the gate uses, a guardian's signature for a minor) and links the new signature where
-- the gate reads it, only where the current evidence is missing or failing.
--
-- Additive and nullable: requests sent from the admin Waivers screen keep both NULL and behave
-- exactly as before. ON DELETE SET NULL so removing a ticket or pass never fails on a request.
-- Rerunnable: ADD COLUMN IF NOT EXISTS / CREATE INDEX IF NOT EXISTS.

ALTER TABLE waiver_sign_request
    ADD COLUMN IF NOT EXISTS event_ticket_purchase_id uuid NULL
        REFERENCES event_ticket_purchase(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS season_pass_purchase_id uuid NULL
        REFERENCES season_pass_purchase(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_waiver_sign_request_ticket
    ON waiver_sign_request (tenant_id, event_ticket_purchase_id)
    WHERE event_ticket_purchase_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_waiver_sign_request_pass
    ON waiver_sign_request (tenant_id, season_pass_purchase_id)
    WHERE season_pass_purchase_id IS NOT NULL;
