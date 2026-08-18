-- v_accounting_entries: book gift-card SALES, and give the bike shop its own revenue breakout.
--
-- ══ Why gift-card sales are SYNTHESIZED here instead of written to tenant_ledger_entry ══
--
-- JournalEntryBuilder.AccrueSale debits liability_gift_card by the redeemed amount every time a
-- card is spent, which is right, but nothing has ever credited that liability, because a gift-card
-- purchase writes no ledger row at all (StripePurchaseFinalizer activates the card and returns;
-- GiftCardRepository.Create only touches gift_card). In QuickBooks the account therefore walks
-- steadily negative as cards are redeemed: a liability that was never created is being discharged.
--
-- The obvious fix, writing a 'gift_card' sale row into tenant_ledger_entry when a card is bought,
-- would break payouts. tenant_ledger_entry is not a bookkeeping table, it is what the tenant gets
-- PAID from: TenantLedgerRepository.GetSummaries sums net_to_tenant_cents over every row with no
-- entry_kind filter, and TenantPayoutRepository attaches every unpaid row to the next payout. In
-- platform charge mode RidePass holds the gift-card float and hands it to the track at REDEMPTION,
-- through the redeemed sale's own net_to_tenant. A ledger row at sale time would pay the track the
-- face value twice: once when the card was bought and again when it was spent.
--
-- So the sale rows are synthesized in this read model, exactly the way Part 2 already synthesizes
-- rental-deposit lifecycle rows that the payout ledger deliberately does not carry. The books get
-- their credit; the payout math is untouched. There is no backfill, and none is wanted: every gift
-- card that was ever sold already has its row here the moment this view is replaced.
--
-- Which cards count as SOLD (Part 3's predicate):
--
--     g.stripe_payment_intent_id IS NOT NULL   -- somebody paid money for it
--     AND g.imported_from IS NULL              -- not a legacy balance carried in from another
--                                              --   system (Script0272); that float was collected
--                                              --   by the old POS and is not a RidePass sale
--     AND g.status NOT IN ('pending', 'void')  -- 'pending' is minted-but-unpaid, 'void' is the
--                                              --   declined/abandoned cleanup; neither was paid
--
-- Nothing else can create a card: BuyGiftCard (PurchaseController) and ImportCard are the only two
-- insert paths, there is no comped/admin-issued/cash gift-card sale anywhere in the codebase, and
-- BuyGiftCard always goes through Stripe. So "has a PaymentIntent and is not an import" is exactly
-- "money changed hands". Known limitation: an admin void of a card that WAS paid for (VoidActive,
-- lost/fraud) also lands in status 'void' and so drops out of this view. That erases a real sale
-- rather than booking the unspent balance as breakage income. It is rare, it is the conservative
-- direction (we never invent a liability), and separating it would need a column gift_card does
-- not have. Worth a dedicated breakage entry later.
--
-- occurred_at_utc is g.created_at. gift_card has no activated_at or paid_at column: the card is
-- minted at PaymentIntent-creation time and Activate() flips status seconds later on the webhook,
-- so created_at IS the paid timestamp to within seconds. updated_at cannot be used, the
-- trg_gift_card_updated_at trigger bumps it on every redemption, which would drag a card's sale
-- forward into whatever business date it was last spent on.
--
-- gross is initial_amount_cents, the FACE VALUE only. The buyer also pays the tenant's service
-- charge on top (BuyGiftCard: totalToCharge = amount + serviceCharge), but that is RidePass's
-- income, never the track's, so it has no place in the track's books. Tax, tip and
-- gift_card_applied are all zero: selling a gift card is not a taxable sale, and a card cannot pay
-- for itself. stripe_fee and ridepass_cut are zero and net_to_tenant equals gross, so the receivable
-- carries the full float, which is what JournalEntryBuilder.AccrueGiftCardSale credits the liability
-- against. There is no refund path for a gift-card PURCHASE anywhere in the code (status 'refunded'
-- exists in the CHECK constraint but nothing ever writes it), so no reversal rows are emitted; if
-- one is ever built it needs a negative row here.
--
-- ══ Why the bike shop gets its own CASE branches ══
--
-- The CASE in Script0183 knew six source kinds. Everything else, and that means every bike shop
-- row, returned source_amount_cents = NULL, which forces the tax and tip proration to 0 and books
-- the shop's sales tax as revenue. shop_sale and shop_rental both carry their own tax_cents, so
-- they join here and break it out properly.
--
-- The shop_sale denominator is total_cents MINUS deposit_applied_cents and credit_applied_cents,
-- not total_cents, because that is precisely the base the ledger's gross is measured against:
-- OnShopSalePaid books gross = total - deposit_applied - credit_applied (a work-order deposit
-- already booked its own 'shop_wo_deposit' row and store credit was booked when it was funded, so
-- neither is collected again at bill-out). Using total_cents would prorate the tax down by the
-- deposit's share and silently lose it, since the deposit row itself carries no tax. With this
-- denominator a full sale recognizes its full tax and a refund mirror recognizes exactly the
-- negative of it, which is the same proration semantics every other branch has.
--
-- shop_rental uses total_cents flat: OnShopRentalPaid books gross = rental.TotalCents. Neither
-- table has a tip column any more (Script0243 removed the shop tip), so tips stay 0 for both.
--
-- Giving shop_sale a non-NULL source amount also switches ON the gift-card proration that was
-- already wired for it: the bike shop register records its gift-card redemptions with
-- source_kind = 'shop_sale' (which is why Script0272 had to widen the redemption CHECK), and the
-- shop's gross deliberately INCLUDES the gift-funded portion, so those cents now land on
-- liability_gift_card instead of being invisible.
--
-- Rerunnable: CREATE OR REPLACE VIEW, and the column list, order and types are identical to
-- Script0183, so no DROP VIEW is needed. The DROP VIEW IF EXISTS guard below is deliberately NOT
-- used for that reason; replacing in place keeps any dependent object valid.

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
        COALESCE(gcr.amount_cents, 0)            AS source_gift_card_cents
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
    0                                                       AS gift_card_applied_cents
FROM gift_card g
JOIN tenant t ON t.id = g.tenant_id
WHERE g.stripe_payment_intent_id IS NOT NULL
  AND g.imported_from IS NULL
  AND g.status NOT IN ('pending', 'void')
  AND g.initial_amount_cents > 0;
