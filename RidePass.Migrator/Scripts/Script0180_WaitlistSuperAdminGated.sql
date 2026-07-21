-- Waitlist becomes a super-admin-gated platform feature, off by default, and pre-pay splits out
-- into its own toggle.
--
-- Two problems with how this worked.
--
-- 1. waitlist_enabled defaulted TRUE and every new track got a waitlist whether it wanted one or
--    not. In practice few tracks want one, and the ones that do almost always want the simple
--    version: text the alternates when a spot opens. They do not want to take money from a rider
--    for a spot that may never materialise.
--
-- 2. Pre-pay was not separable. Turning the waitlist on turned on charging a rider at join time,
--    which is the risky half of the feature (it holds a rider's money against a maybe) and the
--    half almost nobody asked for. It now needs its own deliberate super-admin decision, and it is
--    inert unless the waitlist is on as well.
--
-- The frontend already treated waitlist as a platform feature (PLATFORM_FEATURE_KEYS in
-- Features.vue renders it read-only), but the API still accepted waitlist_enabled on the
-- tenant-facing PUT /Tenant/CancellationPolicy, so a tenant admin could flip it with a direct call.
-- That endpoint no longer takes the flag; both toggles now live only on the super-admin tenant edit.
--
-- Backfill: existing tenants are switched OFF. Normally we would leave live settings alone, but the
-- waitlist has never been used (zero event_waitlist rows in any environment), so nothing is being
-- taken away from anyone, and leaving the one QA tenant's flag on would contradict the whole point
-- of the default. Any track that actually wants a waitlist gets it switched on deliberately.
--
-- Idempotent: guarded ADD COLUMN, a DEFAULT change that is a no-op on re-run, and a backfill that
-- only touches rows still holding the old default.

-- Pre-pay: charge the rider the tier price when they JOIN the waitlist, then auto-confirm them
-- into a ticket when a spot opens (WaitlistPromoter) instead of texting them a pay-now link.
-- Inert unless waitlist_enabled is also true. Off by default and off for every existing tenant.
ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS waitlist_prepay_enabled boolean NOT NULL DEFAULT false;

-- New tracks no longer get a waitlist unless someone asks for one.
ALTER TABLE tenant
    ALTER COLUMN waitlist_enabled SET DEFAULT false;

-- Switch off the tenants that only had it because of the old default. Re-running is a no-op once
-- the rows are false; a track that is later switched on deliberately is NOT re-disabled, because by
-- then this script has already been journaled and will not run again.
UPDATE tenant
   SET waitlist_enabled = false
 WHERE waitlist_enabled = true;
