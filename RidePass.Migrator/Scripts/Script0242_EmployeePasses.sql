-- Employee passes: a season pass product a track grants to its own staff, free or discounted.
--
-- Modelled as an ordinary season pass rather than a new entity, on purpose. Gate scan, walk-up
-- admission (Script0236), photo, waiver, ID verification (Script0238), wristbands, reservations,
-- and the benefit model all work untouched. A parallel employee_pass table would have meant a
-- second admission implementation, and the second one is the one that drifts out of step with
-- the waiver rules.
--
-- Two columns, and each closes a specific hole:
--
-- is_employee marks the product staff-only. It is NOT cosmetic: pass products are public by
-- default (SeasonPassController.ListActive and GetLanding are both anonymous), so a $0 product
-- reaching those paths is free season passes for the entire internet. The repository excludes
-- these by default and only the admin list opts in, so a public endpoint written later is safe
-- by omission rather than by remembering.
--
-- issued_by_user_id records WHO approved the grant. Eligibility (an active account on the
-- tenant) is automatic and grants nothing; approval is a deliberate admin act and is what
-- actually creates the pass. Without this column the admin page cannot answer "who gave this
-- person a free pass", which is the first question asked when one turns up unexpectedly. It
-- mirrors cancelled_by_user_id, already on this table for the other end of the lifecycle.
--
-- Deliberately NOT here: a revoked/valid flag driven by employment. Validity is DERIVED from
-- users.status at read time (see SeasonPassRepository.EmployeePassEligibleExpr). A copied flag
-- is correct right up until someone disables a user through a path that forgot to update
-- passes, and then it is silently wrong in the direction that lets a former employee keep
-- riding. Deriving it means disabling the account in Admin > Users invalidates the pass
-- instantly and everywhere, with no second write to go wrong.
--
-- Additive and rerunnable. No backfill: every existing product is a customer product, which is
-- exactly what the false default says.

ALTER TABLE season_pass_product
    ADD COLUMN IF NOT EXISTS is_employee boolean NOT NULL DEFAULT false;

ALTER TABLE season_pass_purchase
    ADD COLUMN IF NOT EXISTS issued_by_user_id uuid NULL REFERENCES users(id);

-- A free employee pass is the whole point of requirement 1, and season_pass_product has
-- forbidden it since the table was created: CHECK (price_cents > 0). Relax it for employee
-- products ONLY, rather than globally.
--
-- Globally would have been one character less work and wrong twice over: a $0 CUSTOMER product
-- would be publicly listed and buyable, and SeasonPassController.Buy builds a Stripe
-- PaymentIntent per order with no zero-charge path, so a free public pass would fail at
-- checkout rather than being free. Employee passes never touch Buy (it rejects them outright),
-- so scoping the exemption to them keeps the existing guarantee where it is load-bearing.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint
               WHERE conname = 'season_pass_product_price_cents_check'
                 AND conrelid = 'season_pass_product'::regclass) THEN
        ALTER TABLE season_pass_product DROP CONSTRAINT season_pass_product_price_cents_check;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'chk_season_pass_product_price'
                     AND conrelid = 'season_pass_product'::regclass) THEN
        ALTER TABLE season_pass_product
            ADD CONSTRAINT chk_season_pass_product_price
            CHECK (price_cents > 0 OR is_employee);
    END IF;
END $$;

-- The admin roster asks "which of this tenant's staff hold an employee pass", which reads
-- purchases by holder across the tenant's employee products. Partial: employee passes are a
-- rounding error next to customer purchases, so indexing only them keeps it small.
CREATE INDEX IF NOT EXISTS idx_season_pass_purchase_issued_by
    ON season_pass_purchase (tenant_id, issued_by_user_id)
    WHERE issued_by_user_id IS NOT NULL;
