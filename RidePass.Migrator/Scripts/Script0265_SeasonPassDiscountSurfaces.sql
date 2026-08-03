-- Lets the season pass holder discount (Script0260) reach the bike shop and rentals, not just the
-- F&B window, with a per-surface switch for each.
--
-- WHY PER SURFACE RATHER THAN ONE BLANKET FLAG. A single percentage across every till sounds
-- simpler right up until it costs a track real money: 15% chosen with a $9 burger in mind is 15%
-- off a $6,000 bike, and bike margins are nothing like food margins. So the amount stays shared
-- (a track has one idea of "the pass holder perk") while WHERE it applies is a deliberate choice.
--
-- DEFAULT true, BACKFILLED false for retail and rentals on anyone already running the discount.
-- That distinction is the whole point of the script:
--   * The default governs a track setting the perk up fresh from here on, and "all my counters" is
--     what they mean when they switch it on.
--   * The backfill protects every track that already had this on for F&B only. Letting the default
--     apply to them would silently start discounting bikes and rentals the moment this deploys,
--     which is a margin change nobody asked for and nobody would attribute to a migration.
--
-- Additive and rerunnable.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS season_pass_discount_applies_concession boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS season_pass_discount_applies_retail     boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS season_pass_discount_applies_rental     boolean NOT NULL DEFAULT true;

-- Guarded on this script's own journal row rather than on the column values, so a re-run cannot
-- undo a track that has since turned retail or rentals ON on purpose. Same fencing pattern as
-- Script0229.
--
-- The pattern deliberately omits the script NUMBER. This file was renumbered from 0261 to 0265 to
-- clear a duplicate-number collision, and a number-bearing pattern would have stopped matching the
-- journal row already written under the old name: the backfill would then re-run on every database
-- that had already had it, silently switching retail and rentals back off for any track that had
-- since turned them on. Matching on the description alone survives renumbering in either direction.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schemaversions
        WHERE scriptname LIKE '%SeasonPassDiscountSurfaces%'
    ) THEN
        UPDATE tenant
        SET season_pass_discount_applies_retail = false,
            season_pass_discount_applies_rental = false
        WHERE season_pass_discount_enabled;
    END IF;
END $$;
