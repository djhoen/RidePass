-- Waitlist buckets must follow a price ladder's CLASS, not an individual step.
--
-- A price ladder is a set of event_ticket_tier rows sharing a ladder_group; the
-- live price escalates step by step. A buyer's purchase row and a waitlister's
-- entry can therefore reference DIFFERENT steps of the same class (someone who
-- bought the early $50 step vs someone who joined the waitlist when the active
-- step was $75). The old bucket key was tier_id alone, so a refund on the $50
-- step would peek an empty $50 bucket and never promote the $75-bucket waiter.
--
-- Fix: carry the ladder_group on the waitlist row and key the bucket on the
-- class (COALESCE(ladder_group, tier_id)). tier_id still records the exact step
-- a promotion charges (the active step captured at join time). Standalone tiers
-- and per-event (tier-less) waitlists keep their existing behavior because their
-- ladder_group stays NULL.

ALTER TABLE event_waitlist
    ADD COLUMN IF NOT EXISTS ladder_group text NULL;

-- Backfill active rows whose tier is a ladder step. In practice a no-op: joining
-- a ladder waitlist was impossible before this change, so no such rows exist yet.
-- Included for correctness on any stragglers.
UPDATE event_waitlist w
SET ladder_group = t.ladder_group
FROM event_ticket_tier t
WHERE w.tier_id = t.id
  AND t.ladder_group IS NOT NULL
  AND w.ladder_group IS NULL;

-- One waiting/promoted row per rider per CLASS. For a ladder the class is the
-- ladder_group; otherwise it's the tier_id (or the all-zero sentinel for a
-- per-event waitlist). Replaces the tier-only unique index.
DROP INDEX IF EXISTS uk_event_waitlist_active_per_user;
CREATE UNIQUE INDEX uk_event_waitlist_active_per_user
    ON event_waitlist (
        event_id,
        COALESCE(ladder_group, tier_id::text, '00000000-0000-0000-0000-000000000000'),
        user_id)
    WHERE status IN ('waiting','promoted');

-- Hot path: the promoter picks the lowest-position waiting row in a class bucket.
CREATE INDEX IF NOT EXISTS idx_event_waitlist_class_queue
    ON event_waitlist (event_id, ladder_group, tier_id, status, position);
