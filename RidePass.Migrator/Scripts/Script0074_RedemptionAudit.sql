-- Audit columns for redemption + counter-sale events. Until now most flows
-- only flipped status='redeemed' and relied on updated_at — this gives us
-- proper "who and when" tracking on every gate scan / pass check-in / counter
-- sale, matching what event_extra_purchase already does.
--
--   * pass_purchase, event_ticket_purchase: redeemed_at_utc + redeemed_by_user_id
--   * season_pass_reservation:              checked_in_by_user_id (timestamp already exists)
--   * pass_purchase, event_ticket_purchase, event_extra_purchase, membership_purchase:
--                                            sold_by_user_id (cashier on a counter sale)

ALTER TABLE pass_purchase
    ADD COLUMN redeemed_at_utc     timestamptz NULL,
    ADD COLUMN redeemed_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN sold_by_user_id     uuid        NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE event_ticket_purchase
    ADD COLUMN redeemed_at_utc     timestamptz NULL,
    ADD COLUMN redeemed_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN sold_by_user_id     uuid        NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE event_extra_purchase
    ADD COLUMN sold_by_user_id     uuid        NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE membership_purchase
    ADD COLUMN sold_by_user_id     uuid        NULL REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE season_pass_reservation
    ADD COLUMN checked_in_by_user_id uuid      NULL REFERENCES users(id) ON DELETE SET NULL;

CREATE INDEX idx_pass_purchase_sold_by         ON pass_purchase (sold_by_user_id) WHERE sold_by_user_id IS NOT NULL;
CREATE INDEX idx_event_ticket_purchase_sold_by ON event_ticket_purchase (sold_by_user_id) WHERE sold_by_user_id IS NOT NULL;
CREATE INDEX idx_event_extra_purchase_sold_by  ON event_extra_purchase (sold_by_user_id) WHERE sold_by_user_id IS NOT NULL;
CREATE INDEX idx_membership_purchase_sold_by   ON membership_purchase (sold_by_user_id) WHERE sold_by_user_id IS NOT NULL;
