-- Lets a season pass be SOLD at the gate counter, with a staff discount recorded on the row.
--
-- Until now a season pass could only be bought online by the rider themselves: SeasonPass/Buy takes
-- the purchaser from the caller's own token, so a staff member calling it would buy a pass for
-- themselves. That left "season_pass" as a discount surface a tenant could pick (Script0251) which
-- no code path honoured, because there was no counter that sold one.
--
-- Not to be confused with Script0260's tenant.season_pass_discount_*: that is a perk FOR pass
-- holders (a pass holder gets money off other things). This is a discount ON buying the pass.
--
-- Two additive changes, both nullable-or-defaulted so the deployed app keeps inserting without them:
--
-- 1. The staff-discount snapshot, matching shop_sale (Script0252) and the four counter purchase
--    tables (Script0257). The label is kept next to the id because a track that renames
--    "Military 10%" to "Military 15%" must not rewrite what last season's receipts say, and the
--    ON DELETE SET NULL below would otherwise erase the reason entirely.
--
-- 2. sold_by_user_id. Every other thing the counter can sell records who rang it up
--    (event_ticket_purchase, event_extra_purchase, membership_purchase, shop_rental all have one);
--    season_pass_purchase never needed it while it was online-only. Without it the cashier behind a
--    counter pass sale would only be recoverable from the ledger row, which is a much worse place to
--    look and does not survive a ledger rewrite. issued_by_user_id already exists but means
--    something different: an admin GRANTING a free employee pass, not a cashier selling one.

ALTER TABLE season_pass_purchase
    ADD COLUMN IF NOT EXISTS discount_cents int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS discount_preset_id uuid NULL REFERENCES discount_preset(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS discount_label text NULL,
    ADD COLUMN IF NOT EXISTS discount_authorized_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS sold_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL;

-- "What did the military rate cost us, and on what" sweeps by preset. Partial because the
-- overwhelming majority of passes carry no staff discount at all.
CREATE INDEX IF NOT EXISTS idx_season_pass_purchase_discount_preset
    ON season_pass_purchase (tenant_id, discount_preset_id)
    WHERE discount_preset_id IS NOT NULL;

-- "What did this cashier sell today" across the counter's kinds.
CREATE INDEX IF NOT EXISTS idx_season_pass_purchase_sold_by
    ON season_pass_purchase (tenant_id, sold_by_user_id)
    WHERE sold_by_user_id IS NOT NULL;
