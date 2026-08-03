-- Customer-facing display for the F&B POS: a second tablet that mirrors the order being rung up
-- (read-only) and captures the customer's tip when the tenant has tips enabled. The POS pushes a
-- JSON snapshot of the cart into state_json (debounced); the display tablet polls it. The tip flows
-- the other way: the display writes tip_cents, the POS polls for it during checkout. Pairing is a
-- short per-display code shown on the tablet and typed once into the POS. Every state push clears
-- tip_cents so a previous order's tip can never bleed into the next one.

CREATE TABLE IF NOT EXISTS concession_display (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    pair_code   text        NOT NULL,
    state_json  text        NULL,
    tip_cents   int         NULL,
    updated_at  timestamptz NOT NULL DEFAULT now(),
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_concession_display_tenant_code ON concession_display (tenant_id, pair_code);
