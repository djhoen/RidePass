-- Persist the actual handwritten signature alongside the timestamp/IP audit row.
-- Stored as a base64 PNG data URL so the browser can render it directly.

ALTER TABLE rider_waiver_signature
    ADD COLUMN signature_data_url text NULL;
