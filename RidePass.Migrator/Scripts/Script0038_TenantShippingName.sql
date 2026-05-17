-- Free-form recipient name for shipping/mail addressed to the tenant. Distinct from
-- display_name (which is the public branded name the riders see) — this is the line
-- you'd put on the "Attn:" of a package label, e.g. "Acme MX – Office".

ALTER TABLE tenant
    ADD COLUMN shipping_name text NULL;
