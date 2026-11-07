-- Gate check-in: a pass holder's own signature on an EVENT's waiver, per pass.
--
-- 1. season_pass_waiver_signature: the signature that covers THIS pass's holder for one waiver
--    document (an event's rider waiver) other than the pass's own product signature
--    (season_pass_purchase.waiver_signature_id). Before this, the gate looked such a signature up in
--    the buying ACCOUNT's slot (rider_waiver_signature.user_id), which is right when the account is
--    the holder but, for a parent who buys passes for their children, put each child's signature in the
--    parent's slot (one per document), so siblings displaced each other and a parent's own signature
--    could stand in for a child's. Now: an event-document signature for a holder who isn't the account
--    is linked here, one per (pass, document); the account slot is only used when the account IS the
--    holder. The gate reads this first for the required document.
--
-- 2. private_image_trash retry bookkeeping (Script0307 queue): attempts, last_error, next_attempt_at,
--    so one image that can't be deleted is retried with backoff and never blocks the rest.
--
-- Additive: a new table and nullable / defaulted columns; older code ignores both.
-- Rerunnable: CREATE TABLE / INDEX IF NOT EXISTS, ADD COLUMN IF NOT EXISTS.

CREATE TABLE IF NOT EXISTS season_pass_waiver_signature (
    id                        uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                 uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    season_pass_purchase_id   uuid NOT NULL REFERENCES season_pass_purchase(id) ON DELETE CASCADE,
    waiver_id                 uuid NOT NULL REFERENCES tenant_waiver(id) ON DELETE CASCADE,
    rider_waiver_signature_id uuid NOT NULL REFERENCES rider_waiver_signature(id) ON DELETE CASCADE,
    created_at                timestamptz NOT NULL DEFAULT now(),
    updated_at                timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_season_pass_waiver_signature_pass_waiver
    ON season_pass_waiver_signature (season_pass_purchase_id, waiver_id);
CREATE INDEX IF NOT EXISTS idx_season_pass_waiver_signature_tenant
    ON season_pass_waiver_signature (tenant_id, season_pass_purchase_id);

ALTER TABLE private_image_trash
    ADD COLUMN IF NOT EXISTS attempts        integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_error      text NULL,
    ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz NOT NULL DEFAULT now();

CREATE INDEX IF NOT EXISTS idx_private_image_trash_due ON private_image_trash (next_attempt_at, id);
