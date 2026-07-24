-- Tenant-wide rental damage-waiver ("insurance"): an optional add-on offered at rental
-- checkout (bike-shop rentals and packages). When a renter buys it, they pay a
-- non-refundable fee = rental value * rate, and the refundable security deposit hold is
-- waived. Off by default for every tenant; opt in on Rentals -> Settings.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS rental_insurance_enabled boolean NOT NULL DEFAULT false;
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS rental_insurance_label   text;
-- Percent of the rented gear value, in basis points (1500 = 15%). 0 = no charge configured.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS rental_insurance_bps     int  NOT NULL DEFAULT 0;
