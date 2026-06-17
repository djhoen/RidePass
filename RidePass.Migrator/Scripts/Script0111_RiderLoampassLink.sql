-- Links a RidePass rider account to their LoamMx (LoamPassMx) account(s) so they can redeem
-- Loam Pass credits for entry. ONE RidePass rider can connect MANY LoamMx accounts (1-to-many),
-- so the uniqueness is per (user, loampass account), not per user. Riders are per-tenant, so
-- tenant_id is carried for scoped reads/writes. The LoamMx account id + email are stored after
-- the rider confirms an emailed verification code.

CREATE TABLE rider_loampass_link (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id             uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    loampass_account_id text        NOT NULL,
    loampass_email      text        NOT NULL,
    linked_at_utc       timestamptz NOT NULL DEFAULT now()
);
-- A given LoamMx account can be linked once per rider; a rider may link several accounts.
CREATE UNIQUE INDEX uk_rider_loampass_link_user_account ON rider_loampass_link (user_id, loampass_account_id);
CREATE INDEX idx_rider_loampass_link_tenant ON rider_loampass_link (tenant_id);
