-- Deleting a concession product or variant that has sales must be blocked (admin should set it
-- inactive instead), the same as extras and rentals. The concession_sale_line FKs were created
-- ON DELETE SET NULL, so a delete silently succeeded and orphaned the sale-line references , and
-- SumSoldVariant, which joins on variant_id, then dropped those rows from sold counts. The delete
-- handlers in ConcessionController already catch the 23503 FK violation and return "has sales on
-- file ... set inactive instead", they just never fired because nothing restricted. Flip both FKs
-- to ON DELETE RESTRICT so the guard actually works.
--
-- The product->variant cascade (concession_variant.product_id ON DELETE CASCADE) stays: deleting a
-- product with no sales still removes its variants, but if a variant has sale lines the variant_id
-- RESTRICT blocks the cascade and the whole product delete fails (caught as "has sales").
--
-- Existing rows are unaffected: any already-orphaned (nulled) refs stay null, and re-adding the FK
-- validates only non-null references, which still point at live rows.

ALTER TABLE concession_sale_line DROP CONSTRAINT IF EXISTS concession_sale_line_product_id_fkey;
ALTER TABLE concession_sale_line ADD CONSTRAINT concession_sale_line_product_id_fkey
    FOREIGN KEY (product_id) REFERENCES concession_product(id) ON DELETE RESTRICT;

ALTER TABLE concession_sale_line DROP CONSTRAINT IF EXISTS concession_sale_line_variant_id_fkey;
ALTER TABLE concession_sale_line ADD CONSTRAINT concession_sale_line_variant_id_fkey
    FOREIGN KEY (variant_id) REFERENCES concession_variant(id) ON DELETE RESTRICT;
