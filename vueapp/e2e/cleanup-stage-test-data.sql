-- Cleanup for data created by the Playwright checkout/mutation tests on STAGE.
-- Run against the ridepass_stage database with a write-capable connection (the read-only MCP
-- role can't delete). Everything below is scoped to the Motoland tenant so it can't touch
-- another tenant's rows. Review the SELECT counts first, then run the DELETEs.
--
-- What the tests create, and how identifiable it is:
--   * Gift cards        -> tagged by the non-deliverable recipient email. PRECISE, safe to delete.
--   * Bike-shop work orders -> tagged "[PW-TEST]" in the customer name. PRECISE.
--   * Event tickets / concession sales / season passes -> bought as the QA account (danh@prohoods.com)
--     with no distinct marker, so they are indistinguishable from real QA activity. Left in place
--     on purpose. They are test-mode Stripe charges (no real money) tied to the QA login.

\set tenant '5a8a1cda-3625-416d-a02b-7e4b81ccd489'

-- 1) Preview what will be removed --------------------------------------------------------------
SELECT 'gift_card' AS what, count(*)
FROM gift_card
WHERE tenant_id = :'tenant' AND recipient_email = 'pw-checkout-test@example.com';

-- Bike-shop work orders (table not visible to the read-only MCP role; column is customer_name
-- per the intake form). Verify the table/column names in your environment before deleting.
SELECT 'shop_work_order' AS what, count(*)
FROM shop_work_order
WHERE tenant_id = :'tenant' AND customer_name LIKE '[PW-TEST]%';

-- 2) Delete -----------------------------------------------------------------------------------
-- Gift cards (no redemptions are created by the test, so nothing references these rows).
DELETE FROM gift_card
WHERE tenant_id = :'tenant' AND recipient_email = 'pw-checkout-test@example.com';

-- Bike-shop work orders. If child rows (lines, notes, condition photos, inspections) are not set
-- to ON DELETE CASCADE, delete them first by work_order_id, then remove the parent rows.
DELETE FROM shop_work_order
WHERE tenant_id = :'tenant' AND customer_name LIKE '[PW-TEST]%';
