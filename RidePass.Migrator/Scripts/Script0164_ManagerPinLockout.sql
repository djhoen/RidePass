-- Brute-force protection for the manager authorization PIN. Tracks failed verify attempts per
-- (tenant, requesting staff user) so the PIN gate can lock out after repeated wrong guesses. The PIN
-- itself stays on users.pos_pin_hash (Script0161); this only records attempt state.
--
-- Idempotent (rerunnable) and additive.

CREATE TABLE IF NOT EXISTS manager_pin_attempt (
    tenant_id    uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id      uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,  -- the staff member entering the PIN
    failed_count int         NOT NULL DEFAULT 0,
    locked_until timestamptz NULL,
    updated_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, user_id)
);
