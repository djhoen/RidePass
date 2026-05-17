-- Generic polymorphic scheduled-task table. The TaskRunner polls every minute
-- for due rows (status='pending' AND run_at_utc <= now()), atomically claims a
-- batch via FOR UPDATE SKIP LOCKED, dispatches each to its kind-specific
-- handler, and flips the row to succeeded / failed / cancelled.
--
-- This is the intended home for every deferred job — not just the rider-
-- message use case that prompted it. Examples expected to land here over time:
-- abandoned-cart sweep, gift-card scheduled delivery, password-reset cleanup,
-- audit-log retention, event-notification queue, large-import processor.
-- The existing MonthlyPayoutDrafter keeps its own monthly cadence (it's a
-- single tenant-spanning sweep, not a per-row job).
--
-- payload is jsonb so each handler defines its own shape. The dispatcher
-- doesn't peek inside; the handler validates + parses. A handler-registry
-- pattern (one C# class per kind, looked up by `kind` slug) keeps adding new
-- jobs to one new file + one DI registration.

CREATE TABLE scheduled_task (
    id                      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id               uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    kind                    text        NOT NULL,
    payload                 jsonb       NOT NULL,
    status                  text        NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'running', 'succeeded', 'failed', 'cancelled')),
    run_at_utc              timestamptz NOT NULL,
    attempts                int         NOT NULL DEFAULT 0,
    max_attempts            int         NOT NULL DEFAULT 3,
    last_error              text        NULL,
    result_summary          text        NULL,
    started_at_utc          timestamptz NULL,
    completed_at_utc        timestamptz NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    created_by_user_id      uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    cancelled_at_utc        timestamptz NULL,
    cancelled_by_user_id    uuid        NULL REFERENCES users(id) ON DELETE SET NULL
);

-- Dispatcher hot path — partial index keeps it tiny since the vast majority of
-- rows are not 'pending' at any given time. Run-at order means the dispatcher
-- naturally picks the most-overdue first.
CREATE INDEX idx_scheduled_task_dispatch
    ON scheduled_task (run_at_utc)
    WHERE status = 'pending';

-- Admin "what's scheduled / what just ran for this tenant" view.
CREATE INDEX idx_scheduled_task_tenant_created
    ON scheduled_task (tenant_id, created_at DESC);

-- "Pending tasks for this event" — used by the rider-report Scheduled panel.
-- Functional index keys on the payload's eventId so the lookup stays fast.
-- Only meaningful for kinds whose payload includes eventId (current:
-- send_rider_message). Other kinds simply won't appear in this lookup.
CREATE INDEX idx_scheduled_task_event_pending
    ON scheduled_task (tenant_id, (payload->>'eventId'))
    WHERE status = 'pending';
