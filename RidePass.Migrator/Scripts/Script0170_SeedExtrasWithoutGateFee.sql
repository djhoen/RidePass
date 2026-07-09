-- Stop seeding a "Gate Fee" add-on for new motocross tenants. Script0117 made
-- gate fees first-class event ticket tiers (kind='gate_fee', rider/spectator
-- audience) and retired the gate-fee add-on kind in code -- checkout already
-- excludes add-ons of kind 'gate_fee'. Seeding one on every new MX tenant just
-- leaves a confusing dead product in the Add-ons admin page. MTB seeding is
-- unchanged; existing tenants' rows are left alone (Motoland renamed theirs and
-- linked it to an event, so a delete here would be destructive).

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
            (NEW.id, 'Camping',     'camping',     2500, false, 10),
            (NEW.id, 'Parking',     'parking',     1000, false, 20),
            (NEW.id, 'Pit Vehicle', 'pit_vehicle', 2000, false, 30);
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
