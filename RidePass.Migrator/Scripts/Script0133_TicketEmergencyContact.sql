-- Per-rider emergency contact captured during post-payment event registration (not as a
-- pre-payment gate). The buyer enters one rider per registrant in the unified checkout /
-- resume flow, alongside identity + waiver, so the emergency contact belongs on the
-- ticket purchase next to the rest of the rider details rather than on the buyer profile.
-- Nullable: only enforced (per RegistrantRegistrationItem) when the tenant has
-- require_emergency_contact on and the registrant holds a rider-audience ticket.

ALTER TABLE event_ticket_purchase
    ADD COLUMN emergency_contact_name text NULL,
    ADD COLUMN emergency_contact_phone text NULL;
