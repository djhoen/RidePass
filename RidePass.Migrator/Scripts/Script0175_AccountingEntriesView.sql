-- v_accounting_entries: the read model the QuickBooks sync posts from.
--
-- tenant_ledger_entry is the right anchor for an accounting sync. It already normalises all seven
-- sale kinds plus refunds, chargebacks and platform charges into one signed, idempotent,
-- tenant-scoped stream, and all three processes that finalise a payment (the Stripe webhook,
-- Payment/ConfirmIntent, and PendingPurchaseReconciler) write it through StripePurchaseFinalizer.
-- But it cannot answer two questions a set of books must answer:
--
--   1. "How much of this was sales tax?" gross_cents silently contains tax and tips. Tax collected
--      on behalf of a jurisdiction is a LIABILITY, not revenue. The tenant is merchant of record
--      and remits it (see TaxController). Booking it as revenue overstates income and understates
--      what they owe. Same for tips. So we join back to the source rows to break them out.
--   2. "What tender funded this?" A gift-card-funded sale moves no card money at redemption; it
--      draws down a liability created when the card was SOLD. Without the redemption join we'd book
--      the gift card twice as revenue: once on sale, once on redemption.
--
-- Why a view rather than new columns on tenant_ledger_entry: the ledger is the payout ledger. Its
-- numbers drive real money movement (MonthlyPayoutDrafter). Denormalising accounting-only fields
-- onto it means touching the hot write path in StripePurchaseFinalizer for every sale kind, for a
-- consumer that reads once a night. A view keeps the blast radius at zero and cannot drift, because
-- it derives from the same rows the payout math uses.
--
-- ── The proration trick ──────────────────────────────────────────────────────────────────────
-- A refund's ledger row carries a NEGATIVE gross and points at the same source row as its sale, so
-- the source's positive tax_cents/tip_cents can't be used as-is. Every derived amount is instead
-- scaled by (gross_cents / source_amount_cents):
--     • sale          → ratio  1  → the full source tax/tip
--     • full refund   → ratio -1  → the exact negatives
--     • partial refund→ ratio  <1 → prorated, matching how AdmissionTax rounds per row
-- One expression, all three cases, and it stays correct if partial-refund support widens later.
--
-- ── revenue = gross - tax - tip, uniformly ───────────────────────────────────────────────────
-- This holds for every kind, which is why the sync needs no per-kind revenue special-casing:
--   • concessions: total = subtotal - discount + (prices_include_tax ? 0 : tax) + tip. When tax is
--     added on top, subtracting it backs out what we added; when prices are tax-inclusive the tax
--     already sits inside subtotal, so subtracting it still backs it out. Either way the remainder
--     is subtotal - discount, the real revenue.
--   • event tickets: amount_cents is stored tax-INCLUSIVE in both modes (AdmissionTax.cs), so the
--     same subtraction backs the tax out.
--   • everything else carries no tax or tips, and the COALESCE(...,0) makes it a no-op.
-- Discounts and comps are already netted out of the source amount, so revenue lands net of them.
-- We deliberately do NOT gross-up-and-contra: booking a discount as contra-revenue would require a
-- reliable pre-discount gross that concession_sale does not store, and a wrong gross-up is worse
-- than an honest net. Discounts stay visible in the RidePass Reports screens.
--
-- Idempotent: CREATE OR REPLACE VIEW. Read-only and additive. Nothing consumes it yet but the
-- QuickBooks sync, and no existing query changes.

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
        -- The positive, sale-side amount this ledger row derives from. The denominator of the
        -- proration ratio above. NULL for platform charges (sms/email/dispute), which have no
        -- source row and need no breakout.
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
        -- gift_card_redemption is unique on (source_kind, source_id), so this stays 1:1. It covers
        -- BOTH the fully-covered case (payment_method='voucher', no card charge) and the partial
        -- case (payment_method='stripe' on the reduced remainder), in both, this many cents came
        -- out of the gift card liability rather than off a card.
        COALESCE(gcr.amount_cents, 0)            AS source_gift_card_cents
    -- Every join carries tenant_id as well as the id. The id alone would be enough in a correct
    -- database (source_id is a PK and a ledger row always points at its own tenant's purchase), but
    -- this is the read model an accounting sync posts from: a single mismatched row would put one
    -- track's money in another track's books. The predicate makes that unrepresentable rather than
    -- merely unlikely, and costs nothing: these are all PK lookups either way.
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
    -- Tenant-LOCAL calendar date, not a UTC date: a Saturday-night gate take belongs on Saturday's
    -- books, and bucketing by UTC would shove a US evening event onto Sunday.
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

-- ── Part 2: rental security deposits, the HOLD lifecycle only ────────────────────────────────
-- RentalController charges rental + deposit on ONE PaymentIntent (rental_pi_id) but sets
-- rental_purchase.amount_cents to the rental portion only, and OnRentalPaid books gross =
-- amount_cents. So while a deposit is merely HELD it is deliberately absent from the ledger: a
-- refundable deposit is not earnings and must not inflate the tenant's payout balance while it may
-- still go back to the rider. The platform holds the float, exactly as it does for gift cards.
--
-- A set of books still has to show it, though: money came in, and the track owes it back. So these
-- two rows track the liability and nothing else:
--
--   deposit_collected  money in, held        →  DR tender / CR deposit liability
--   deposit_released   hold ends on return   →  DR deposit liability / CR tender
--
-- Note what is NOT here: the captured damage. That is real income and it now rides the ledger as an
-- ordinary sale (source_kind='rental_deposit', written by RentalController on return, Script0179),
-- so Part 1 above already picks it up and credits the revenue. Emitting a 'deposit_captured' row
-- here as well would book the same damage twice.
--
-- The three pieces compose to the right answer. A $200 deposit with $50 kept:
--     Part 2 collected   DR receivable 200 / CR liability  200
--     Part 2 released     DR liability  200 / CR receivable 200
--     Part 1 sale         DR receivable  50 / CR forfeited-deposit revenue 50
--   liability nets to 0 (the hold is over), receivable nets to +50 (what the platform now owes the
--   track), revenue is 50. The other 150 went back to the rider and was never the track's money.
--
-- Anchored to the rental's own sale ledger row rather than rental_purchase.created_at so the
-- deposit lands on the same business date as the rental that carried it, and so a deposit can never
-- be booked for a rental whose sale was never booked.
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
        -- Money in, held against the rental.
        ('deposit_collected', r.deposit_cents, sale.occurred_at_utc),
        -- The hold ends on return: the WHOLE deposit stops being held, whatever its fate. The kept
        -- portion becomes revenue via the Part 1 'rental_deposit' sale; the rest goes to the rider.
        ('deposit_released',  r.deposit_cents, r.returned_at)
) AS d(entry_kind, gross_cents, occurred_at_utc)
WHERE r.deposit_cents > 0
  AND d.gross_cents > 0
  AND d.occurred_at_utc IS NOT NULL;   -- drops the release row until the rental is actually returned
