-- Low-stock thresholds for F&B inventory items. When low_stock_threshold is set and on_hand falls to
-- or below it, the item is "low". low_stock_notified_at dedupes the manager alert so it fires once per
-- low episode (cleared when stock is replenished back above the threshold).
ALTER TABLE concession_inventory_item ADD COLUMN low_stock_threshold numeric(12,3) NULL;
ALTER TABLE concession_inventory_item ADD COLUMN low_stock_notified_at timestamptz NULL;
