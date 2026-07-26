# Season Pass Upgrades, and the Upgrade-Offer Drip

Implementation-ready design. Status: proposed, not implemented. 2026-07-25.

## 1. Requirements

1. A tenant offers an upgrade from **pass A to pass B for a set price**.
2. A holder whose pass is **not used up** sees "upgrade available", which opens the upgrade details.
3. A **scheduled marketing job** emails pass owners **X days after purchase** to tell them an
   upgrade is available.
4. The email content is authored in the **recurring campaign**.

## 2. Current state, verified

**Nothing upgrade-shaped exists.** No upgrade table, no upgrade column, no path from one pass
product to another.

What exists and this design builds on:

- **Pass products and purchases** with three kinds (`unlimited`, `days_of_week`, `credits`), a
  validity window (`valid_from_date` / `valid_to_date`, both NOT NULL), and `credits_remaining`
  for credit packs.
- **A generic deferred-job queue**: `scheduled_task` (kind, jsonb payload, `run_at_utc`,
  attempts, retry/backoff) dispatched by `TaskRunner`'s 60-second loop to per-kind
  `IScheduledTaskHandler`s. Existing kinds: `send_campaign`, `send_rider_message`. The runner's
  own header calls this "the intended home for every deferred job other than the monthly
  drafter."
- **Tenant-spanning periodic sweeps** as a separate pattern in the same process (monthly payout
  drafter on a 30m tick, QuickBooks sync hourly), documented as standalone "because it's a
  single periodic sweep, not a per-row job."
- **Marketing email machinery**: `ISmtpEmailer`, suppression checks with a `marketing` flag
  (`IsSuppressed(email, tenantId, marketing)`), `List-Unsubscribe` headers, a visible
  unsubscribe footer, and **per-email billing** into the tenant ledger via
  `EmailPricing.MarginalChargeCents` as an `email_charge` entry.

### 2.1 The existing campaign is the wrong shape for requirement 3

`email_campaign` is a **one-shot broadcast**: one subject/body, a `scheduled_for`, a `sent_at`,
and a recipient list materialized from the **newsletter subscriber list**
(`_subscribers.ListActiveForSend`). It has no recurrence and no audience concept.

What requirement 3 describes is not a broadcast. It is a **per-purchase lifecycle email**:
triggered X days after *each rider's own* purchase, addressed to *pass owners* rather than
newsletter subscribers, and sent once per pass. Those differ in trigger, audience, and dedupe,
which is all three axes.

So this design **reuses the sending machinery** (SMTP, suppression, unsubscribe, billing) and
**does not reuse the campaign tables**. Bolting recurrence onto `email_campaign` would mean a
row that is simultaneously a template and a send record, and "when did this campaign go out"
stops having an answer.

## 3. Upgrade paths

```sql
CREATE TABLE IF NOT EXISTS season_pass_upgrade_path (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    from_product_id uuid NOT NULL REFERENCES season_pass_product(id) ON DELETE CASCADE,
    to_product_id   uuid NOT NULL REFERENCES season_pass_product(id) ON DELETE CASCADE,
    -- What the holder pays to move up. Flat, not a computed difference: the tenant decides what
    -- the upgrade is worth, which is rarely just (B - A).
    price_cents     int  NOT NULL CHECK (price_cents >= 0),
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_upgrade_path_distinct CHECK (from_product_id <> to_product_id)
);

-- One offer per direction. Two live "3-pack to unlimited" rows at different prices is a
-- coin-flip at checkout, not a feature.
CREATE UNIQUE INDEX IF NOT EXISTS uk_upgrade_path_pair
    ON season_pass_upgrade_path (from_product_id, to_product_id);
```

`price_cents >= 0` rather than `> 0`: a free upgrade is a legitimate goodwill gesture, and
unlike a free *product* (which would be publicly listed and unbuyable) an upgrade is only ever
reachable by an existing holder.

**Not enforced, deliberately**: that B is "better" than A. Nothing in the data says one pass
beats another, and a track may well offer a sideways move (weekday pass to weekend pass) for a
fee. Encoding a hierarchy would be inventing an ordering the tenant did not ask for.

## 4. "Not used up"

Eligibility is the same vocabulary the gate already uses, plus a kind-specific test:

| Kind | Not used up when |
|---|---|
| `unlimited` | today (tenant-local) `<= valid_to_date` |
| `days_of_week` | same as unlimited |
| `credits` | `credits_remaining > 0` **and** within the window |

All kinds additionally require `status = 'paid'`. An expired pass is not upgradeable: the
upgrade sells the *remainder* of a season, and there is no remainder. A track that wants to sell
a lapsed holder something is selling them a new pass, not an upgrade.

Note the credits case is why "used up" cannot be a single date test: a 3-pack with 0 rides left
is used up on day one of a 12-month window.

## 5. The upgrade transaction

This is the part with a wrong answer that looks right.

**Rejected: mutate `product_id` on the existing purchase.** It is tempting because it preserves
the rider's QR, their photo, their signed waiver, and their ID verification with no work. But
the ledger already recorded a sale of product A at price A, and the purchase row is the thing
reporting joins to. Flipping the product silently rewrites history: revenue-by-product,
the sales list, and any refund of the original all start describing a purchase that never
happened.

**Adopted: a new purchase, and the old one retired.**

```sql
ALTER TABLE season_pass_purchase
    ADD COLUMN IF NOT EXISTS upgraded_from_purchase_id uuid NULL REFERENCES season_pass_purchase(id);

ALTER TABLE season_pass_purchase
    DROP CONSTRAINT IF EXISTS season_pass_purchase_status_check;
ALTER TABLE season_pass_purchase
    ADD CONSTRAINT season_pass_purchase_status_check
    CHECK (status = ANY (ARRAY['pending','paid','failed','cancelled','refunded','upgraded']));
```

The `'upgraded'` status earns its place for free: **every admission and benefit path already
requires `status = 'paid'`**, so the moment the old row flips it stops admitting, stops granting
benefits, and stops appearing as an active pass, with no new enforcement written anywhere. This
is the same property the employee-pass `pending` state relies on.

### 5.1 What must carry forward

The new pass is the same human, so making them re-register would be an upgrade experience worse
than not upgrading. Copy, and treat this list as load-bearing:

- `photo_data_url` — else the pass is unregistered and will not scan
- `waiver_signature_id`
- `holder_first_name`, `holder_last_name`, `holder_birthdate`
- `id_verified_at`, `id_verified_by_user_id`, `id_verified_dob` — re-carding someone the track
  already carded is exactly the friction Script0238 was built to remove
- `purchaser_user_id`, `purchaser_email`, `purchaser_name`

**Credits do not carry** by default: upgrading a part-used 3-pack to unlimited is buying a
different thing, not topping up. Where B is also a credit pack, see §11.

### 5.2 The QR changes, and someone must be told

A new purchase means a new `redemption_token`, so a rider with the old pass saved to their phone
wallet has a dead QR. Options: reissue silently and rely on the confirmation email, or carry the
old token onto the new row.

Recommend **reissue**, and make the upgrade confirmation say plainly that the old pass QR no
longer works. Carrying the token would make two purchase rows share a credential, which breaks
the assumption that a token identifies one purchase and would confuse the gate scan lookup.

### 5.3 Money

Charge `price_cents` through the existing season pass purchase path (Stripe, coupons, credit,
the lot). The upgrade is a normal sale of product B at a non-list price, so the ledger entry,
fee calculation, and payout all behave as they already do. `upgraded_from_purchase_id` is the
only thing that marks it as an upgrade.

**The old pass is not refunded.** Its value was consumed as part of the upgrade price the tenant
set. Do not write a refund entry: it would inflate refund reporting and imply money moved.

## 6. Rider surface

- **My Passes**: on any pass with a live upgrade path and a not-used-up state, an "Upgrade
  available" chip and a CTA.
- **Upgrade details page**: what B gives that A does not (product description plus the benefit
  rows already rendered on the buy page), the price, and what happens to the current pass. The
  last part matters: "your current pass is replaced" is not a detail to bury.
- Checkout reuses the season pass purchase flow.

## 7. The recurring campaign

A definition, not a send record:

```sql
CREATE TABLE IF NOT EXISTS season_pass_upgrade_campaign (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- NULL = every active upgrade path. Set = only offers on this one.
    upgrade_path_id   uuid NULL REFERENCES season_pass_upgrade_path(id) ON DELETE CASCADE,
    subject           text NOT NULL,
    body_html         text NOT NULL,
    body_text         text NULL,
    -- Requirement 3's "after X days": measured from the holder's own purchase, not a calendar.
    days_after_purchase int NOT NULL CHECK (days_after_purchase >= 0),
    is_active         boolean NOT NULL DEFAULT false,
    created_by_user_id uuid NULL REFERENCES users(id),
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);

-- One send per pass per campaign, ever. This table IS the dedupe: without it a sweep that
-- crashes halfway through re-emails everyone it already reached on the next tick.
CREATE TABLE IF NOT EXISTS season_pass_upgrade_campaign_send (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    campaign_id       uuid NOT NULL REFERENCES season_pass_upgrade_campaign(id) ON DELETE CASCADE,
    pass_purchase_id  uuid NOT NULL REFERENCES season_pass_purchase(id) ON DELETE CASCADE,
    email             text NOT NULL,
    status            text NOT NULL DEFAULT 'sent' CHECK (status IN ('sent','failed','skipped')),
    skip_reason       text NULL,
    sent_at           timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_upgrade_campaign_send_once
    ON season_pass_upgrade_campaign_send (campaign_id, pass_purchase_id);
```

`is_active` defaults to **false**. A campaign that starts sending the moment it is saved would
email the tenant's entire back catalogue of pass holders on the next tick, and bill them for it.
Authoring and arming are separate acts.

## 8. The sweep

A tenant-spanning periodic sweep, matching the payout drafter and QuickBooks patterns rather
than the per-row queue, because there is no row to enqueue against until the sweep finds one.
Suggested cadence: hourly, or daily at a fixed hour. Daily is enough for a "day X" trigger and
keeps the blast radius of a bug to one run.

Per active campaign, select passes where:

1. `status = 'paid'`, product matches the campaign's path `from_product_id`
2. `created_at <= now() - days_after_purchase`
3. not used up (§4)
4. **not already upgraded** — no purchase whose `upgraded_from_purchase_id` is this one
5. no `season_pass_upgrade_campaign_send` row for (campaign, purchase)
6. a deliverable email, `IsSuppressed(email, tenantId, marketing: true)` false

Then send, and write the send row **whether it succeeded, failed, or was skipped**, with the
reason. A failure that writes nothing gets retried forever every tick.

Reuse `SendCampaignHandler`'s existing helpers for the `List-Unsubscribe` header and the visible
unsubscribe footer. These are marketing emails under CAN-SPAM; the transactional exemption does
not cover "buy our better pass."

### 8.1 This costs money on every tick

Each send bills the tenant through `EmailPricing.MarginalChargeCents` as an `email_charge`
ledger entry, exactly as broadcasts do. A drip differs from a broadcast in that the tenant sets
it up once and it keeps charging, scaling with pass sales.

The admin UI must therefore show, before arming: **how many holders currently match**, and a
plain statement that this sends continuously. A tenant discovering a recurring charge from a
switch they flipped months ago is a support ticket and a refund conversation.

## 9. Interaction with buddy and employee passes

- **Employee passes** (`is_employee`) must be excluded from upgrade paths and from the sweep.
  They are grants, not purchases, and emailing staff a marketing offer to upgrade the pass the
  track gave them is the wrong message.
- **Buddy entitlements** live on the product's benefits, so upgrading to a product with more
  buddy passes grants more. Already-spent redemptions stay attached to the OLD purchase id,
  which means the new pass starts with a full allowance. That is probably right (they bought a
  new entitlement) but it is a decision, not an accident: see §11.

## 10. Reporting

- Upgrades are ordinary sales, so revenue reporting needs no change.
- Worth adding: an upgrade funnel per path (offers sent, upgrades taken, conversion), readable
  from the send table joined to `upgraded_from_purchase_id`.

## 11. Open questions

1. **Flat price vs remaining value.** A 3-pack with 3 rides left and one with 1 left pay the
   same upgrade price. Simple and predictable, but a rider who has barely used their pass is
   effectively paying twice for the same days. Options: accept it, prorate by remaining
   credits/days, or let the tenant define several paths with different prices (which the schema
   allows only per product pair, not per remaining balance).
2. **Buddy allowance on upgrade.** Does the new pass start with a fresh buddy allowance even
   though the holder already spent two on the old one? §9 says yes by default. A track might
   reasonably want spent redemptions to carry across the upgrade.
3. **Multiple live passes.** Nothing stops a rider holding two passes. If they upgrade one, the
   other is untouched, which is right, but "upgrade available" needs to be per pass rather than
   per rider on the My Passes screen.
4. **Cadence and quiet hours.** Daily at what hour, in whose timezone? The tenant's, presumably,
   which means the sweep resolves each tenant's local hour rather than running once globally.
5. **One campaign or several per path?** The schema allows a campaign per path plus a catch-all
   (`upgrade_path_id IS NULL`). If both match a pass, which wins? Recommend: the path-specific
   one, with the catch-all skipped, mirroring how scoped benefits beat whole-surface ones.

## 12. Phasing

- **Phase 1**: upgrade paths, eligibility, the upgrade transaction (§5 including the carry-forward
  list), admin path editor, rider "upgrade available" + details + checkout. This is the feature;
  it is useful with no email at all.
- **Phase 2**: the recurring campaign, the sweep, the send log, and the pre-arm impact estimate.
- **Phase 3**: the upgrade funnel report.

Phase 1 first is deliberate. The drip markets a thing that has to exist and be proven at the
till before anyone is emailed about it.
