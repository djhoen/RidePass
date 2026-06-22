-- Cash reconciliation for the operator (gate) app.
--
-- Cash sales/refunds are already attributed to the worker (sold_by_user_id) and
-- recorded in the ledger with payment_method = 'cash', so we do NOT put a session FK
-- on the sale path. Instead a cash_session is a per-worker bookkeeping envelope for a
-- shift at an event; "expected cash" is derived from that worker's cash sales minus
-- cash refunds within the session window (computed in the reconciliation report).
--
-- A turn-in is a BLIND count: the worker counts without seeing expected_cents, hands
-- the cash to a manager, and the manager confirms receipt FROM THEIR OWN login (no
-- manager PIN on the hand-off; the PIN is reserved for refunds). The manager's count
-- sets variance_cents (manager_counted - expected). The real anti-skim control is
-- attribution + blind count + variance visibility + audit, not a synchronous approval.

-- A worker's cash-handling session for a shift (optionally tied to an event/day).
CREATE TABLE cash_session (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    event_id            uuid        NULL REFERENCES event(id) ON DELETE SET NULL,
    user_id             uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id           text        NULL,
    opening_float_cents int         NOT NULL DEFAULT 0 CHECK (opening_float_cents >= 0),
    status              text        NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'turned_in', 'closed')),
    opened_at           timestamptz NOT NULL DEFAULT now(),
    closed_at           timestamptz NULL
);
CREATE INDEX idx_cash_session_tenant_event ON cash_session (tenant_id, event_id);
CREATE INDEX idx_cash_session_open ON cash_session (tenant_id, user_id, status);
-- At most one OPEN session per worker per event (event_id NULL collapses to a sentinel
-- so a worker can't open two no-event sessions at once either).
CREATE UNIQUE INDEX uk_cash_session_open
    ON cash_session (tenant_id, user_id, COALESCE(event_id, '00000000-0000-0000-0000-000000000000'::uuid))
    WHERE status = 'open';

-- A blind-count turn-in from a worker to a manager. manager_* fields stay NULL until
-- the manager confirms receipt; variance is set at confirm time.
CREATE TABLE cash_turn_in (
    id                   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    cash_session_id      uuid        NOT NULL REFERENCES cash_session(id) ON DELETE CASCADE,
    event_id             uuid        NULL REFERENCES event(id) ON DELETE SET NULL,
    worker_user_id       uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    manager_user_id      uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    expected_cents       int         NULL,
    worker_counted_cents int         NOT NULL CHECK (worker_counted_cents >= 0),
    manager_counted_cents int        NULL CHECK (manager_counted_cents IS NULL OR manager_counted_cents >= 0),
    variance_cents       int         NULL,
    status               text        NOT NULL DEFAULT 'submitted' CHECK (status IN ('submitted', 'confirmed', 'disputed')),
    note                 text        NULL,
    submitted_at         timestamptz NOT NULL DEFAULT now(),
    confirmed_at         timestamptz NULL
);
CREATE INDEX idx_cash_turn_in_tenant_event ON cash_turn_in (tenant_id, event_id);
CREATE INDEX idx_cash_turn_in_worker ON cash_turn_in (tenant_id, worker_user_id, submitted_at DESC);
CREATE INDEX idx_cash_turn_in_session ON cash_turn_in (cash_session_id);
-- Managers poll for turn-ins still awaiting their confirmation.
CREATE INDEX idx_cash_turn_in_pending ON cash_turn_in (tenant_id, event_id) WHERE status = 'submitted';
