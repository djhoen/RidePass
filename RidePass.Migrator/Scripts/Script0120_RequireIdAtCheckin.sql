-- Per-tenant gate policy: when on, the gate worker must attest that they checked the
-- rider's photo ID against the purchaser name before redeeming any of that order's
-- items. Pairs with the event+purchaser-scoped redemption (one scan surfaces every
-- ticket the rider owns for the event), giving tracks an identity check on the looser
-- scan. Default FALSE so existing tracks keep one-tap check-in until they opt in.

ALTER TABLE tenant
    ADD COLUMN require_id_at_checkin boolean NOT NULL DEFAULT false;
