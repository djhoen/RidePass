-- RidePass initial schema: tenants, users, updated_at trigger.
-- Snake_case, UUID PKs, Postgres-idiomatic.

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE TABLE tenant (
    id           uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    subdomain    text        NOT NULL UNIQUE,
    display_name text        NOT NULL,
    status       text        NOT NULL DEFAULT 'active'
                             CHECK (status IN ('active','suspended','pending')),
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_tenant_subdomain ON tenant (subdomain);

CREATE TABLE users (
    id            uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id     uuid        NULL REFERENCES tenant(id) ON DELETE CASCADE,
    email         text        NOT NULL,
    password_hash text        NOT NULL,
    first_name    text        NOT NULL,
    last_name     text        NOT NULL,
    role          text        NOT NULL
                              CHECK (role IN ('super_admin','tenant_admin','tenant_staff','rider')),
    status        text        NOT NULL DEFAULT 'active'
                              CHECK (status IN ('active','suspended','pending')),
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),

    -- super_admin has no tenant; all other roles must belong to a tenant
    CONSTRAINT chk_user_tenant_scope CHECK (
        (role = 'super_admin' AND tenant_id IS NULL)
        OR (role <> 'super_admin' AND tenant_id IS NOT NULL)
    )
);

-- Email unique within a tenant (same email may exist across tenants)
CREATE UNIQUE INDEX idx_users_email_per_tenant
    ON users (tenant_id, LOWER(email))
    WHERE tenant_id IS NOT NULL;

-- Super-admin emails are globally unique (no tenant)
CREATE UNIQUE INDEX idx_users_email_super_admin
    ON users (LOWER(email))
    WHERE tenant_id IS NULL;

CREATE INDEX idx_users_tenant_id ON users (tenant_id);

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_tenant_updated_at
    BEFORE UPDATE ON tenant
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_users_updated_at
    BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
