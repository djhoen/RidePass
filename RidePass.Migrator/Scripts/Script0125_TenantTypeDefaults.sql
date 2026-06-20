-- Smarter per-tenant-type provisioning defaults. Adds an MTB venue category and
-- branches the seed triggers so a new tenant starts with sensible event types and
-- add-on products for what they actually are (MX track vs MTB bike park / shuttle /
-- resort). Only affects tenants created after this migration; the seed functions are
-- CREATE OR REPLACE (no data change).

-- MTB sub-classification. NULL for MX (and legacy) tenants.
ALTER TABLE tenant ADD COLUMN venue_category text;
ALTER TABLE tenant ADD CONSTRAINT tenant_venue_category_chk
    CHECK (venue_category IS NULL OR venue_category IN ('bike_park', 'shuttle', 'resort'));

-- ── Event types ──────────────────────────────────────────────────────────────
-- MX unchanged (6). MTB expands from 2 -> 4, with the access-day name following the
-- venue category but keeping the open_ride/race/practice codes so apex discovery
-- filters still match.
CREATE OR REPLACE FUNCTION seed_default_event_types() RETURNS trigger AS $$
BEGIN
    IF NEW.tenant_type = 'mountain_bike' THEN
        INSERT INTO tenant_event_type (tenant_id, code, name, color, sort_order, is_system) VALUES
            (NEW.id, 'open_ride',
                CASE NEW.venue_category WHEN 'shuttle' THEN 'Shuttle Day' WHEN 'resort' THEN 'Lift Day' ELSE 'Trail Day' END,
                '#1976D2', 10, true),
            (NEW.id, 'race',     'Race',     '#D32F2F', 20, true),
            (NEW.id, 'practice', 'Practice', '#388E3C', 30, true),
            (NEW.id, 'lesson',   'Clinic',   '#7B1FA2', 40, true)
        ON CONFLICT (tenant_id, code) DO NOTHING;
    ELSE
        INSERT INTO tenant_event_type (tenant_id, code, name, color, sort_order, is_system) VALUES
            (NEW.id, 'open_ride',       'Open Ride',       '#1976D2', 10, true),
            (NEW.id, 'race',             'Race',            '#D32F2F', 20, true),
            (NEW.id, 'practice',         'Practice',        '#388E3C', 30, true),
            (NEW.id, 'lesson',           'Lesson',          '#7B1FA2', 40, true),
            (NEW.id, 'private_booking',  'Private Booking', '#F57C00', 50, true),
            (NEW.id, 'other',            'Other',           '#616161', 60, true)
        ON CONFLICT (tenant_id, code) DO NOTHING;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ── Add-on products ──────────────────────────────────────────────────────────
-- The first product is the venue's access product (named by type/category); then
-- Parking + Camping. Fixes the old "Pit Vehicle for a bike park" default. Prices are
-- placeholders the tenant edits. requires_waiver=false, matching the prior seed.
CREATE OR REPLACE FUNCTION seed_default_extra_products() RETURNS trigger AS $$
BEGIN
    IF NEW.tenant_type = 'mountain_bike' THEN
        INSERT INTO event_extra_product (tenant_id, name, kind, price_cents, requires_waiver, sort_order)
        VALUES (NEW.id,
            CASE NEW.venue_category WHEN 'shuttle' THEN 'Shuttle Pass' WHEN 'resort' THEN 'Lift Ticket' ELSE 'Day Pass' END,
            CASE NEW.venue_category WHEN 'shuttle' THEN 'shuttle'      WHEN 'resort' THEN 'lift'        ELSE 'day_pass' END,
            CASE NEW.venue_category WHEN 'shuttle' THEN 5500           WHEN 'resort' THEN 5000          ELSE 4000 END,
            false, 5);
        INSERT INTO event_extra_product (tenant_id, name, kind, price_cents, requires_waiver, sort_order) VALUES
            (NEW.id, 'Parking', 'parking', 1000, false, 20),
            (NEW.id, 'Camping', 'camping', 2500, false, 30);
    ELSE
        INSERT INTO event_extra_product (tenant_id, name, kind, price_cents, requires_waiver, sort_order) VALUES
            (NEW.id, 'Gate Fee',    'gate_fee',    1000, false, 5),
            (NEW.id, 'Camping',     'camping',     2500, false, 10),
            (NEW.id, 'Parking',     'parking',     1000, false, 20),
            (NEW.id, 'Pit Vehicle', 'pit_vehicle', 2000, false, 30);
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ── Membership name by type ──────────────────────────────────────────────────
-- MTB tenants get "Park Membership" instead of the "Track Membership" column default
-- (only when still the default, so a custom name is never clobbered).
CREATE OR REPLACE FUNCTION set_tenant_type_membership_name() RETURNS trigger AS $$
BEGIN
    IF NEW.tenant_type = 'mountain_bike'
       AND (NEW.membership_name IS NULL OR NEW.membership_name = 'Track Membership') THEN
        NEW.membership_name := 'Park Membership';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_tenant_type_membership_name ON tenant;
CREATE TRIGGER trg_tenant_type_membership_name
    BEFORE INSERT ON tenant
    FOR EACH ROW EXECUTE FUNCTION set_tenant_type_membership_name();
