-- Optional weekly operating hours for online Food & Beverage ordering, evaluated in the tenant's
-- timezone. NULL = always open (no schedule set), preserving current behavior. Stored as a JSON array
-- of 7 entries (index 0 = Sunday ... 6 = Saturday), each { open, openMinute, closeMinute }.
ALTER TABLE concession_menu_settings ADD COLUMN IF NOT EXISTS ordering_hours jsonb NULL;
