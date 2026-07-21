-- Gift cards as a tender at the shop register. Gift-card money is deferred revenue (the
-- purchase writes NO ledger entry), so at redemption the sale's ledger entry recognizes the
-- gift-funded portion as gross while the PI only charges the remainder. The applied amount +
-- card are snapshotted on the sale so refunds can hand the balance back and payment failures
-- restore it (the finalizer's RestoreDiscountsFor already handles 'shop_sale' redemptions).
--
-- Additive + idempotent.

ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS gift_card_applied_cents int NOT NULL DEFAULT 0;
ALTER TABLE shop_sale ADD COLUMN IF NOT EXISTS gift_card_id uuid NULL REFERENCES gift_card(id) ON DELETE SET NULL;
