-- Promo callout tiles for the menu board hero carousel ("Make it a combo $5.99", "Side & regular
-- soda included"), modeled on QSR board callouts. A promo rotates through the carousel alongside
-- product photos: text-only tiles render on the accent color; ones with an image render over it.
-- menu_board_id NULL = show on every board (same model as concession_category); deleting a board
-- falls its promos back to all boards rather than losing them.

CREATE TABLE IF NOT EXISTS concession_menu_promo (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    menu_board_id uuid        NULL REFERENCES concession_menu_board(id) ON DELETE SET NULL,
    title         text        NOT NULL,
    subtitle      text        NULL,
    image_url     text        NULL,
    sort_order    int         NOT NULL DEFAULT 0,
    is_active     boolean     NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_concession_menu_promo_tenant ON concession_menu_promo (tenant_id, sort_order);
