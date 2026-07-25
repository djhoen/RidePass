-- Grab-and-go F&B items: never show on the cook screen.
--
-- A bag of chips, a candy bar, or a bottled water is handed over at the window; there is
-- nothing for the kitchen to make, so a ticket line for it is pure noise on the KDS (and
-- it wedges the ticket in "Preparing…" until someone bumps a line nobody cooked).
--
-- concession_product.requires_prep is the tenant's per-item setting (default true = made to
-- order, current behavior for every existing item).
--
-- concession_sale_line.requires_prep is the SNAPSHOT, mirroring how station_id/name/price are
-- snapshotted onto the line: the cook screen filters on the line, so re-flagging an item later
-- never rewrites the tickets that are already in the queue.
--
-- Ordering consequence (enforced in ConcessionController, not here): a no-prep line is written
-- with prep_status = 'ready' at sale time, so
--   * a mixed order flips to 'ready' when the cook bumps its real prep lines, and
--   * an all-grab-and-go order is 'ready' the moment it is created, appearing on the pickup
--     board and never on the cook screen.
-- That also keeps CountActivePrepLines (the online quote-time backlog) honest for free, since
-- no-prep lines are never 'queued'/'in_progress'.
--
-- Additive and rerunnable. No backfill: every existing item keeps needing prep, which is what
-- the tenants configured implicitly. They can flag the grab-and-go items themselves.

ALTER TABLE concession_product
    ADD COLUMN IF NOT EXISTS requires_prep boolean NOT NULL DEFAULT true;

ALTER TABLE concession_sale_line
    ADD COLUMN IF NOT EXISTS requires_prep boolean NOT NULL DEFAULT true;
