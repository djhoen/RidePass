-- Per-event allow-list of day-pass products. Tenants pick which day-pass
-- products can be redeemed at each event so a "child pass" doesn't show up
-- on a Pro race day. Empty list = no day-pass reservation option for that
-- event (the rider sees no Reserve-a-pass button on the event modal).

CREATE TABLE event_day_pass_eligibility (
    event_id            uuid NOT NULL REFERENCES event(id) ON DELETE CASCADE,
    day_pass_product_id uuid NOT NULL REFERENCES day_pass_product(id) ON DELETE CASCADE,
    PRIMARY KEY (event_id, day_pass_product_id)
);

CREATE INDEX idx_event_day_pass_eligibility_product
    ON event_day_pass_eligibility (day_pass_product_id);
