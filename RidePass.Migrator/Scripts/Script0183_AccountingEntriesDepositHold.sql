-- v_accounting_entries: exclude HELD deposits from the deposit-lifecycle rows.
--
-- Rental security deposits are now handled two ways (RentalController):
--   • Short rentals HOLD the deposit as a Stripe manual-capture authorization. No money moves and
--     no Stripe fee is charged unless damage is captured on return. deposit_pi_id is set.
--   • Longer rentals (return outside the ~7-day auth window) fall back to CHARGING the deposit with
--     the rental and refunding it on return, exactly as before. deposit_pi_id stays NULL.
--
-- Part 2 of this view books the deposit as cash-in / owed-back (DR tender / CR liability, then the
-- reverse on release). That is correct only when money actually moved, i.e. the CHARGED path. For a
-- true hold nothing was collected, so emitting those rows would invent a liability and a cash
-- movement that never happened. So Part 2 now applies only where deposit_pi_id IS NULL.
--
-- Held deposits therefore produce NO accounting until damage is captured on return, at which point
-- RentalController writes an ordinary 'rental_deposit' sale into the ledger (with the real Stripe fee
-- on that capture) and Part 1 picks it up. That is the whole point of the hold: the books show
-- nothing for money that was only ever reserved, and show real income the moment it is earned.
--
-- Only Part 2's WHERE changes; Part 1 is unchanged. CREATE OR REPLACE VIEW, so idempotent and
-- non-breaking (the column list is identical).

CREATE OR REPLACE VIEW v_accounting_entries AS

-- ── Part 1: everything the ledger already knows about ────────────────────────────────────────
WITH ledger AS (
    SELECT
        l.id,
        l.tenant_id,
        l.entry_kind,
        l.source_kind,
        l.source_id,
        l.occurred_at_utc,
        l.gross_cents,
        l.stripe_fee_cents,
        l.ridepass_cut_cents,
        l.net_to_tenant_cents,
        l.payment_method,
        t.timezone,
        CASE l.source_kind
            WHEN 'event_ticket' THEN etp.amount_cents
            WHEN 'extras'       THEN eep.amount_cents
            WHEN 'season_pass'  THEN spp.amount_cents
            WHEN 'membership'   THEN mp.amount_cents
            WHEN 'rental'       THEN rp.amount_cents
            WHEN 'concession'   THEN cs.total_cents
            ELSE NULL
        END                                     AS source_amount_cents,
        COALESCE(etp.tax_cents, cs.tax_cents, 0) AS source_tax_cents,
        COALESCE(cs.tip_cents, 0)                AS source_tip_cents,
        COALESCE(gcr.amount_cents, 0)            AS source_gift_card_cents
    FROM tenant_ledger_entry l
    JOIN tenant t                       ON t.id = l.tenant_id
    LEFT JOIN event_ticket_purchase etp ON l.source_kind = 'event_ticket' AND etp.id = l.source_id AND etp.tenant_id = l.tenant_id
    LEFT JOIN event_extra_purchase eep  ON l.source_kind = 'extras'       AND eep.id = l.source_id AND eep.tenant_id = l.tenant_id
    LEFT JOIN season_pass_purchase spp  ON l.source_kind = 'season_pass'  AND spp.id = l.source_id AND spp.tenant_id = l.tenant_id
    LEFT JOIN membership_purchase mp    ON l.source_kind = 'membership'   AND mp.id  = l.source_id AND mp.tenant_id  = l.tenant_id
    LEFT JOIN rental_purchase rp        ON l.source_kind = 'rental'       AND rp.id  = l.source_id AND rp.tenant_id  = l.tenant_id
    LEFT JOIN concession_sale cs        ON l.source_kind = 'concession'   AND cs.id  = l.source_id AND cs.tenant_id  = l.tenant_id
    LEFT JOIN gift_card_redemption gcr  ON gcr.source_kind = l.source_kind AND gcr.source_id = l.source_id AND gcr.tenant_id = l.tenant_id
)
SELECT
    l.tenant_id,
    l.id                                                    AS ledger_entry_id,
    l.entry_kind,
    l.source_kind,
    l.source_id,
    l.occurred_at_utc,
    (l.occurred_at_utc AT TIME ZONE l.timezone)::date       AS business_date,
    l.payment_method,
    l.gross_cents,
    l.stripe_fee_cents,
    l.ridepass_cut_cents,
    l.net_to_tenant_cents,
    CASE WHEN COALESCE(l.source_amount_cents, 0) > 0
         THEN round(l.source_tax_cents::numeric * l.gross_cents / l.source_amount_cents)::int
         ELSE 0 END                                         AS tax_cents,
    CASE WHEN COALESCE(l.source_amount_cents, 0) > 0
         THEN round(l.source_tip_cents::numeric * l.gross_cents / l.source_amount_cents)::int
         ELSE 0 END                                         AS tip_cents,
    CASE WHEN COALESCE(l.source_amount_cents, 0) > 0
         THEN round(l.source_gift_card_cents::numeric * l.gross_cents / l.source_amount_cents)::int
         ELSE 0 END                                         AS gift_card_applied_cents
FROM ledger l

UNION ALL

-- ── Part 2: CHARGED rental deposits (the fallback + legacy path) ──────────────────────────────
-- Only deposits that were actually CHARGED (deposit_pi_id IS NULL) appear here: the deposit rode the
-- rental PaymentIntent, so real money came in and the track owes it back. A HELD deposit
-- (deposit_pi_id set) moved no money, so it produces nothing until damage is captured (which then
-- rides Part 1 as a 'rental_deposit' sale).
--
--   deposit_collected  money in, held        →  DR tender / CR deposit liability
--   deposit_released   hold ends on return   →  DR deposit liability / CR tender
--
-- The captured damage is NOT emitted here; it is an ordinary 'rental_deposit' sale in the ledger,
-- so Part 1 credits that revenue. Emitting it here too would double-count it.
--
-- A charged $200 deposit with $50 kept:
--     Part 2 collected   DR receivable 200 / CR liability  200
--     Part 2 released     DR liability  200 / CR receivable 200
--     Part 1 sale         DR receivable  50 / CR forfeited-deposit revenue 50
--   liability nets to 0, receivable nets to +50 (owed to the track), revenue is 50. The other 150
--   went back to the rider and was never the track's money.
--
-- Anchored to the rental's own sale ledger row so the deposit lands on the same business date as the
-- rental that carried it, and so a deposit can never be booked for a rental whose sale was not booked.
SELECT
    r.tenant_id,
    NULL::uuid                                              AS ledger_entry_id,
    d.entry_kind,
    'rental'                                                AS source_kind,
    r.id                                                    AS source_id,
    d.occurred_at_utc,
    (d.occurred_at_utc AT TIME ZONE t.timezone)::date       AS business_date,
    r.payment_method,
    d.gross_cents,
    0                                                       AS stripe_fee_cents,
    0                                                       AS ridepass_cut_cents,
    0                                                       AS net_to_tenant_cents,
    0                                                       AS tax_cents,
    0                                                       AS tip_cents,
    0                                                       AS gift_card_applied_cents
FROM rental_purchase r
JOIN tenant t ON t.id = r.tenant_id
JOIN tenant_ledger_entry sale
      ON sale.tenant_id   = r.tenant_id
     AND sale.source_kind = 'rental'
     AND sale.source_id   = r.id
     AND sale.entry_kind  = 'sale'
CROSS JOIN LATERAL (
    VALUES
        ('deposit_collected', r.deposit_cents, sale.occurred_at_utc),
        ('deposit_released',  r.deposit_cents, r.returned_at)
) AS d(entry_kind, gross_cents, occurred_at_utc)
WHERE r.deposit_cents > 0
  AND r.deposit_pi_id IS NULL           -- CHARGED deposits only; a held deposit moved no money
  AND d.gross_cents > 0
  AND d.occurred_at_utc IS NOT NULL;    -- drops the release row until the rental is actually returned
