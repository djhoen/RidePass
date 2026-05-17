-- Membership requirement model: collapse the four "required for ..." flags into
-- two audience-shaped flags so admins think in terms of riders vs. spectators.
--
--   * required_for_riders    — race-entry purchases, day passes, season passes.
--                              Default TRUE — most tracks require their racers
--                              to be members.
--   * required_for_spectators — extras (Gate Fees, camping, parking, merch).
--                              Default FALSE — most tracks let walk-up
--                              spectators through without a membership.
--
-- Existing tenants carry forward by OR-ing the legacy flags into the new shape:
--   riders     = pass OR event_ticket OR season_pass
--   spectators = extras

ALTER TABLE tenant
    ADD COLUMN membership_required_for_riders     boolean NOT NULL DEFAULT true,
    ADD COLUMN membership_required_for_spectators boolean NOT NULL DEFAULT false;

UPDATE tenant SET
    membership_required_for_riders =
        (membership_required_for_pass
         OR membership_required_for_event_ticket
         OR membership_required_for_season_pass),
    membership_required_for_spectators = membership_required_for_extras;

ALTER TABLE tenant
    DROP COLUMN membership_required_for_pass,
    DROP COLUMN membership_required_for_event_ticket,
    DROP COLUMN membership_required_for_season_pass,
    DROP COLUMN membership_required_for_extras;
