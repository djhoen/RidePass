-- Buddy passes: a season pass grants its holder N guest admissions per season, redeemed at the
-- counter with the holder present. Design: docs/buddy-passes.md.
--
-- The entitlement ITSELF already exists and needs no new storage: season_pass_benefit has
-- accepted benefit_type = 'buddy_pass' with a quantity since Script0178, and the buy page has
-- been advertising it ("3 buddy passes a season") that whole time. What has never existed is a
-- way to say what it is good for, and a way to spend one. Those are the two tables here.
--
-- WHY NOT COUPONS. The obvious cheaper design is to mint N shareable single-use coupons at
-- purchase and let the buddy redeem one at checkout. It was rejected because the requirement is
-- that the PASS HOLDER IS PRESENT and staff perform the redemption: a transferable code lets a
-- buddy redeem alone from their sofa, and no amount of validation makes a bearer token prove
-- that two specific people are standing at a window. That decision is what forces explicit
-- usage tracking, hence season_pass_buddy_redemption.

-- ── What the buddy pass is good for ─────────────────────────────────────────
-- The quantity is ONE pool; the scopes are a SET. Three buddy passes valid at Lift Days AND
-- Clinics is one entitlement with two scopes, not two entitlements of three, so the scopes
-- cannot live on season_pass_benefit (whose unique index is one row per benefit per scope).
CREATE TABLE IF NOT EXISTS season_pass_buddy_scope (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    benefit_id    uuid NOT NULL REFERENCES season_pass_benefit(id) ON DELETE CASCADE,
    -- Exactly one of these is set, enforced below. event_type_id names a tenant_event_type;
    -- is_walk_up covers admission on a day with no event at all (Script0236's walk-up mode).
    event_type_id uuid NULL REFERENCES tenant_event_type(id) ON DELETE CASCADE,
    is_walk_up    boolean NOT NULL DEFAULT false,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_buddy_scope_target CHECK (
        (event_type_id IS NOT NULL AND is_walk_up = false)
     OR (event_type_id IS NULL     AND is_walk_up = true)
    )
);

-- One row per target. Two "Lift Day" rows would double-list the perk in the admin UI.
CREATE UNIQUE INDEX IF NOT EXISTS uk_buddy_scope_event_type
    ON season_pass_buddy_scope (benefit_id, event_type_id) WHERE event_type_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uk_buddy_scope_walk_up
    ON season_pass_buddy_scope (benefit_id) WHERE is_walk_up;

-- ZERO scope rows means valid NOWHERE, not everywhere. The server rejects saving a buddy_pass
-- benefit with an empty scope set rather than defaulting to "everything": the permissive default
-- would silently hand out free race entries, which is the expensive direction to be wrong in.

-- ── Spending one ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS season_pass_buddy_redemption (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- Which pass spent the entitlement. The remaining-count counts these.
    pass_purchase_id    uuid NOT NULL REFERENCES season_pass_purchase(id) ON DELETE CASCADE,
    buddy_user_id       uuid NOT NULL REFERENCES users(id),
    -- Event-anchored or walk-up-anchored, never neither. Deliberately the same shape as
    -- season_pass_reservation after Script0236, so both admissions read the same way.
    event_id            uuid NULL REFERENCES event(id) ON DELETE SET NULL,
    check_in_date       date NULL,
    -- Set for an event-day redemption: the discounted admission this entitlement paid for.
    ticket_purchase_id  uuid NULL REFERENCES event_ticket_purchase(id) ON DELETE SET NULL,
    discount_cents      int  NOT NULL DEFAULT 0,
    redeemed_at         timestamptz NOT NULL DEFAULT now(),
    redeemed_by_user_id uuid NULL REFERENCES users(id),
    -- Credit returned to the holder by an admin. SOFT on purpose: the row stays as the record
    -- that this admission happened (for the walk-up shape the row IS the admission record), and
    -- only the entitlement comes back. Deleting it would assert that someone who rode never did.
    credit_returned_at         timestamptz NULL,
    credit_returned_by_user_id uuid NULL REFERENCES users(id),
    credit_return_reason       text NULL,
    CONSTRAINT chk_buddy_redemption_anchor CHECK (event_id IS NOT NULL OR check_in_date IS NOT NULL),
    -- A returned credit always carries who and why; a live one carries neither. Enforced in the
    -- schema so no code path can write a reasonless return.
    CONSTRAINT chk_buddy_redemption_return CHECK (
        (credit_returned_at IS NULL     AND credit_returned_by_user_id IS NULL AND credit_return_reason IS NULL)
     OR (credit_returned_at IS NOT NULL AND credit_returned_by_user_id IS NOT NULL
         AND credit_return_reason IS NOT NULL AND length(btrim(credit_return_reason)) > 0)
    )
);

-- The hot path is "how many has this pass spent", which only counts live rows.
CREATE INDEX IF NOT EXISTS idx_buddy_redemption_pass
    ON season_pass_buddy_redemption (pass_purchase_id) WHERE credit_returned_at IS NULL;

-- Usage report: everything this tenant has spent, newest first.
CREATE INDEX IF NOT EXISTS idx_buddy_redemption_tenant
    ON season_pass_buddy_redemption (tenant_id, redeemed_at DESC);

-- One buddy, one admission, per pass per local day, for the walk-up shape. Stops a double-tap at
-- the window from burning two entitlements, mirroring uk_season_pass_reservation_walkup.
--
-- `credit_returned_at IS NULL` in the predicate is LOAD-BEARING, not decoration: without it a
-- mis-scan that was returned would permanently block re-admitting that same buddy on that same
-- day, which is precisely the situation a return exists to recover from. The event shape is
-- deliberately not covered: the same buddy legitimately attends two different events in one day.
CREATE UNIQUE INDEX IF NOT EXISTS uk_buddy_redemption_walkup_once
    ON season_pass_buddy_redemption (pass_purchase_id, buddy_user_id, check_in_date)
    WHERE check_in_date IS NOT NULL AND credit_returned_at IS NULL;
