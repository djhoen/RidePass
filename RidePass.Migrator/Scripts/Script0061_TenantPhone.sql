-- Tenant contact phone — shown in the home-page footer alongside the contact email.
-- Free text so tenants can format with parens, dashes, country code, etc.

ALTER TABLE tenant
    ADD COLUMN phone text NULL;
