-- Under-18 riders need a parent or guardian to sign on their behalf. We capture the
-- parent's name and phone alongside the signature; the signature image itself is the
-- parent's (the rider hands the device to their parent at the gate).

ALTER TABLE rider_waiver_signature
    ADD COLUMN signed_by_parent boolean NOT NULL DEFAULT false,
    ADD COLUMN parent_name text NULL,
    ADD COLUMN parent_phone text NULL;
