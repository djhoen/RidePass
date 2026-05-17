-- Event waitlist (alternates queue).
--
-- Bucketing:
--   * For race events, waitlist is per-tier (tier_id NOT NULL) — a Pro division
--     cancellation only promotes alternates waiting on the Pro division.
--   * For day-pass-reservation events, waitlist is per-event (tier_id NULL) —
--     all alternates compete for the next freed spot regardless of pass type.
--
-- Position:
--   * Lowest position number = front of the line.
--   * Computed as MAX(position) + 1 over the same (event_id, tier_id) bucket
--     restricted to waiting rows. Existing 'expired'/'cancelled' rows stay for
--     audit but don't affect position numbers.
--
-- Pre-pay branch:
--   * is_prepaid → rider already has a Stripe PaymentIntent locked in. When
--     promoted, status flips straight to 'confirmed' and the system creates
--     the real purchase row. If the event ends without ever promoting them,
--     a refund is issued via prepay_refund_id.
--
-- Promotion + confirm window:
--   * promoted_at_utc / confirm_deadline_utc set when WaitlistPromoter picks
--     this row off the front. confirm_token is the rider's one-time link
--     embedded in the SMS.

CREATE TABLE event_waitlist (
    id                          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    event_id                    uuid        NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    tier_id                     uuid        NULL REFERENCES event_ticket_tier(id) ON DELETE CASCADE,
    user_id                     uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    position                    int         NOT NULL CHECK (position >= 1),
    quantity                    int         NOT NULL DEFAULT 1 CHECK (quantity >= 1),
    notes                       text        NULL,

    -- Pre-pay branch (optional). When is_prepaid=true the rider already paid;
    -- promotion is auto-confirm with no SMS-driven 20-min window.
    is_prepaid                  boolean     NOT NULL DEFAULT false,
    prepay_pi_id                text        NULL,
    prepay_amount_cents         int         NOT NULL DEFAULT 0,
    prepay_refund_id            text        NULL,
    prepay_refunded_at_utc      timestamptz NULL,

    -- Promotion state.
    promoted_at_utc             timestamptz NULL,
    confirm_deadline_utc        timestamptz NULL,
    confirm_token               uuid        NULL,
    -- Filled in once status='confirmed' so we know which purchase the
    -- waitlist row spawned (handy for refund-on-cancel and audits).
    created_purchase_id         uuid        NULL,
    created_purchase_kind       text        NULL CHECK (created_purchase_kind IS NULL
                                              OR created_purchase_kind IN ('day_pass','event_ticket')),

    -- Lifecycle:
    --   waiting   → in queue
    --   promoted  → at the front, awaiting confirmation
    --   confirmed → paid (if needed) and converted to a real purchase
    --   expired   → didn't confirm in time; promoter moves to next
    --   cancelled → rider withdrew before promotion
    status                      text        NOT NULL DEFAULT 'waiting'
                                              CHECK (status IN ('waiting','promoted','confirmed','expired','cancelled')),

    cancelled_reason            text        NULL,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);

-- One waiting/promoted row per rider per (event,tier). Letting riders queue
-- twice gives them an unfair edge, and the UI doesn't surface it.
CREATE UNIQUE INDEX uk_event_waitlist_active_per_user
    ON event_waitlist (event_id, COALESCE(tier_id, '00000000-0000-0000-0000-000000000000'::uuid), user_id)
    WHERE status IN ('waiting','promoted');

-- Hot path: WaitlistPromoter picks lowest-position waiting row in a bucket.
CREATE INDEX idx_event_waitlist_bucket_queue
    ON event_waitlist (event_id, tier_id, status, position);

-- Expiry worker scans for promoted rows past their deadline.
CREATE INDEX idx_event_waitlist_promoted_deadline
    ON event_waitlist (confirm_deadline_utc)
    WHERE status = 'promoted';

-- One-time confirm token lookup from the rider's SMS link.
CREATE UNIQUE INDEX uk_event_waitlist_confirm_token
    ON event_waitlist (confirm_token)
    WHERE confirm_token IS NOT NULL;

CREATE INDEX idx_event_waitlist_user ON event_waitlist (user_id, status);

CREATE TRIGGER trg_event_waitlist_updated_at
    BEFORE UPDATE ON event_waitlist
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
