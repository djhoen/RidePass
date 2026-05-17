-- Splits event ticket tiers into two named kinds shown on the public site under the
-- general label "Admissions":
--   spectator_pass — pay-to-watch (parents, spectators)
--   race_entry     — pay-to-compete (riders entering classes)
--
-- Existing rows default to spectator_pass since pre-split data is more likely to
-- have been general gate admission than race-class entry. Tenant admins can
-- recategorize via the admin UI after the migration.

ALTER TABLE event_ticket_tier
    ADD COLUMN kind text NOT NULL DEFAULT 'spectator_pass'
        CHECK (kind IN ('spectator_pass', 'race_entry'));
