-- Store credit on an F&B open tab is redeemed per CLOSE ATTEMPT. A counter sale redeems once under
-- its sale id and, if the card dies, reverses once under the same id; the once-per-reference unique
-- index on tenant_credit_entry makes both idempotent. A tab is different: its close can be attempted
-- several times (the card declines on the reader, the cashier switches to cash, tries again), and a
-- second redeem under the sale id would collide with the first attempt's entry and report success
-- WITHOUT debiting the balance, right after the retry had reversed the first one. The sale would say
-- "credit applied" while the customer kept the credit.
--
-- So each attempt redeems under its own reference ('concession_tab_close' + a fresh id) and the sale
-- remembers which attempt's redeem is outstanding here. A retry reverses that attempt by its id and
-- redeems again under a new one; a void or a later refund reverses whichever id the row carries. NULL
-- means no credit is applied, or the credit was redeemed under the sale-level reference (counter sales,
-- and tabs closed before this column existed, which ConcessionTabCredit still resolves correctly).
--
-- Additive and idempotent.

ALTER TABLE concession_sale ADD COLUMN IF NOT EXISTS credit_redeem_ref uuid NULL;
