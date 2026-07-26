-- A tenant-level on/off for rewards, so a track that doesn't run loyalty isn't shown a Rewards
-- admin page, a rewards prompt at the till, or a Rewards tab in the rider's account.
--
-- DEFAULT false, BACKFILLED true for anyone already running a program. This distinction matters:
-- reward_program rows already exist and are already paying out, so a plain `DEFAULT false` would
-- silently switch off a live loyalty scheme for every tenant using one, stop their riders earning,
-- and hide the page they'd go to in order to work out why. The backfill is the whole point of the
-- script; the default only governs tenants created after it.
--
-- Deliberately keyed off "has an active program" rather than "has any program": a tenant who built
-- one, turned it off, and moved on should stay off.

ALTER TABLE tenant ADD COLUMN IF NOT EXISTS rewards_enabled boolean NOT NULL DEFAULT false;

UPDATE tenant t
SET rewards_enabled = true
WHERE NOT t.rewards_enabled
  AND EXISTS (
      SELECT 1 FROM reward_program p
      WHERE p.tenant_id = t.id AND p.is_active
  );
