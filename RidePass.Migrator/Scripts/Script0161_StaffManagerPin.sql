-- Manager PIN for authorizing gated POS actions. A cashier is logged into the F&B POS as themselves;
-- to comp an order or apply an arbitrary manual discount, a manager walks over and enters a short PIN
-- without logging the cashier out. The PIN identifies WHICH manager approved it (stamped on the sale and
-- the void/comp report), so it lives per-user rather than per-tenant. Only staff who actually hold a
-- manager/admin role can authorize; that role check is enforced in code at set-time and verify-time.
--
-- Stored as a salted hash (Microsoft.AspNetCore.Identity.PasswordHasher), never the raw digits, so a DB
-- leak doesn't expose PINs. NULL = this user has not set a PIN and cannot authorize.
--
-- Idempotent and backwards-compatible: a single additive nullable column; nothing changes until a
-- manager sets a PIN.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS pos_pin_hash text NULL;
