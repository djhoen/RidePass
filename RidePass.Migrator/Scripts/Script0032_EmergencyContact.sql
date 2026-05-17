-- Emergency contact for the rider — captured at signup or counter create, surfaced to
-- gate staff during a sale, and editable by the rider on their profile. Nullable so
-- legacy accounts aren't broken; required at creation time going forward.

ALTER TABLE users
    ADD COLUMN emergency_contact_name text NULL,
    ADD COLUMN emergency_contact_phone text NULL;

-- Per-tenant policy: when on, riders without an emergency contact on file are blocked
-- from purchasing until they fill it in on their profile.
ALTER TABLE tenant
    ADD COLUMN require_emergency_contact boolean NOT NULL DEFAULT false;
