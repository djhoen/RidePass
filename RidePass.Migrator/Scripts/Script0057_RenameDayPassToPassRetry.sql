-- Idempotent retry of the day_pass → pass rename. Script0056 got journalled
-- before its body actually ran (the explicit BEGIN/COMMIT collided with
-- DbUp's transaction-per-script wrapper), leaving the database half-renamed.
-- This script uses IF EXISTS / NOT EXISTS guards so it converges regardless
-- of which renames already landed.

DO $$
BEGIN
    -- Tables --------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM information_schema.tables
               WHERE table_schema='public' AND table_name='day_pass_product') THEN
        ALTER TABLE day_pass_product RENAME TO pass_product;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.tables
               WHERE table_schema='public' AND table_name='day_pass_purchase') THEN
        ALTER TABLE day_pass_purchase RENAME TO pass_purchase;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.tables
               WHERE table_schema='public' AND table_name='event_day_pass_eligibility') THEN
        ALTER TABLE event_day_pass_eligibility RENAME TO event_pass_eligibility;
    END IF;

    -- Columns -------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='event_pass_eligibility' AND column_name='day_pass_product_id') THEN
        ALTER TABLE event_pass_eligibility RENAME COLUMN day_pass_product_id TO pass_product_id;
    END IF;

    -- Indexes -------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM pg_class WHERE relkind='i' AND relname='day_pass_product_pkey') THEN
        ALTER INDEX day_pass_product_pkey RENAME TO pass_product_pkey;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_class WHERE relkind='i' AND relname='day_pass_purchase_pkey') THEN
        ALTER INDEX day_pass_purchase_pkey RENAME TO pass_purchase_pkey;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_class WHERE relkind='i' AND relname='idx_day_pass_product_tenant') THEN
        ALTER INDEX idx_day_pass_product_tenant RENAME TO idx_pass_product_tenant;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_class WHERE relkind='i' AND relname='idx_day_pass_purchase_tenant_status') THEN
        ALTER INDEX idx_day_pass_purchase_tenant_status RENAME TO idx_pass_purchase_tenant_status;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_class WHERE relkind='i' AND relname='idx_day_pass_purchase_event') THEN
        ALTER INDEX idx_day_pass_purchase_event RENAME TO idx_pass_purchase_event;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_class WHERE relkind='i' AND relname='idx_event_day_pass_eligibility_product') THEN
        ALTER INDEX idx_event_day_pass_eligibility_product RENAME TO idx_event_pass_eligibility_product;
    END IF;

    -- Triggers ------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_day_pass_product_updated_at') THEN
        ALTER TRIGGER trg_day_pass_product_updated_at ON pass_product RENAME TO trg_pass_product_updated_at;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_day_pass_purchase_updated_at') THEN
        ALTER TRIGGER trg_day_pass_purchase_updated_at ON pass_purchase RENAME TO trg_pass_purchase_updated_at;
    END IF;

    -- Named CHECK constraints --------------------------------------------
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname='day_pass_purchase_status_check') THEN
        ALTER TABLE pass_purchase RENAME CONSTRAINT day_pass_purchase_status_check TO pass_purchase_status_check;
    END IF;
END $$;


-- ── source_kind values + CHECK constraints across consumer tables ─────
ALTER TABLE coupon_redemption DROP CONSTRAINT IF EXISTS coupon_redemption_source_kind_check;
UPDATE coupon_redemption SET source_kind = 'pass' WHERE source_kind = 'day_pass';
ALTER TABLE coupon_redemption ADD CONSTRAINT coupon_redemption_source_kind_check
    CHECK (source_kind IN ('pass','event_ticket','season_pass','rental'));

ALTER TABLE gift_card_redemption DROP CONSTRAINT IF EXISTS gift_card_redemption_source_kind_check;
UPDATE gift_card_redemption SET source_kind = 'pass' WHERE source_kind = 'day_pass';
ALTER TABLE gift_card_redemption ADD CONSTRAINT gift_card_redemption_source_kind_check
    CHECK (source_kind IN ('pass','event_ticket','season_pass','rental'));

ALTER TABLE tenant_ledger_entry DROP CONSTRAINT IF EXISTS tenant_ledger_entry_source_kind_check;
UPDATE tenant_ledger_entry SET source_kind = 'pass' WHERE source_kind = 'day_pass';
ALTER TABLE tenant_ledger_entry ADD CONSTRAINT tenant_ledger_entry_source_kind_check
    CHECK (source_kind IS NULL OR source_kind IN ('pass','event_ticket','season_pass','rental'));


-- ── coupon.applicable_scope ──────────────────────────────────────────────
ALTER TABLE coupon DROP CONSTRAINT IF EXISTS coupon_applicable_scope_check;
UPDATE coupon SET applicable_scope = 'pass' WHERE applicable_scope = 'day_pass';
ALTER TABLE coupon ADD CONSTRAINT coupon_applicable_scope_check
    CHECK (applicable_scope IN ('all','pass','event_ticket','season_pass','rental'));


-- ── event_ticket_tier.bundled_coupon_scope ──────────────────────────────
ALTER TABLE event_ticket_tier DROP CONSTRAINT IF EXISTS event_ticket_tier_bundled_coupon_scope_check;
UPDATE event_ticket_tier SET bundled_coupon_scope = 'pass' WHERE bundled_coupon_scope = 'day_pass';
ALTER TABLE event_ticket_tier ADD CONSTRAINT event_ticket_tier_bundled_coupon_scope_check
    CHECK (bundled_coupon_scope IS NULL OR bundled_coupon_scope IN ('all','pass','event_ticket','season_pass'));


-- ── reward_program.requirement_kind ─────────────────────────────────────
ALTER TABLE reward_program DROP CONSTRAINT IF EXISTS reward_program_requirement_kind_check;
UPDATE reward_program SET requirement_kind = 'pass' WHERE requirement_kind = 'day_pass';
ALTER TABLE reward_program ADD CONSTRAINT reward_program_requirement_kind_check
    CHECK (requirement_kind IN ('pass','event_ticket','any'));


-- ── event_waitlist.created_purchase_kind ────────────────────────────────
ALTER TABLE event_waitlist DROP CONSTRAINT IF EXISTS event_waitlist_created_purchase_kind_check;
UPDATE event_waitlist SET created_purchase_kind = 'pass' WHERE created_purchase_kind = 'day_pass';
ALTER TABLE event_waitlist ADD CONSTRAINT event_waitlist_created_purchase_kind_check
    CHECK (created_purchase_kind IS NULL OR created_purchase_kind IN ('pass','event_ticket'));


-- ── Audit log target-kind strings ──────────────────────────────────────
UPDATE audit_log SET target_kind = 'pass_product'   WHERE target_kind = 'day_pass_product';
UPDATE audit_log SET target_kind = 'pass_purchase'  WHERE target_kind = 'day_pass_purchase';
