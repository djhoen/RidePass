-- Bike shop sale numbers: a per-tenant, per-local-day counter, mirroring concession_order_counter.
--
-- A retail receipt wants a short human number ("Sale #14 today"), reset each business day in the
-- tenant's timezone. The counter is bumped atomically on the paid transition (cash at the counter,
-- card in the finalizer), so numbers are gap-free per day and never collide under a webhook race.
CREATE TABLE IF NOT EXISTS shop_order_counter (
    tenant_id     uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    business_date date NOT NULL,
    last_number   int  NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, business_date)
);
