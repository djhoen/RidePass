-- Book captured rental damage deposits into the ledger, so the tenant actually gets paid for them.
--
-- The bug this fixes: RentalController charges the rental fee AND the refundable deposit on one
-- PaymentIntent (rental_pi_id), but sets rental_purchase.amount_cents to the rental portion only,
-- and OnRentalPaid books gross = amount_cents. So the deposit money is collected by the platform and
-- appears in tenant_ledger_entry nowhere.
--
-- While the deposit is merely HELD that is correct, and deliberately so: a refundable deposit is not
-- earnings, and it must not inflate the tenant's payout balance while it may still go back to the
-- rider. The platform holds the float, exactly as it does for gift cards.
--
-- But when a unit comes back damaged and the track keeps part of the deposit, that captured amount
-- stops being refundable and becomes the track's income. Today nothing books it, so:
--   • MonthlyPayoutDrafter never sees it and the track is never paid the damage money,
--   • and it is invisible to every report.
-- The platform silently keeps it. No rental has ever carried a deposit in production, so nothing has
-- been lost yet, but it would start the first time a track turned rentals on.
--
-- The fix is a new source_kind. On return, RentalController writes ONE sale entry for the captured
-- portion (source_kind='rental_deposit', source_id = the rental). The refunded portion needs no
-- entry: that money goes back to the rider and was never the tenant's.
--
--   • Platform charge: gross = captured, net = captured. The platform holds the float, so it now
--     owes the whole captured amount to the tenant.
--   • Direct charge: gross = captured, net = 0. The deposit landed in the tenant's own Stripe
--     account, so they already hold the money; there is no platform settlement to make.
--
-- In both cases ridepass_cut = 0. We take a service charge on the rental fee, never on the deposit
-- (see ChargeRouter.Plan in RentalController: the deposit rides the charge but carries no
-- application fee), and capturing a deposit for damage does not change that.
--
-- The existing partial unique index on (tenant_id, source_kind, source_id) per entry_kind makes the
-- write idempotent, so a retried return cannot double-book the damage.
--
-- Idempotent and additive: widens a CHECK constraint to accept one more value. Old rows still pass,
-- and the deployed app keeps working because it simply never writes the new value.

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_source_kind_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
    END IF;

    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
        CHECK (source_kind IS NULL OR source_kind IN (
            'pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras',
            'concession', 'tenant_billing_event', 'email_campaign',
            -- Damage kept out of a rental security deposit on return. Distinct from 'rental' (the
            -- rental fee itself) so the two never collide on the (source_kind, source_id) unique
            -- index: both point at the same rental_purchase id and must be able to coexist.
            'rental_deposit'
        ));
END $$;
