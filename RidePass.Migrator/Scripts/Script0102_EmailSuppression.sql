-- Universal "do not send" list, the compliance backbone for email. Fed by SES
-- bounce/complaint events, one-click unsubscribes, and manual admin actions. The
-- send path checks this before delivering.
--
-- scope:  'all'       hard bounce / invalid address -> block EVERYTHING to it.
--         'marketing' unsubscribe / complaint        -> block marketing only,
--                                                       transactional (receipts,
--                                                       verification) still send.
-- tenant_id NULL = platform-wide (a hard bounce is invalid for every tenant);
--           set  = scoped to one tenant (an unsubscribe from that track's mail).
CREATE TABLE email_suppression (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NULL REFERENCES tenant(id) ON DELETE CASCADE,
    email       text        NOT NULL,
    reason      text        NOT NULL CHECK (reason IN ('bounce', 'complaint', 'unsubscribe', 'manual')),
    scope       text        NOT NULL CHECK (scope IN ('all', 'marketing')),
    source      text        NULL,       -- e.g. 'ses_bounce', 'ses_complaint', 'one_click', 'admin'
    detail      text        NULL,       -- bounce subtype / diagnostic / note
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Fast membership check by address.
CREATE INDEX idx_email_suppression_email ON email_suppression (lower(email));

-- Dedupe: one row per (tenant-or-global, address, scope). The sentinel handles the
-- NULL tenant so ON CONFLICT DO NOTHING works for platform-wide rows too.
CREATE UNIQUE INDEX uk_email_suppression
    ON email_suppression (COALESCE(tenant_id, '00000000-0000-0000-0000-000000000000'::uuid), lower(email), scope);
