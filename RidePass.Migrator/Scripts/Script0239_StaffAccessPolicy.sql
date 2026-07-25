-- Where and when staff may perform the operations that move money. A track's cashier
-- credentials work identically from the counter and from an employee's couch at 2am, and until
-- now nothing in the app could tell those apart or care. These columns let a tenant say "the
-- register only works at the track, during hours", which turns a stolen or misused login from a
-- standing liability into something bounded by geography and the clock.
--
-- Two independent constraints, either of which can be left empty:
--
--   staff_allowed_cidrs   the networks the track operates from. Empty array = no location rule,
--                         which is the right default for a track on a dynamic residential IP
--                         that cannot name a stable range.
--   staff_hours_start/end the tenant-LOCAL window operations are allowed in. Both NULL = no
--                         clock rule. A window whose end is <= its start is read as crossing
--                         midnight (22:00 to 02:00), which is how a night event actually runs.
--
-- staff_access_policy_mode gates the whole thing: 0 = off (the default, and the behavior every
-- tenant has today), 1 = enforce. There is deliberately no "warn" mode. The Staff Activity
-- screen already shows every recorded action with the address it came from, so a track reviews
-- what actually happens there first and flips enforcement on once the allowlist is known to be
-- right. A warn mode would have meant writing an audit row per request on the gate-scan hot path
-- to tell them something that screen already tells them.
--
-- Enforcement never applies to settings.manage or users.manage. An admin locked out by their own
-- policy has to be able to sign in from anywhere and correct it; a rule with no way back is an
-- outage waiting to happen, not a control.
--
-- Additive and idempotent. Nothing reads these columns until the code that enforces them ships,
-- and even then only when a tenant opts in by setting the mode.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS staff_access_policy_mode int NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_staff_access_policy_mode'
    ) THEN
        ALTER TABLE tenant
            ADD CONSTRAINT chk_tenant_staff_access_policy_mode
            CHECK (staff_access_policy_mode IN (0, 1));
    END IF;
END $$;

-- Stored as text rather than inet/cidr: these are round-tripped to a settings form as typed,
-- validated in application code, and never used as a SQL operand. Keeping them text avoids
-- Postgres rejecting a half-typed value on save and keeps the column readable in the admin UI.
ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS staff_allowed_cidrs text[] NOT NULL DEFAULT '{}';

-- Plain time, no zone: interpreted in the tenant's own timezone at check time, because "we close
-- at 8pm" means 8pm at the track regardless of where the server or the staffer is.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS staff_hours_start time NULL;
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS staff_hours_end   time NULL;

-- Both or neither. A half-configured window would otherwise silently mean "no rule", which reads
-- to an admin as though their setting took effect when it did not.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_staff_hours_pair'
    ) THEN
        ALTER TABLE tenant
            ADD CONSTRAINT chk_tenant_staff_hours_pair
            CHECK ((staff_hours_start IS NULL) = (staff_hours_end IS NULL));
    END IF;
END $$;
