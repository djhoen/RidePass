-- Store credit phase 3: credit as a tender on MULTI-ROW checkouts (gate counter, online event
-- checkout). Those carts have no order header: one PaymentIntent spans many purchase rows, each
-- booking its own ledger entry. Rather than teaching four purchase tables about credit, one
-- checkout_credit_tender row anchors the whole thing: created (and the balance debited) at
-- checkout, found again by PaymentIntent id, reversed whole if the payment fails or the cart is
-- abandoned (all rows on a PI fail together, so whole-tender reversal is exact).
--
-- Ledger: per-row entries stay EXACTLY as they are (zero change to battle-tested money code).
-- One balancing entry per tender (source_kind 'credit_tender', gross = -credit) nets the books:
-- money actually collected = sum(row grosses) - credit. The platform cut stays charged in full,
-- same as a cash sale (the tenant funds credit-covered value; granted credit is their promo).
--
-- Additive + idempotent.

CREATE TABLE IF NOT EXISTS checkout_credit_tender (
    id                        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                 uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    credit_account_id         uuid        NOT NULL REFERENCES tenant_credit_account(id) ON DELETE CASCADE,
    -- NULL for a cash counter sale (settled immediately, no PI to key on).
    stripe_payment_intent_id  text        NULL,
    credit_applied_cents      int         NOT NULL CHECK (credit_applied_cents > 0),
    context                   text        NOT NULL CHECK (context IN ('counter', 'event_checkout')),
    created_at                timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS uk_checkout_credit_tender_pi
    ON checkout_credit_tender (stripe_payment_intent_id) WHERE stripe_payment_intent_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_checkout_credit_tender_tenant ON checkout_credit_tender (tenant_id, created_at DESC);

-- The balancing entry's payment method: 'credit' joins the ledger's allowed set.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_payment_method_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_payment_method_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_payment_method_check
        CHECK (payment_method IN ('stripe', 'cash', 'voucher', 'stripe_connect', 'loampass_credits', 'stripe_direct', 'credit'));
END $$;

-- The balancing entry's source kind, plus 'credit_tender' as a redeem reference kind (the
-- once-per-reference unique index on tenant_credit_entry already covers any reference_kind).
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tenant_ledger_entry_source_kind_check') THEN
        ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
    END IF;
    ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
        CHECK (source_kind IS NULL OR source_kind IN (
            'pass', 'event_ticket', 'season_pass', 'rental', 'membership', 'extras',
            'concession', 'tenant_billing_event', 'email_campaign', 'rental_deposit',
            'shop_sale', 'shop_rental', 'shop_rental_deposit', 'shop_wo_deposit', 'credit_tender'
        ));
END $$;
