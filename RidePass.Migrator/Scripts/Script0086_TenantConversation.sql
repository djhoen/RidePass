-- Inbound conversations: when a customer texts the tenant's provisioned
-- toll-free number, Twilio POSTs the IncomingSms webhook (next sub-phase),
-- which find-or-creates a conversation row and appends a tenant_message.
-- Outbound replies from the admin Inbox UI (and, in sub-phase C, the existing
-- notification paths like waitlist promos and rider messages) write to the
-- same tenant_message table so threads show both sides of the exchange.
--
-- Scope shape:
--   • Conversation: keyed by (tenant_id, customer_phone) — one ongoing thread
--     per phone-number pair. Customer_user_id is a soft link (no FK) since
--     the texter might not have a RidePass account, and we don't want a
--     deleted user to wipe their conversation history.
--   • Message: child rows ordered by created_at_utc. Direction discriminates
--     inbound vs outbound. twilio_message_sid is unique when set so duplicate
--     webhook deliveries don't double-insert.

CREATE TABLE tenant_conversation (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,

    -- E.164 phone of the customer. (tenant_id, customer_phone) is unique so
    -- repeated texts from the same number land in the same thread.
    customer_phone text NOT NULL,

    -- Soft link to the users table — populated when the inbound number
    -- matches a known rider's profile. Stays nullable to support customers
    -- who don't have an account yet.
    customer_user_id uuid NULL,

    -- Updated on every message (in or out). Drives the conversation-list sort.
    last_message_at_utc timestamptz NOT NULL DEFAULT now(),

    -- Last time the CUSTOMER wrote. Combined with last_read_at_utc this is
    -- how the admin Inbox UI computes the "unread" flag without an explicit
    -- counter (which would need careful increment/decrement plumbing).
    last_inbound_at_utc timestamptz NULL,

    last_read_at_utc timestamptz NULL,

    -- 'active' (default) | 'archived'. Archived conversations stay in the DB
    -- but drop out of the default Inbox list — admin can re-open from a
    -- "show archived" toggle.
    status text NOT NULL DEFAULT 'active',

    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_tenant_conversation_tenant_phone
    ON tenant_conversation(tenant_id, customer_phone);

CREATE INDEX ix_tenant_conversation_tenant_last_msg
    ON tenant_conversation(tenant_id, last_message_at_utc DESC);

CREATE TABLE tenant_message (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id uuid NOT NULL REFERENCES tenant_conversation(id) ON DELETE CASCADE,

    -- Denormalised from the conversation for fast tenant-scoped reads
    -- (e.g., "all of this tenant's recent SMS for billing reconciliation").
    -- Trigger-enforced consistency would be nicer but conversation_id is the
    -- only write path so application code keeps them in sync.
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,

    direction text NOT NULL,                  -- 'inbound' | 'outbound'
    body text NOT NULL,

    -- Twilio Message SID. Globally unique within Twilio; UNIQUE WHERE NOT NULL
    -- so duplicate webhook deliveries (Twilio retries) don't double-insert.
    twilio_message_sid text NULL,

    -- 'received' for inbound terminal state.
    -- 'queued' | 'sent' | 'delivered' | 'failed' | 'undelivered' for outbound.
    -- Outbound rows start at 'queued', advance as StatusCallback fires.
    status text NOT NULL,

    -- Twilio reports segment count on terminal callbacks. Stored so the
    -- conversation thread can show per-message cost ("3 segments · $0.06").
    num_segments int NULL,

    -- Outbound only: which admin user clicked send. Null for system-sent
    -- (waitlist promotion, scheduled rider message, etc.) — those have
    -- source_kind / source_id in a future column if we need to attribute them.
    sent_by_user_id uuid NULL,

    error_code text NULL,
    error_message text NULL,

    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_tenant_message_twilio_sid
    ON tenant_message(twilio_message_sid)
    WHERE twilio_message_sid IS NOT NULL;

CREATE INDEX ix_tenant_message_conversation_created
    ON tenant_message(conversation_id, created_at_utc);

CREATE INDEX ix_tenant_message_tenant_created
    ON tenant_message(tenant_id, created_at_utc DESC);
