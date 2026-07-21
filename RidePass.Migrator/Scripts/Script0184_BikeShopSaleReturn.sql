-- Add 'sale_return' to the bike shop stock-movement reasons.
--
-- A refund WITH restock (the customer brought the item back) reverses a sale's depletion: pool
-- stock goes back up, a serialized unit returns to 'available'. That movement is a return, not a
-- generic adjustment, so the audit trail should say so. Widening a CHECK can't invalidate existing
-- rows, and no code writes 'sale_return' until the refund path ships, so this is safe and additive.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'shop_stock_movement_reason_check') THEN
        ALTER TABLE shop_stock_movement DROP CONSTRAINT shop_stock_movement_reason_check;
    END IF;
    ALTER TABLE shop_stock_movement ADD CONSTRAINT shop_stock_movement_reason_check
        CHECK (reason IN ('receive','sale','rental_out','rental_return','repair_consume',
                          'adjustment','stocktake','transfer','sale_return'));
END $$;
