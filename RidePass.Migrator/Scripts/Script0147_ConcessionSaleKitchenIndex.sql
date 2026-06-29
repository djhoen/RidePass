-- Cook-screen poll runs every few seconds per tenant (many tenants on a busy day), filtering
-- concession_sale by tenant + the live set (paid, not completed). A partial index keeps it tiny
-- (only live orders) and makes each poll an index lookup, ordered by the called-out number.
CREATE INDEX IF NOT EXISTS idx_concession_sale_kitchen
    ON concession_sale (tenant_id, order_number)
    WHERE status = 'paid' AND fulfillment_status <> 'completed';
