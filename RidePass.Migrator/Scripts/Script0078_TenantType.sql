-- Tenant type: distinguishes the operating model so provisioning can seed
-- type-appropriate defaults. Two values for now; CHECK constraint keeps the
-- door open for new types (BMX, equestrian, etc.) without a schema change.
--
-- Existing tenants default to 'motocross' since that's what RidePass was
-- originally built for. The provisioning triggers below branch on tenant_type
-- so MTB tenants get a smaller event-type set + non-motorized waiver wording.

ALTER TABLE tenant
    ADD COLUMN tenant_type text NOT NULL DEFAULT 'motocross'
    CHECK (tenant_type IN ('motocross', 'mountain_bike'));

-- ── Event-type seeding ───────────────────────────────────────────────────
-- MX keeps the existing six (open_ride, race, practice, lesson, private_booking, other).
-- MTB gets the two the operator asked for (race, practice). Admins can add
-- more from Settings → Event Types.
CREATE OR REPLACE FUNCTION seed_default_event_types()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.tenant_type = 'mountain_bike' THEN
        INSERT INTO tenant_event_type (tenant_id, code, name, color, sort_order, is_system) VALUES
            (NEW.id, 'race',     'Race',     '#D32F2F', 10, true),
            (NEW.id, 'practice', 'Practice', '#388E3C', 20, true)
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

-- ── Waiver seeding ───────────────────────────────────────────────────────
-- Body is obvious-placeholder so admins know to replace before going live.
-- Wording differs because the liability conversation differs between
-- motorized and non-motorized venues — replacing wholesale is fine, but
-- starting from approximately-right boilerplate beats starting from blank.
CREATE OR REPLACE FUNCTION seed_initial_waiver()
RETURNS TRIGGER AS $$
DECLARE
    body_text text;
BEGIN
    IF NEW.tenant_type = 'mountain_bike' THEN
        body_text :=
            E'PLACEHOLDER WAIVER — REPLACE BEFORE GOING LIVE.\n\n' ||
            E'Mountain biking, trail riding, and related activities involve inherent risks of serious injury or death. ' ||
            E'By participating in any activity at this venue, the rider acknowledges and voluntarily assumes these risks. ' ||
            E'This is placeholder text — replace it with your venue''s legally-reviewed waiver before publishing.';
    ELSE
        body_text :=
            E'PLACEHOLDER WAIVER — REPLACE BEFORE GOING LIVE.\n\n' ||
            E'Motorsports activities involve inherent risks of serious injury or death. ' ||
            E'By participating in any activity at this venue, the rider acknowledges and voluntarily assumes these risks. ' ||
            E'This is placeholder text — replace it with your venue''s legally-reviewed waiver before publishing.';
    END IF;
    INSERT INTO tenant_waiver (tenant_id, version, title, body, is_active)
    VALUES (NEW.id, 1, 'Waiver & Release of Liability', body_text, true)
    ON CONFLICT DO NOTHING;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ── Pass product seeding ─────────────────────────────────────────────────
-- Inserts an inactive Day Pass placeholder so admins immediately see the
-- shape they need to fill in. is_active=false keeps it off the public catalog
-- until the operator reviews price/copy.
CREATE OR REPLACE FUNCTION seed_default_pass_products()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO pass_product (tenant_id, name, description, price_cents, is_active, sort_order, requires_waiver)
    VALUES (NEW.id, 'Day Pass',
            'Single-day access. Replace this description and price before activating.',
            100, false, 10, true);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_tenant_insert_pass_products
    AFTER INSERT ON tenant
    FOR EACH ROW EXECUTE FUNCTION seed_default_pass_products();
