-- Ticket ← funding-pass link for season-pass ride-credit accounting. A "credits" season pass
-- (ride pack) can now fund an event ticket at checkout: one credit is burned when the $0
-- ticket is created, and this column records WHICH pass paid so a refund (or a failed /
-- abandoned payment) can hand the credit back. Nullable — the overwhelming majority of
-- tickets are money-funded. Naming mirrors applied_reward_redemption_id (Script0029).
--
-- Additive, rerunnable, backwards-compatible (existing rows read as not-credit-funded).

ALTER TABLE event_ticket_purchase
    ADD COLUMN IF NOT EXISTS applied_season_pass_purchase_id uuid NULL
        REFERENCES season_pass_purchase(id) ON DELETE SET NULL;

-- Partial: only credit-funded tickets are ever looked up by pass (refund hand-back path).
CREATE INDEX IF NOT EXISTS idx_event_ticket_purchase_applied_pass
    ON event_ticket_purchase (applied_season_pass_purchase_id)
    WHERE applied_season_pass_purchase_id IS NOT NULL;
