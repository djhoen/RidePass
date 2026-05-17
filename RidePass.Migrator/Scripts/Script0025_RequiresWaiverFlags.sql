-- Per-item opt-in for waiver signing. Defaulting TRUE preserves the prior behavior
-- where every item required the tenant's active waiver — tenants can flip individual
-- items off (e.g. spectator tickets, merch) once the column is in place.

ALTER TABLE day_pass_product
    ADD COLUMN requires_waiver boolean NOT NULL DEFAULT true;

ALTER TABLE event
    ADD COLUMN requires_waiver boolean NOT NULL DEFAULT true;
