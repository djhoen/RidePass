-- Lessons step 2: a training group IS a ticket tier.
--
-- See docs/lessons.md. MX and MTB both sell coached sessions as groups segmented by ability and
-- equipment, each with its own coach and cap. Race classes already model exactly this shape
-- (a `race_entry` tier with per-tier inventory and price), so a training group extends the tier
-- rather than introducing a parallel product. No new purchase table, no new checkout.
--
-- All columns are nullable/defaulted, so every existing tier keeps its current behavior.

ALTER TABLE event_ticket_tier
    -- Which coach runs THIS group. event_instructor stays the event-level roster (who is working
    -- the clinic at all); this points at the one running the group. ON DELETE SET NULL so
    -- deactivating a coach never destroys sold history.
    ADD COLUMN IF NOT EXISTS instructor_id   uuid NULL REFERENCES instructor(id) ON DELETE SET NULL,

    -- Ability band. Deliberately free text, not an enum: MX uses skill plus displacement
    -- ("Beginner", "Novice"), MTB uses trail-difficulty ability zones ("Green Circle",
    -- "Blue Square", "Black Diamond"). The UI offers a picklist seeded per tenant type.
    ADD COLUMN IF NOT EXISTS skill_level     text NULL,

    -- Equipment band, same free-text reasoning. MX: '50cc' | '65cc' | '85cc' | '250F'.
    -- MTB: 'Trail' | 'Downhill' | 'E-bike'.
    ADD COLUMN IF NOT EXISTS equipment_label text NULL,

    -- A group may run at its own time inside the event, so one clinic can put beginners in the
    -- morning and intermediates in the afternoon without becoming two events. NULL inherits the
    -- event's window, which is what every existing tier does today.
    ADD COLUMN IF NOT EXISTS starts_at       timestamptz NULL,
    ADD COLUMN IF NOT EXISTS ends_at         timestamptz NULL;

-- A group window must be a real interval when both ends are set. Guarded so re-running is safe.
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_event_ticket_tier_window') THEN
        ALTER TABLE event_ticket_tier ADD CONSTRAINT chk_event_ticket_tier_window
            CHECK (starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at);
    END IF;
END $$;

-- "Which groups is this coach running?" drives the double-booking check at save AND at checkout.
CREATE INDEX IF NOT EXISTS idx_event_ticket_tier_instructor
    ON event_ticket_tier (instructor_id) WHERE instructor_id IS NOT NULL;

-- How many students one coach can take in a single session. The effective cap on a group becomes
-- min(tier.inventory, instructor.max_students_per_session), so a coach cannot be oversubscribed
-- even when an admin leaves tier inventory blank. 8 is a middle-of-the-road default: ski group
-- lessons run 6 to 10.
ALTER TABLE instructor
    ADD COLUMN IF NOT EXISTS max_students_per_session int NOT NULL DEFAULT 8
        CHECK (max_students_per_session > 0);
