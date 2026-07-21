-- Catalog tier-2 fields:
--   shop_variant.msrp_cents   Manufacturer's suggested retail price, shown as a compare-at. Null = none.
--   shop_variant.mpn          Manufacturer part number (VPN/vendor part number already exists as
--                             vendor_part_number). Null = none.
--   shop_product.is_published Whether the product is listed in the online store. Distinct from
--                             is_sellable (sellable at the counter): a shop can sell something at the
--                             register without listing it online. Defaults TRUE so every currently
--                             sellable product keeps showing online exactly as before.
--
-- Additive, rerunnable, backwards-compatible.

ALTER TABLE shop_variant
    ADD COLUMN IF NOT EXISTS msrp_cents integer;

ALTER TABLE shop_variant
    ADD COLUMN IF NOT EXISTS mpn text;

ALTER TABLE shop_product
    ADD COLUMN IF NOT EXISTS is_published boolean NOT NULL DEFAULT true;
