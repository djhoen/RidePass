-- Logged-in riders/spectators can order + pay for concessions from the web app (online card),
-- separate from the in-venue counter. Attribute the sale to the purchaser and tag the channel so
-- the rider can see "their" orders + status and reporting can tell online from counter sales.

ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS purchaser_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS purchaser_email text NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS purchaser_name text NULL;
ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS order_channel text NOT NULL DEFAULT 'counter'
    CHECK (order_channel IN ('counter', 'online'));

-- The rider's "my orders" lookup.
CREATE INDEX IF NOT EXISTS idx_concession_sale_purchaser
    ON concession_sale (tenant_id, purchaser_user_id, created_at DESC)
    WHERE purchaser_user_id IS NOT NULL;
