-- Phase 9: Discovery. Tenants carry a physical location so riders can find tracks
-- near them. Lat/lng are nullable — a tenant without coordinates simply doesn't
-- appear in distance-bounded searches but still shows up in unbounded searches.

ALTER TABLE tenant ADD COLUMN address_line text NULL;
ALTER TABLE tenant ADD COLUMN city         text NULL;
ALTER TABLE tenant ADD COLUMN region       text NULL;
ALTER TABLE tenant ADD COLUMN postal_code  text NULL;
ALTER TABLE tenant ADD COLUMN country      text NULL;
ALTER TABLE tenant ADD COLUMN latitude     double precision NULL;
ALTER TABLE tenant ADD COLUMN longitude    double precision NULL;

CREATE INDEX idx_tenant_geo ON tenant (latitude, longitude)
    WHERE latitude IS NOT NULL AND longitude IS NOT NULL;
