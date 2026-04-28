-- Phase 3 scheduling schema: tenant timezone, event types, events, blackouts.

ALTER TABLE tenant ADD COLUMN timezone text NOT NULL DEFAULT 'UTC';

-- Per-tenant event types. System defaults are auto-seeded on tenant insert.
CREATE TABLE tenant_event_type (
    id          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    code        text        NOT NULL,
    name        text        NOT NULL,
    color       text        NOT NULL DEFAULT '#1976D2',
    sort_order  int         NOT NULL DEFAULT 100,
    is_system   boolean     NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uk_tenant_event_type UNIQUE (tenant_id, code)
);

CREATE INDEX idx_tenant_event_type_tenant ON tenant_event_type (tenant_id);

CREATE TRIGGER trg_tenant_event_type_updated_at
    BEFORE UPDATE ON tenant_event_type
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Events
CREATE TABLE event (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    event_type_id   uuid        NOT NULL REFERENCES tenant_event_type(id) ON DELETE RESTRICT,
    title           text        NOT NULL,
    description     text        NULL,
    starts_at       timestamptz NOT NULL,
    ends_at         timestamptz NOT NULL,
    all_day         boolean     NOT NULL DEFAULT false,
    capacity        int         NULL CHECK (capacity IS NULL OR capacity > 0),
    location_label  text        NULL,
    status          text        NOT NULL DEFAULT 'scheduled' CHECK (status IN ('scheduled','cancelled')),
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_event_range CHECK (ends_at >= starts_at)
);

CREATE INDEX idx_event_tenant_starts ON event (tenant_id, starts_at);
CREATE INDEX idx_event_tenant_range ON event (tenant_id, ends_at);

CREATE TRIGGER trg_event_updated_at
    BEFORE UPDATE ON event
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Blackouts (track closures)
CREATE TABLE blackout (
    id          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    starts_at   timestamptz NOT NULL,
    ends_at     timestamptz NOT NULL,
    all_day     boolean     NOT NULL DEFAULT false,
    reason      text        NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_blackout_range CHECK (ends_at >= starts_at)
);

CREATE INDEX idx_blackout_tenant_starts ON blackout (tenant_id, starts_at);

CREATE TRIGGER trg_blackout_updated_at
    BEFORE UPDATE ON blackout
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Seed default event types on tenant insert.
CREATE OR REPLACE FUNCTION seed_default_event_types()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO tenant_event_type (tenant_id, code, name, color, sort_order, is_system) VALUES
        (NEW.id, 'open_ride',       'Open Ride',       '#1976D2', 10, true),
        (NEW.id, 'race',             'Race',            '#D32F2F', 20, true),
        (NEW.id, 'practice',         'Practice',        '#388E3C', 30, true),
        (NEW.id, 'lesson',           'Lesson',          '#7B1FA2', 40, true),
        (NEW.id, 'private_booking',  'Private Booking', '#F57C00', 50, true),
        (NEW.id, 'other',            'Other',           '#616161', 60, true)
    ON CONFLICT (tenant_id, code) DO NOTHING;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_tenant_insert_event_types
    AFTER INSERT ON tenant
    FOR EACH ROW EXECUTE FUNCTION seed_default_event_types();

-- Backfill existing tenants.
INSERT INTO tenant_event_type (tenant_id, code, name, color, sort_order, is_system)
SELECT t.id, v.code, v.name, v.color, v.sort_order, true
FROM tenant t
CROSS JOIN (VALUES
    ('open_ride',       'Open Ride',       '#1976D2', 10),
    ('race',             'Race',            '#D32F2F', 20),
    ('practice',         'Practice',        '#388E3C', 30),
    ('lesson',           'Lesson',          '#7B1FA2', 40),
    ('private_booking',  'Private Booking', '#F57C00', 50),
    ('other',            'Other',           '#616161', 60)
) AS v(code, name, color, sort_order)
ON CONFLICT (tenant_id, code) DO NOTHING;
