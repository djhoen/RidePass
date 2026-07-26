-- Promotes the season pass holder discount from an F&B menu setting to a tenant-level one, so it
-- sits beside Rewards (Script0259) as its own switch.
--
-- WHY IT MOVES. These are two ways to answer the same question, "how do we reward regulars?", and
-- a track picks one: a standing discount for pass holders, or points/credit earned per visit. One
-- of them living in Settings -> Features and the other buried three levels into the F&B menu
-- editor made them look like different KINDS of thing, so a tenant weighing them up never saw
-- them side by side. Running both is allowed (the UI warns rather than blocks) because a track
-- may well want pass holders discounted AND everyone earning credit.
--
-- WHAT IT DOES NOT GOVERN: per-pass benefits (season_pass_benefit). Those are product
-- configuration, not a loyalty scheme, and an employee pass granting free entry must not switch
-- off because a tenant turned off "discount for pass holders". The new flag governs only the
-- tenant-wide "any active pass holder gets X off" perk.
--
-- Expand, don't contract: concession_menu_settings keeps its columns and its data. The app stops
-- READING them here, so a rollback to the previous release finds its settings intact.
--
-- Additive and rerunnable.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS season_pass_discount_enabled boolean NOT NULL DEFAULT false,
    -- 'percent' (basis points) or 'amount' (cents), matching every other discount in the schema.
    ADD COLUMN IF NOT EXISTS season_pass_discount_kind    text    NOT NULL DEFAULT 'percent',
    ADD COLUMN IF NOT EXISTS season_pass_discount_value   integer NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenant_season_pass_discount_kind') THEN
        ALTER TABLE tenant ADD CONSTRAINT ck_tenant_season_pass_discount_kind
            CHECK (season_pass_discount_kind IN ('percent', 'amount'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenant_season_pass_discount_value') THEN
        -- Percent is basis points, so 10000 is the 100% ceiling; an amount is unbounded cents and
        -- gets clamped to the sale at apply time.
        ALTER TABLE tenant ADD CONSTRAINT ck_tenant_season_pass_discount_value
            CHECK (season_pass_discount_value >= 0
                   AND (season_pass_discount_kind <> 'percent' OR season_pass_discount_value <= 10000));
    END IF;
END $$;

-- Carry each tenant's existing F&B setting up. Without this every track already discounting pass
-- holders at the window would silently stop on deploy, and the first they'd know is a pass holder
-- being charged full price for a burger.
--
-- Guarded on the tenant still being at defaults so a re-run can't clobber a value edited after the
-- first run.
UPDATE tenant t
SET season_pass_discount_enabled = true,
    season_pass_discount_kind    = COALESCE(NULLIF(s.season_pass_discount_kind, ''), 'percent'),
    season_pass_discount_value   = GREATEST(0, COALESCE(s.season_pass_discount_value, 0))
FROM concession_menu_settings s
WHERE s.tenant_id = t.id
  AND s.season_pass_discount_enabled
  AND NOT t.season_pass_discount_enabled
  AND t.season_pass_discount_value = 0;
