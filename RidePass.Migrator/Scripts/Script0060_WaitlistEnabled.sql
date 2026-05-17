-- Waitlist feature toggle.
--
-- Default true so existing tenants keep their current behaviour. When off:
--   - rider "Join Waitlist" buttons are hidden in the UI
--   - WaitlistController.Join rejects new joins
-- The expiry worker keeps running either way — it just won't find new rows
-- to promote when joins are blocked.

ALTER TABLE tenant
    ADD COLUMN waitlist_enabled boolean NOT NULL DEFAULT true;
