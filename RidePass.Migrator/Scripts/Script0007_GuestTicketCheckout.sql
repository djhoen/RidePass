-- Allow guest checkout on event ticket purchases.
-- Day passes remain tied to a user account (required by waiver flow).

ALTER TABLE event_ticket_purchase
    ALTER COLUMN purchaser_user_id DROP NOT NULL;
