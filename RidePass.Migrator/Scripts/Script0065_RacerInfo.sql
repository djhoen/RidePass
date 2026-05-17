-- Per-rider racer attributes used on the Buy Race Entry "Racer Info" step and
-- in trackside-software exports. Stored on the user (not the purchase) so they
-- carry across events without re-asking. Both are nullable freeform text:
--   * race_number — e.g. "21", "07B", "X22" (alphanumeric; leading zeros matter)
--   * bike — e.g. "Yamaha YZ250F", "Honda CRF450R" (no enum; brand+model varies wildly)

ALTER TABLE users
    ADD COLUMN bike text NULL,
    ADD COLUMN race_number text NULL;
