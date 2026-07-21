-- Season pass holder registration: one order, many passes, one holder each.
--
-- Until now a season pass purchase was implicitly "the buyer's own pass": Buy took a single
-- product id plus one selfie, and the purchaser WAS the holder. That made the common case
-- impossible — a parent buying passes for three kids had to run checkout three times, and
-- every pass carried the parent's name.
--
-- The unified checkout (mirroring the event-ticket flow) now creates one pending row per pass
-- on a single PaymentIntent, and collects each holder's identity, photo, and waiver signature
-- AFTER payment in a registration step. So each row needs to name its own holder, independent
-- of purchaser_name / purchaser_user_id (which stay = whoever paid).
--
-- All three columns are nullable: existing rows predate the concept (their holder is the
-- purchaser), and a freshly-created pending row has no holder until registration finishes.

ALTER TABLE season_pass_purchase
    ADD COLUMN IF NOT EXISTS holder_first_name text NULL,
    ADD COLUMN IF NOT EXISTS holder_last_name  text NULL,
    ADD COLUMN IF NOT EXISTS holder_birthdate  date NULL;

-- Backfill the holder from the purchaser for every pass that predates the registration step.
-- Those passes were genuinely bought by their holder, so purchaser_name IS the holder name;
-- without this they'd all read as unregistered and the gate would refuse to check them in.
--
-- purchaser_name is a single free-text field ("Jane Q Rider"), so split on the FIRST space:
-- everything before it is the first name, the remainder is the last name ("Mary Jo Van Dyke"
-- -> "Mary" + "Jo Van Dyke"). A single-word name has no last name and stores NULL rather than
-- '' so it reads as "not supplied" consistently.
--
-- The CASE is load-bearing: position(' ') returns 0 when there's no space, and substring(FROM 1)
-- would then return the WHOLE name, so a single-word "Madonna" would land as first="Madonna",
-- last="Madonna" and display as "Madonna Madonna".
--
-- WHERE holder_first_name IS NULL keeps this a no-op on re-run and never clobbers a holder
-- captured by the registration step.
UPDATE season_pass_purchase
SET holder_first_name = NULLIF(split_part(trim(purchaser_name), ' ', 1), ''),
    holder_last_name  = CASE
        WHEN position(' ' IN trim(purchaser_name)) = 0 THEN NULL
        ELSE NULLIF(trim(substring(trim(purchaser_name) FROM position(' ' IN trim(purchaser_name)) + 1)), '')
    END
WHERE holder_first_name IS NULL
  AND trim(coalesce(purchaser_name, '')) <> '';

-- Allow status = 'failed'.
--
-- StripePurchaseFinalizer has always written 'failed' onto a season pass whose PaymentIntent
-- failed, but this CHECK never permitted it — so that write raised a check violation instead
-- of marking the row, leaving failed passes stuck 'pending' forever. Every other purchase
-- table (event_ticket_purchase, event_extra_purchase) already allows 'failed'; this brings
-- season passes in line. Widening a CHECK can't invalidate existing rows, so no NOT VALID
-- dance is needed, and old code paths keep working since nothing wrote 'failed' successfully.
ALTER TABLE season_pass_purchase DROP CONSTRAINT IF EXISTS season_pass_purchase_status_check;
ALTER TABLE season_pass_purchase
    ADD CONSTRAINT season_pass_purchase_status_check
    CHECK (status IN ('pending','paid','failed','cancelled','refunded'));

-- Gate rule, for the record (enforced in SeasonPassController, not by a constraint): a pass is
-- checkable-in only once it has a photo AND, when its product requires a waiver, a signature.
-- A constraint can't express it — the row is deliberately created incomplete at checkout and
-- completed a step later, so any NOT NULL here would reject the pending insert.
--
-- No index for that check: it's always reached through an already-indexed lookup (a reservation
-- id at the gate, or purchaser_user_id on the rider's pass list), so a partial index would just
-- cost write throughput on every purchase without serving a query.
