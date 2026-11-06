-- v_accounting_entries: carry tip PAYOUTS out to the books.
--
-- Script0294 added entry_kind 'tip_payout': cash handed to a staff member out of a drawer, written
-- with gross = -amount, payment_method 'cash', tax/fee/cut/net all 0, source_kind 'tip_payout' and
-- source_id = tip_payout.id. Nothing in the ledger CTE below joins on that source kind, so a payout
-- row came through this view with source_amount_cents NULL and therefore tip_cents 0, which
-- JournalEntryBuilder.AccrueSale would have read as a NEGATIVE revenue_other sale paid in cash:
-- revenue = gross - tax - tip = -amount. The liability it was meant to discharge never moved.
--
-- The one change here is the tip_cents branch: for a 'tip_payout' row tip_cents IS gross_cents (the
-- whole row is tip, and it is negative). AccrueSale then produces revenue = -amount - (-amount) = 0,
-- DR Tips payable amount (the -tip term) and CR Cash on hand amount (the cash term on a negative
-- gross), which is exactly the entry Toast and Square post for a tip-out, and the day still
-- balances because the fee, cut and receivable terms are all zero. tax_cents and
-- gift_card_applied_cents stay 0 through the existing ELSE, business_date is derived like every
-- other ledger row, and source_kind 'tip_payout' passes straight through from the ledger. The view
-- never filtered on entry_kind, so no predicate needed widening; the consumers that do
-- (EndOfDayReportRepository, ReportsRepository) restrict themselves to sale/refund and are
-- deliberately unaffected: a payout is not revenue and must not show up as a negative sale.
--
-- Byte-identical to Script0274 apart from that branch. Column list, order and types are unchanged
-- (the new branch yields the same int the round()::int branch does), so CREATE OR REPLACE replaces
-- in place and every dependent object stays valid. No DROP VIEW, for the reason Script0273 gives.

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
            -- The base the shop ledger's gross is measured against, see the header.
            WHEN 'shop_sale'    THEN ss.total_cents - ss.deposit_applied_cents - ss.credit_applied_cents
            WHEN 'shop_rental'  THEN sr.total_cents
            ELSE NULL
        END                                     AS source_amount_cents,
        COALESCE(etp.tax_cents, cs.tax_cents, ss.tax_cents, sr.tax_cents, 0) AS source_tax_cents,
        COALESCE(cs.tip_cents, 0)                AS source_tip_cents,
        COALESCE(gcr.amount_cents, 0)            AS source_gift_card_cents,
        -- Department override, resolved through whichever event this row hangs off.
        CASE l.source_kind
            WHEN 'event_ticket' THEN tet_tix.revenue_key
            WHEN 'extras'       THEN tet_ext.revenue_key
            ELSE NULL
        END                                      AS revenue_key_override
    FROM tenant_ledger_entry l
    JOIN tenant t                       ON t.id = l.tenant_id
    LEFT JOIN event_ticket_purchase etp ON l.source_kind = 'event_ticket' AND etp.id = l.source_id AND etp.tenant_id = l.tenant_id
    LEFT JOIN event_extra_purchase eep  ON l.source_kind = 'extras'       AND eep.id = l.source_id AND eep.tenant_id = l.tenant_id
    LEFT JOIN season_pass_purchase spp  ON l.source_kind = 'season_pass'  AND spp.id = l.source_id AND spp.tenant_id = l.tenant_id
    LEFT JOIN membership_purchase mp    ON l.source_kind = 'membership'   AND mp.id  = l.source_id AND mp.tenant_id  = l.tenant_id
    LEFT JOIN rental_purchase rp        ON l.source_kind = 'rental'       AND rp.id  = l.source_id AND rp.tenant_id  = l.tenant_id
    LEFT JOIN concession_sale cs        ON l.source_kind = 'concession'   AND cs.id  = l.source_id AND cs.tenant_id  = l.tenant_id
    LEFT JOIN shop_sale ss              ON l.source_kind = 'shop_sale'    AND ss.id  = l.source_id AND ss.tenant_id  = l.tenant_id
    LEFT JOIN shop_rental sr            ON l.source_kind = 'shop_rental'  AND sr.id  = l.source_id AND sr.tenant_id  = l.tenant_id
    LEFT JOIN gift_card_redemption gcr  ON gcr.source_kind = l.source_kind AND gcr.source_id = l.source_id AND gcr.tenant_id = l.tenant_id
    -- Ticket → tier → event → type. Four hops, all tenant-scoped.
    LEFT JOIN event_ticket_tier ett     ON ett.id = etp.tier_id           AND ett.tenant_id = l.tenant_id
    LEFT JOIN event e_tix               ON e_tix.id = ett.event_id        AND e_tix.tenant_id = l.tenant_id
    LEFT JOIN tenant_event_type tet_tix ON tet_tix.id = e_tix.event_type_id AND tet_tix.tenant_id = l.tenant_id
    -- Extras carry their own event_id, nullable since Script0063 for counter merchandise.
    LEFT JOIN event e_ext               ON e_ext.id = eep.event_id        AND e_ext.tenant_id = l.tenant_id
    LEFT JOIN tenant_event_type tet_ext ON tet_ext.id = e_ext.event_type_id AND tet_ext.tenant_id = l.tenant_id
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
    -- A tip payout is all tip: the whole (negative) gross discharges Tips payable. See the header.
    CASE WHEN l.entry_kind = 'tip_payout' THEN l.gross_cents
         WHEN COALESCE(l.source_amount_cents, 0) > 0
         THEN round(l.source_tip_cents::numeric * l.gross_cents / l.source_amount_cents)::int
         ELSE 0 END                                         AS tip_cents,
    CASE WHEN COALESCE(l.source_amount_cents, 0) > 0
         THEN round(l.source_gift_card_cents::numeric * l.gross_cents / l.source_amount_cents)::int
         ELSE 0 END                                         AS gift_card_applied_cents,
    l.revenue_key_override
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
    0                                                       AS gift_card_applied_cents,
    NULL::text                                              AS revenue_key_override
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
  AND d.occurred_at_utc IS NOT NULL     -- drops the release row until the rental is actually returned

UNION ALL

-- ── Part 3: gift cards that were SOLD ─────────────────────────────────────────────────────────
-- The credit side the liability has always been missing. One row per paid, non-imported card:
--
--     DR tender (receivable / Stripe clearing) face value
--     CR liability_gift_card                   face value
--
-- and JournalEntryBuilder.AccrueSale then debits that liability back down as the card is spent, so
-- the two finally close. A $100 card sold and fully redeemed on a $100 ticket with a $4 cut leaves
-- liability_gift_card at zero and the receivable at $96, which is exactly the payout.
--
-- payment_method mirrors the charge mode snapshotted on the card itself, never the tenant's CURRENT
-- stripe_charge_mode, for the same reason JournalEntryBuilder refuses to take a mode parameter:
-- flipping a tenant to direct charge must not re-book history. A direct-charge card was sold on the
-- track's own connected account, so its float sits in their Stripe balance, not in our receivable.
--
-- The full rationale for synthesizing these rows here instead of writing them to
-- tenant_ledger_entry (it would pay the track its gift-card float twice) is in the Script0273
-- header, which this script otherwise reproduces verbatim.
SELECT
    g.tenant_id,
    NULL::uuid                                              AS ledger_entry_id,
    'gift_card_sold'                                        AS entry_kind,
    'gift_card'                                             AS source_kind,
    g.id                                                    AS source_id,
    g.created_at                                            AS occurred_at_utc,
    (g.created_at AT TIME ZONE t.timezone)::date            AS business_date,
    CASE WHEN COALESCE(g.stripe_connected_account_id, '') <> ''
         THEN 'stripe_direct' ELSE 'stripe' END             AS payment_method,
    g.initial_amount_cents                                  AS gross_cents,
    0                                                       AS stripe_fee_cents,
    0                                                       AS ridepass_cut_cents,
    g.initial_amount_cents                                  AS net_to_tenant_cents,
    0                                                       AS tax_cents,
    0                                                       AS tip_cents,
    0                                                       AS gift_card_applied_cents,
    NULL::text                                              AS revenue_key_override
FROM gift_card g
JOIN tenant t ON t.id = g.tenant_id
WHERE g.stripe_payment_intent_id IS NOT NULL
  AND g.imported_from IS NULL
  AND g.status NOT IN ('pending', 'void')
  AND g.initial_amount_cents > 0;
