-- Rentals phase 2: per-item condition photos + scheduled maintenance windows.
--
-- Condition photos: counter staff snaps a quick photo of each assigned per-item
-- unit at handout (and again at return) so we have visual evidence of condition
-- if a damage charge against the deposit is later disputed. Stored inline as a
-- base64 data URL — same approach as season-pass holder photos. Capped indirectly
-- by the data-url length validator on the API side.
--
-- Maintenance windows: replaces the current "set status='maintenance' forever"
-- workflow with a date-range model so admins can schedule a bike out for a
-- couple weekends without having to flip status back and forth. Capacity checks
-- exclude any item with an overlapping window.

ALTER TABLE rental_purchase_item
    ADD COLUMN checkout_photo_data_url text NULL,
    ADD COLUMN checkout_notes          text NULL,
    ADD COLUMN return_photo_data_url   text NULL,
    ADD COLUMN return_notes            text NULL;


CREATE TABLE rental_item_maintenance (
    id              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    item_id         uuid        NOT NULL REFERENCES rental_item(id) ON DELETE CASCADE,
    starts_at_date  date        NOT NULL,
    ends_at_date    date        NOT NULL CHECK (ends_at_date >= starts_at_date),
    reason          text        NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_rental_item_maintenance_item_window
    ON rental_item_maintenance (item_id, starts_at_date, ends_at_date);
CREATE TRIGGER trg_rental_item_maintenance_updated_at
    BEFORE UPDATE ON rental_item_maintenance
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
