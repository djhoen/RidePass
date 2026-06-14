-- Prevent duplicate ACTIVE pass-product names within a tenant (case-insensitive).
-- Duplicate passes with the same name render as repeated rows in the event "Day
-- Passes" / pricing lists and confuse riders. The friendly check lives in
-- PassProductController; this index is the hard backstop. Inactive products are
-- excluded so a name can be retired (deactivated) and a fresh one reused.
CREATE UNIQUE INDEX IF NOT EXISTS uk_pass_product_active_name
    ON pass_product (tenant_id, lower(name))
    WHERE is_active;
