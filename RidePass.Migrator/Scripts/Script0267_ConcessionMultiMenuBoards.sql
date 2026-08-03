-- Multiple in-venue menu boards per tenant (stacked-TV style: each screen shows its own set of
-- categories, like a QSR wall of displays). A board is a named, orderable screen; categories are
-- assigned to a board via concession_category.menu_board_id. NULL menu_board_id = the category
-- appears on every board (and on the single default board for tenants that never create boards),
-- so existing tenants keep their current one-screen behavior with no backfill needed.

CREATE TABLE IF NOT EXISTS concession_menu_board (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name        text        NOT NULL,
    sort_order  int         NOT NULL DEFAULT 0,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_concession_menu_board_tenant ON concession_menu_board (tenant_id, sort_order);

ALTER TABLE concession_category ADD COLUMN IF NOT EXISTS
    menu_board_id uuid NULL REFERENCES concession_menu_board(id) ON DELETE SET NULL;
