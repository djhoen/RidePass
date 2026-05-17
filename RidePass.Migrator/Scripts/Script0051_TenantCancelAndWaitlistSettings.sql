-- Tenant settings for cancellation policy + waitlist confirm window.
--
-- allow_self_cancel: when true, riders can cancel their own purchases from
-- My Passes (refund honors service-charge rule). When false, the rider sees
-- a "request cancellation" form that fires a notification to tenant admins.
--
-- waitlist_confirm_window_minutes: how long a promoted alternate has to
-- confirm/pay before the spot rolls to the next person in line. Default 20.

ALTER TABLE tenant
    ADD COLUMN allow_self_cancel boolean NOT NULL DEFAULT false,
    ADD COLUMN waitlist_confirm_window_minutes int NOT NULL DEFAULT 20
        CHECK (waitlist_confirm_window_minutes BETWEEN 5 AND 240);
