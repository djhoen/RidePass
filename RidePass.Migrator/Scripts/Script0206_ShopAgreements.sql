-- Signed agreements for the bike shop: repair authorization on work orders, and the rental
-- agreement a renter signs before gear leaves the counter.
--
-- WHY NOT REUSE tenant_waiver: that table is the event/ticket waiver and its repository exposes
-- GetActive(tenantId) as "the one active waiver for this tenant". Teaching it a `kind` would
-- change a signature used across event checkout, ticket registration, extras, and the gate, i.e.
-- a wide blast radius on a critical path for a shop-only feature. These agreements are their own
-- versioned documents. The event waiver stays exactly where it is, and a rental checks BOTH: the
-- track's waiver (liability) and the shop's rental agreement (the gear, the money, the return).
--
-- Modeled on tenant_waiver's shape (versioned, one active per kind) so it behaves the way the
-- rest of the app already does.

CREATE TABLE IF NOT EXISTS shop_agreement (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id  uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- rental_agreement  = terms the renter signs before gear goes out
    -- work_order_terms  = authorization to perform the repair
    kind       text        NOT NULL CHECK (kind IN ('rental_agreement', 'work_order_terms')),
    version    int         NOT NULL DEFAULT 1,
    title      text        NOT NULL,
    body       text        NOT NULL DEFAULT '',
    is_active  boolean     NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

-- At most one ACTIVE document per kind per tenant, so "the current rental agreement" is always
-- unambiguous. Superseded versions stay for the signatures that reference them.
CREATE UNIQUE INDEX IF NOT EXISTS uk_shop_agreement_active
    ON shop_agreement (tenant_id, kind) WHERE is_active;
CREATE INDEX IF NOT EXISTS idx_shop_agreement_tenant
    ON shop_agreement (tenant_id, kind, version DESC);

DROP TRIGGER IF EXISTS trg_shop_agreement_updated_at ON shop_agreement;
CREATE TRIGGER trg_shop_agreement_updated_at BEFORE UPDATE ON shop_agreement
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE IF NOT EXISTS shop_agreement_signature (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- RESTRICT: a signed agreement version can be superseded but never deleted out from under
    -- the signature that proves what was agreed to.
    agreement_id  uuid        NOT NULL REFERENCES shop_agreement(id) ON DELETE RESTRICT,
    -- Exactly one owner, same pattern (and same reasoning) as shop_condition_photo.
    work_order_id uuid        NULL REFERENCES shop_work_order(id) ON DELETE CASCADE,
    rental_id     uuid        NULL REFERENCES shop_rental(id)     ON DELETE CASCADE,
    -- Snapshot of what was actually signed. The body can be re-published later; this row has to
    -- keep meaning what it meant on the day.
    agreement_version int     NOT NULL,
    signer_name   text        NOT NULL,
    signer_email  text        NULL,
    signature_data_url text   NOT NULL,
    signed_at     timestamptz NOT NULL DEFAULT now(),
    ip_address    text        NULL,
    -- Who held the tablet, for the audit trail.
    witnessed_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT chk_shop_agreement_signature_owner CHECK (
        (work_order_id IS NOT NULL AND rental_id IS NULL)
     OR (work_order_id IS NULL AND rental_id IS NOT NULL)
    )
);

-- "Has this rental / work order been signed?" is the checkout gate's hot path.
CREATE INDEX IF NOT EXISTS idx_shop_agreement_sig_rental
    ON shop_agreement_signature (rental_id) WHERE rental_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_shop_agreement_sig_wo
    ON shop_agreement_signature (work_order_id) WHERE work_order_id IS NOT NULL;

-- Seed a starting rental agreement for tracks that already run the shop, so the checkout gate
-- has something to enforce instead of blocking every rental on a missing document. Deliberately
-- plain and short: the tenant edits it in shop settings. Only for tenants with the shop on, and
-- only when they have no rental agreement yet.
INSERT INTO shop_agreement (tenant_id, kind, title, body)
SELECT t.id, 'rental_agreement', 'Rental Agreement',
       'I accept responsibility for the equipment listed on this rental while it is in my care. '
    || 'I agree to return it by the agreed return time and in the condition I received it, '
    || 'allowing for normal wear. I understand I am responsible for loss, theft, or damage beyond '
    || 'normal wear, and that the security deposit may be applied toward those costs. '
    || 'I confirm the equipment was inspected with me and is in working order at pickup.'
FROM tenant t
WHERE t.bike_shop_enabled
  AND NOT EXISTS (
      SELECT 1 FROM shop_agreement a
      WHERE a.tenant_id = t.id AND a.kind = 'rental_agreement'
  );
