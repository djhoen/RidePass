-- Speed up the date-range Rider/Spectator report. Two gaps, both additive + IF NOT EXISTS:
--
-- 1. event_ticket_purchase(tier_id): there was no plain index on the tier FK, so the
--    planner could only drive the report from a full scan of the tenant's purchases.
--    With this, a bounded date range can start from the window's events (idx_event_tenant_starts)
--    -> their tiers (idx_event_ticket_tier_event) -> their purchases, pruning by date instead
--    of scanning every purchase. This is the big win for the spectator report, whose rows are a
--    small subset that the old plan had to scan far to find.
CREATE INDEX IF NOT EXISTS idx_event_ticket_purchase_tier
    ON event_ticket_purchase (tier_id);

-- 2. rider_waiver_signature(tenant_id, lower(signer_email)): the report's per-row waiver-coverage
--    check matches a guest signature by email, but the existing signer_email index leads with
--    waiver_id, so an email-only lookup couldn't use it. This functional index covers it.
CREATE INDEX IF NOT EXISTS idx_rider_waiver_sig_tenant_signer_email
    ON rider_waiver_signature (tenant_id, lower(signer_email))
    WHERE signer_email IS NOT NULL;
