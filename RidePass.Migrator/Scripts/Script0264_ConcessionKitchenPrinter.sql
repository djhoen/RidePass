-- Kitchen ticket printers for concessions.
--
-- When a cashier sale is paid, the kitchen gets a printed ticket (order number, customer, items)
-- alongside the cook screen it already has. Which printer gets which lines is configurable.
--
-- Printers are their own table rather than a printer_url column on concession_station, because
-- stations and printers do NOT route the same way. A common setup is two cook SCREENS split by
-- station (grill, fryer) feeding ONE printer at the pass that prints the whole order. A column on
-- station cannot express that: you would have to put the same URL on both stations and the kitchen
-- would get two half-tickets on one printer instead of one whole one.
--
-- So the rule is on the printer, and it is one sentence: a printer prints the lines for the
-- stations linked to it, and a printer with NO linked stations prints the entire order. That single
-- rule covers every layout we know of:
--   * one whole-order printer at the pass, screens still split by station  (no rows in the join)
--   * a printer per station                                               (one row each)
--   * an expo printer plus per-station printers                           (both shapes together)
--   * one printer shared by grill+fryer, another for drinks               (two rows, one printer)
--
-- It also removes the need for a separate "default printer" concept for items whose product has no
-- station_id: a whole-order printer already catches them.
--
-- url is the printer's ePOS-Print endpoint, e.g. https://192.168.1.50. It must be https: the POS is
-- served over https and browsers block mixed content, so a plain http printer silently fails.

CREATE TABLE IF NOT EXISTS concession_printer (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id  uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name       text        NOT NULL,
    url        text        NOT NULL,
    sort_order int         NOT NULL DEFAULT 0,
    is_active  boolean     NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_concession_printer_tenant ON concession_printer (tenant_id, is_active, sort_order);

-- Which stations a printer is scoped to. No rows for a printer = prints the whole order.
-- Cascade on both sides: deleting a printer drops its scope, and deleting a station just narrows
-- any printer that referenced it (a printer that loses its last station becomes whole-order, which
-- is the safe direction - it prints more, never less).
CREATE TABLE IF NOT EXISTS concession_printer_station (
    printer_id uuid NOT NULL REFERENCES concession_printer(id) ON DELETE CASCADE,
    station_id uuid NOT NULL REFERENCES concession_station(id) ON DELETE CASCADE,
    PRIMARY KEY (printer_id, station_id)
);
CREATE INDEX IF NOT EXISTS idx_concession_printer_station_station ON concession_printer_station (station_id);
