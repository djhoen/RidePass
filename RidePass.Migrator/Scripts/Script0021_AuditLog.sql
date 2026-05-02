-- Append-only audit log for super-admin and tenant-admin write actions. Compliance + debugging.

CREATE TABLE audit_log (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    actor_user_id   uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    actor_email     text        NULL,         -- snapshot at write time so log survives user deletion
    actor_role      text        NULL,
    action          text        NOT NULL,     -- e.g., "tenant.create", "payout.markPaid"
    target_kind     text        NULL,         -- e.g., "tenant", "payout", "user"
    target_id       uuid        NULL,
    summary         text        NOT NULL,     -- human-readable one-liner
    metadata        jsonb       NULL,         -- kind-specific structured data
    ip_address      text        NULL,
    tenant_id       uuid        NULL REFERENCES tenant(id) ON DELETE SET NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_audit_log_created  ON audit_log (created_at DESC);
CREATE INDEX idx_audit_log_actor    ON audit_log (actor_user_id, created_at DESC);
CREATE INDEX idx_audit_log_tenant   ON audit_log (tenant_id, created_at DESC) WHERE tenant_id IS NOT NULL;
CREATE INDEX idx_audit_log_target   ON audit_log (target_kind, target_id, created_at DESC) WHERE target_kind IS NOT NULL;
CREATE INDEX idx_audit_log_action   ON audit_log (action, created_at DESC);
