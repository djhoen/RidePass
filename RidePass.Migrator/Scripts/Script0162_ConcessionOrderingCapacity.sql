-- Online-order throttle + live quote times for Food & Beverage. Per-tenant capacity config in its own
-- table (kept separate from concession_menu_settings on purpose). Default disabled, so existing tenants
-- behave exactly as before until they opt in.
--
-- Idempotent (rerunnable) and additive/backwards-compatible.

CREATE TABLE IF NOT EXISTS concession_ordering_capacity (
    tenant_id          uuid        PRIMARY KEY REFERENCES tenant(id) ON DELETE CASCADE,
    capacity_enabled   boolean     NOT NULL DEFAULT false,   -- master switch for the throttle + quotes
    base_prep_minutes  int         NOT NULL DEFAULT 10,      -- floor quote when the kitchen is idle
    max_active_orders  int         NOT NULL DEFAULT 0,       -- pause online ordering at/above this; 0 = no cap
    show_quote_times   boolean     NOT NULL DEFAULT true,    -- show the customer an estimated ready time
    online_paused      boolean     NOT NULL DEFAULT false,   -- manual staff pause (toggled from the cook/cashier screen)
    updated_at         timestamptz NOT NULL DEFAULT now()
);
