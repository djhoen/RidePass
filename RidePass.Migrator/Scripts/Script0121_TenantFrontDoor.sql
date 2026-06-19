-- Front-door config for embedded / custom-domain clients.
--   external_home_url / external_events_url: an embedded client's own website pages.
--     Used for the {subdomain}.ridepass.io redirect and for apex link targeting.
--   custom_domain_verified: gates the custom-domain redirect — we only forward the
--     subdomain to the custom domain once that domain actually serves (DNS + TLS +
--     host resolution), flipped by the verification flow in a later phase. Default
--     false so entering a domain string changes no behavior.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS external_home_url text NULL;
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS external_events_url text NULL;
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS custom_domain_verified boolean NOT NULL DEFAULT false;
