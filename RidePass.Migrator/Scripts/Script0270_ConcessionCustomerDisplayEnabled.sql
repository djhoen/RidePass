-- Tenant opt-in flag for the customer-facing POS display. When on, a POS register with no paired
-- (or non-syncing) display highlights its Display toolbar button so the cashier notices before the
-- first customer does. Default off: existing tenants see no change until they enable it.

ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS
    customer_display_enabled boolean NOT NULL DEFAULT false;
