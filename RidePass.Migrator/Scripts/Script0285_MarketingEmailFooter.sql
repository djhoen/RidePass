-- A footer the tenant writes once (hours, a tagline, a legal line) that goes on every marketing
-- email (campaigns and automations), above the automatic name/address/socials block. Editor
-- HTML, same as the email body; prepared for email at send time. NULL = nothing extra.
-- Additive and rerunnable.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS marketing_email_footer_html text NULL;
