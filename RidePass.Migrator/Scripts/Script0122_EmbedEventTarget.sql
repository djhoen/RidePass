-- For embedded clients: where an event click from the RidePass discovery site lands.
--   'external'  (default) — the track's own site (external_events_url, then external_home_url)
--   'ridepass'  — the RidePass-hosted event page ({subdomain}.ridepass.io/Event/:id),
--                 which also means the subdomain redirect must let /Event/:id render.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS embed_event_target text NOT NULL DEFAULT 'external'
    CHECK (embed_event_target IN ('external', 'ridepass'));
