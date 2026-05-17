-- Rename "day pass" → "pass" everywhere. Pre-launch refactor: no live tenants
-- to migrate, but the schema still has columns / constraints / indexes that
-- mention day_pass. This script updates them all in lock-step with the C#/TS
-- code rename. Touches:
--
--   * Tables day_pass_product, day_pass_purchase → pass_product, pass_purchase
--   * Their indexes, triggers, PK constraint names, the explicit status check
--   * source_kind CHECK constraints + data on coupon_redemption,
--     gift_card_redemption, tenant_ledger_entry
--   * applicable_scope on coupon, bundled_coupon_scope on event_ticket_tier
--   * reward_program.requirement_kind
--   * event_waitlist.created_purchase_kind
--   * audit_log.target_kind rows that reference the old name
--
-- DbUp wraps each script in a transaction, so any failure rolls everything
-- back automatically — no explicit BEGIN/COMMIT needed (and adding them
-- collides with DbUp's transaction).

-- ── Tables, indexes, triggers, named constraints ────────────────────────────
ALTER TABLE day_pass_product  RENAME TO pass_product;
ALTER TABLE day_pass_purchase RENAME TO pass_purchase;

ALTER INDEX day_pass_product_pkey                  RENAME TO pass_product_pkey;
ALTER INDEX day_pass_purchase_pkey                 RENAME TO pass_purchase_pkey;
ALTER INDEX idx_day_pass_product_tenant            RENAME TO idx_pass_product_tenant;
ALTER INDEX idx_day_pass_purchase_tenant_status    RENAME TO idx_pass_purchase_tenant_status;
ALTER INDEX idx_day_pass_purchase_event            RENAME TO idx_pass_purchase_event;

ALTER TRIGGER trg_day_pass_product_updated_at  ON pass_product  RENAME TO trg_pass_product_updated_at;
ALTER TRIGGER trg_day_pass_purchase_updated_at ON pass_purchase RENAME TO trg_pass_purchase_updated_at;

-- The status CHECK was created with an explicit name in Script0009.
ALTER TABLE pass_purchase RENAME CONSTRAINT day_pass_purchase_status_check TO pass_purchase_status_check;


-- ── source_kind values + CHECK constraints across consumer tables ──────────
ALTER TABLE coupon_redemption DROP CONSTRAINT coupon_redemption_source_kind_check;
UPDATE coupon_redemption SET source_kind = 'pass' WHERE source_kind = 'day_pass';
ALTER TABLE coupon_redemption ADD CONSTRAINT coupon_redemption_source_kind_check
    CHECK (source_kind IN ('pass','event_ticket','season_pass','rental'));

ALTER TABLE gift_card_redemption DROP CONSTRAINT gift_card_redemption_source_kind_check;
UPDATE gift_card_redemption SET source_kind = 'pass' WHERE source_kind = 'day_pass';
ALTER TABLE gift_card_redemption ADD CONSTRAINT gift_card_redemption_source_kind_check
    CHECK (source_kind IN ('pass','event_ticket','season_pass','rental'));

ALTER TABLE tenant_ledger_entry DROP CONSTRAINT tenant_ledger_entry_source_kind_check;
UPDATE tenant_ledger_entry SET source_kind = 'pass' WHERE source_kind = 'day_pass';
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
    CHECK (source_kind IS NULL OR source_kind IN ('pass','event_ticket','season_pass','rental'));


-- ── coupon.applicable_scope ────────────────────────────────────────────────
ALTER TABLE coupon DROP CONSTRAINT coupon_applicable_scope_check;
UPDATE coupon SET applicable_scope = 'pass' WHERE applicable_scope = 'day_pass';
ALTER TABLE coupon ADD CONSTRAINT coupon_applicable_scope_check
    CHECK (applicable_scope IN ('all','pass','event_ticket','season_pass','rental'));


-- ── event_ticket_tier.bundled_coupon_scope ─────────────────────────────────
ALTER TABLE event_ticket_tier DROP CONSTRAINT event_ticket_tier_bundled_coupon_scope_check;
UPDATE event_ticket_tier SET bundled_coupon_scope = 'pass' WHERE bundled_coupon_scope = 'day_pass';
ALTER TABLE event_ticket_tier ADD CONSTRAINT event_ticket_tier_bundled_coupon_scope_check
    CHECK (bundled_coupon_scope IS NULL OR bundled_coupon_scope IN ('all','pass','event_ticket','season_pass'));


-- ── reward_program.requirement_kind ────────────────────────────────────────
ALTER TABLE reward_program DROP CONSTRAINT reward_program_requirement_kind_check;
UPDATE reward_program SET requirement_kind = 'pass' WHERE requirement_kind = 'day_pass';
ALTER TABLE reward_program ADD CONSTRAINT reward_program_requirement_kind_check
    CHECK (requirement_kind IN ('pass','event_ticket','any'));


-- ── event_waitlist.created_purchase_kind ───────────────────────────────────
ALTER TABLE event_waitlist DROP CONSTRAINT event_waitlist_created_purchase_kind_check;
UPDATE event_waitlist SET created_purchase_kind = 'pass' WHERE created_purchase_kind = 'day_pass';
ALTER TABLE event_waitlist ADD CONSTRAINT event_waitlist_created_purchase_kind_check
    CHECK (created_purchase_kind IS NULL OR created_purchase_kind IN ('pass','event_ticket'));


-- ── Audit log target-kind strings ──────────────────────────────────────────
UPDATE audit_log SET target_kind = 'pass_product'   WHERE target_kind = 'day_pass_product';
UPDATE audit_log SET target_kind = 'pass_purchase'  WHERE target_kind = 'day_pass_purchase';


-- ── Rename event_day_pass_eligibility → event_pass_eligibility ─────────────
-- The join table was named for the relationship; keep the rename consistent.
ALTER TABLE event_day_pass_eligibility RENAME TO event_pass_eligibility;
ALTER TABLE event_pass_eligibility RENAME COLUMN day_pass_product_id TO pass_product_id;
ALTER INDEX idx_event_day_pass_eligibility_product RENAME TO idx_event_pass_eligibility_product;
