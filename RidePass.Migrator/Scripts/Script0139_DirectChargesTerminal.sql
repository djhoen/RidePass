-- Direct charges, phase 3: Terminal / card-present (counter + concession tap-to-pay).
--
-- For a 'direct' tenant the Stripe Terminal Location and the card-present PaymentIntents must live
-- on the tenant's OWN connected account (the existing stripe_terminal_location_id is a platform-
-- account Location, used in 'platform' mode). Store the connected-account Location separately so a
-- tenant can switch modes without one Location id clobbering the other.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS stripe_connected_terminal_location_id text NULL;
