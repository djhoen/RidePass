-- Supply chain: reorder points, supplier lead time, and vendor part numbers.
--
-- low_stock_threshold already answers "shout at me", but nothing answers "how many do I buy?".
-- These are the two numbers every retail system in this category carries (Lightspeed calls them
-- Reorder Point and Reorder Level):
--
--   reorder_point = the floor. At or below it, the item wants reordering.
--   reorder_level = the target. Order enough to bring available stock back up to this.
--
-- Kept separate from low_stock_threshold on purpose: the threshold drives an ALERT (and the
-- low-stock filter), while these drive a PURCHASING decision. A shop often wants to be warned
-- earlier than it wants to place an order, and conflating them would force one to move with the
-- other. Both nullable: null = not managed, and the item simply never appears on the reorder list.

ALTER TABLE shop_variant
    ADD COLUMN IF NOT EXISTS reorder_point int NULL
        CHECK (reorder_point IS NULL OR reorder_point >= 0),
    ADD COLUMN IF NOT EXISTS reorder_level int NULL
        CHECK (reorder_level IS NULL OR reorder_level >= 0);

COMMENT ON COLUMN shop_variant.reorder_point IS
    'Stock floor. At or below this, the variant appears on the reorder list. NULL = not managed. Distinct from low_stock_threshold, which only drives the alert.';
COMMENT ON COLUMN shop_variant.reorder_level IS
    'Target stock to reorder back up to. Suggested qty = reorder_level - available - already on order. NULL = not managed.';

-- The vendor's own part number for this variant. Ascend searches its cloud catalog by VPN and
-- Lightspeed carries a Manufacturer SKU alongside the shop's own; without it, reconciling a
-- delivery against the supplier's packing slip and invoice is manual eyeballing.
ALTER TABLE shop_variant
    ADD COLUMN IF NOT EXISTS vendor_part_number text NULL;

COMMENT ON COLUMN shop_variant.vendor_part_number IS
    'The supplier''s part number for this variant (VPN/MPN), for matching packing slips and invoices at receiving.';

-- Typical days from placing an order with this supplier to it landing. Lets the reorder list say
-- "order by" rather than just "order", and flags a PO as late against its expected date.
ALTER TABLE shop_supplier
    ADD COLUMN IF NOT EXISTS lead_time_days int NULL
        CHECK (lead_time_days IS NULL OR (lead_time_days >= 0 AND lead_time_days <= 365));

COMMENT ON COLUMN shop_supplier.lead_time_days IS
    'Typical days from order to delivery for this supplier. NULL = unknown.';

-- Reorder list hot path: "which managed variants are at or below their point".
CREATE INDEX IF NOT EXISTS idx_shop_variant_reorder
    ON shop_variant (tenant_id) WHERE reorder_point IS NOT NULL AND is_active = true;
