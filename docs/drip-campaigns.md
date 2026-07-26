# Upgrades Admin Page and the Drip Campaign Builder

Status: **Phases 1 and 2 built**, 2026-07-25. Phase 3 (multi-step reporting depth) still open.
Original design below, with the as-built decisions folded into §9 and §10.

Companion to `docs/pass-upgrades.md`, which covers the upgrade mechanics and the send job. This
document covers the two **admin surfaces**: where a tenant builds upgrade offers, and where they
build the automated emails that market them.

## 1. What the market does, and what to take from it

Researched before designing, because automation builders have well-worn conventions and
inventing new ones would make this harder to learn, not easier.

**Two schools.**

- **Canvas / visual journey builders**: Klaviyo Flows, Mailchimp's Customer Journey Builder,
  ActiveCampaign's automation builder, HubSpot Workflows. A node graph on a drag-drop canvas:
  trigger, wait, condition, branch, action. ActiveCampaign's is the deepest, supporting
  unlimited triggers, conditional logic, branching, goals, and automation split-testing.
- **Linear sequences**: Kit (ConvertKit) is explicit about the split, offering *Sequences* for
  straight-line email courses and reserving *Automations* for branching funnels.

The 2026 trend reported across comparisons is toward canvas builders with branching. **That is
the wrong target for RidePass**, and the reason is worth stating rather than assuming.

A canvas builder is the right answer when the *hard part is expressing the flow*: e-commerce
teams running abandoned-cart, browse-abandonment, win-back, and post-purchase journeys with
different branches per segment. A bike park sending "you have an upgrade available" 30 days
after purchase has one trigger, one wait, one email, and one exit condition. Building a node
editor for that is weeks of UI to express something a form expresses in six fields, and every
tenant would then face a blank canvas to do the one thing they wanted.

**What to take:**

| Convention | Take it? | Why |
|---|---|---|
| Trigger → wait → send, as the core spine | **Yes** | Universal across every platform; it is the mental model tenants arrive with |
| **Exit / goal conditions** ("stop if they buy") | **Yes, non-negotiable** | The single most important correctness feature. Without it you email people to buy a thing they already bought |
| Multi-step sequences (3-5 emails over 7-10 days is the documented welcome-series norm) | **Yes**, as ordered steps | Cheap once the spine exists; one row per step |
| Enrollment rules (once vs re-enroll) | **Yes**, but fixed to once | Re-enrollment is a footgun here; see §4.3 |
| Draft → active lifecycle with a pre-arm estimate | **Yes** | Already how `email_campaign` works, and the billing risk demands it |
| Send-time windows / quiet hours | **Yes**, simple version | Trivial to add at the sweep, and "don't email at 3am" is a real complaint |
| Visual canvas with branching | **No** | Weeks of UI for a problem that is currently one straight line |
| AI send-time optimisation, predictive segmentation, split testing | **No** | Klaviyo/ActiveCampaign-tier features with no audience here |

**Design rule that falls out**: model a drip as an **ordered list of steps**, not a graph. A
graph is a superset, and if branching is ever genuinely needed, a linear sequence migrates into
one cleanly (each step becomes a node). Starting with the graph and never needing it does not
migrate back.

## 2. Current state, verified

- **`email_campaign`** is a one-shot broadcast: subject, `body_html`/`body_text`,
  `scheduled_for`, `sent_at`, `recipient_count`, and a lifecycle of
  `draft → scheduled → sending → sent → failed`. Recipients come from the newsletter subscriber
  list. There is no recurrence and no audience concept.
- **`Campaigns.vue`** is the editor: a table of campaigns with per-status actions (compose,
  send, unschedule) and a `RichTextEditor` for the body. `CampaignController` exposes
  get/update/send/unschedule.
- **Nav** already groups marketing under `CampaignsManage`: Coupons, Subscribers, Campaigns.
- **`scheduled_task`** + `TaskRunner`'s 60s dispatcher handle per-row deferred work; standalone
  tenant-spanning sweeps (payout drafter, QuickBooks) are the pattern for periodic scans.
- **Sending machinery** to reuse as-is: SMTP, suppression with a `marketing` flag,
  `List-Unsubscribe`, the visible unsubscribe footer, and per-email ledger billing via
  `EmailPricing`.

## 3. Upgrades admin page

`/Admin/PassUpgrades`, under `CatalogManage` (it is pricing and product configuration, the same
bar as editing the passes themselves), linked from the Season Passes area.

**Layout: a matrix, not a list.** Upgrade paths are pairs, and a flat list of "A → B" rows makes
it hard to see the gaps. Rows are the tenant's active pass products, columns are the same
products, and each cell holds the price to move from row to column. Diagonal cells are blocked
(`chk_upgrade_path_distinct`). Most tracks have 3-6 products, so the grid stays readable and
immediately answers "what can a 3-pack holder move to?"

Per cell: a price, an active toggle, and an empty state that reads as "no offer" rather than
"free".

Also on the page:

- **Live holder counts per path**: "42 holders eligible today". Comes from the §4 eligibility
  rules and is what makes the pricing decision concrete.
- **A plain statement of what an upgrade does to the old pass** (replaced, QR reissued, no
  refund), because that is the part a tenant will otherwise learn from an angry rider.
- Employee products excluded from both axes, per `pass-upgrades.md` §9.

### 3.1 The link across to Automations

Configuration lives here; the email that markets it lives in Automations (§8.1). That split is
defensible but not self-evident, so the upgrades page has to point at the other half rather than
leaving a tenant to wonder why setting up an upgrade sent nobody anything.

Make it **status-aware, not a bare button**. A link that says the same thing whether or not an
automation exists teaches nothing; one that reports the current state answers the question the
tenant actually has, which is "is anyone being told about this?"

| State of the matching automation | What the panel says |
|---|---|
| None exists | "No one is being told about this upgrade. **Set up an offer email →**" |
| Exists, `is_active = false` | "An offer email is drafted but not turned on. **Review it →**" |
| Exists and active | "Offer email active: sent 30 days after purchase. 340 sent, 22 upgrades. **Edit →**" |

The active state doubles as the conversion report from §7, which is the number that justifies
the ongoing email spend and is more likely to be seen here, next to the price, than on a
reporting page nobody opens.

**Link back the other way too.** The automation editor names the upgrade path it markets, with a
link to this page. Someone who lands in Automations first should not have to guess what
"Season pass purchased → 3-Pack" is selling.

Matching is on the trigger's `fromProductId` against the path's `from_product_id`. Where several
automations match, show the count and link to the filtered list rather than picking one
arbitrarily.

**Sequencing**: the panel ships with Phase 2, since there are no automations to report on in
Phase 1. Until then the page carries a one-line note that automated offer emails are coming, so
the absence reads as "not yet" instead of "broken".

## 4. The drip campaign builder

New nav entry **Automations** beside Campaigns, under `CampaignsManage`. Separate from
Campaigns rather than a tab inside it: a broadcast and an automation have different lifecycles
(one is sent and done, one runs forever), and merging them is what makes `sent_at` meaningless.

### 4.1 The editor

A form, top to bottom, mirroring the trigger → wait → send spine everyone else uses:

1. **Name** (internal).
2. **Trigger**: a select. Phase 1 ships one option, *Season pass purchased*, with the
   `from_product` filter (any product, or a specific one). The select exists on day one even
   with a single option so the second trigger is an addition rather than a redesign.
3. **Steps**: an ordered list, each with a **wait** ("30 days after the trigger") and an
   **email** (subject + `RichTextEditor` body, reusing the Campaigns compose UI wholesale).
   Add/remove/reorder. One step is the common case; the list costs almost nothing over a single
   step and covers the documented 3-5 email norm.
4. **Stop sending when** (exit conditions), as checkboxes:
   - they upgrade *(default on)*
   - their pass is used up or expires *(default on)*
   - they unsubscribe *(always on, not editable)*
5. **Send window**: "only send between 9am and 6pm, tenant local" *(default on)*. Evaluated at
   the sweep; a step that comes due at 3am waits for the window rather than being skipped.
6. **Activate**, with the impact estimate in §4.4.

### 4.2 Exit conditions are the correctness feature

The research is unanimous that goals/exit conditions are core, and here they are the difference
between a useful feature and an embarrassing one. A rider who upgraded on day 12 must not get
the day-30 "upgrade available" email. Because `pass-upgrades.md` retires the old purchase to
`status = 'upgraded'`, the exit check is a status test the sweep already has to do, not new
machinery.

Evaluate exit conditions **at send time, not enrollment time**. State changes during the wait —
that is the entire point of the wait.

### 4.3 Enrollment is once per pass, and not configurable

Platforms expose re-enrollment because a contact can abandon many carts. Here the trigger is a
purchase, the subject is that specific pass, and the offer is about that pass's remaining value.
Re-enrolling on the same pass means emailing the same person about the same pass twice, which is
never wanted. Enrollment is keyed on the purchase and enforced by the unique index on the send
log in `pass-upgrades.md` §7.

A rider who buys a *second* pass enrolls again, correctly, because it is a different purchase.

### 4.4 Activation shows the bill first

Automations bill per email through `EmailPricing`, continuously, scaling with pass sales. Before
the activate confirm:

- how many holders match **right now** (the backlog that goes out on the first sweep)
- the rolling 30-day pass-sale rate as a **forecast** of ongoing volume
- both in emails and in estimated cost

`is_active` defaults to false. The backlog point is the sharp edge: activating a "30 days after
purchase" automation at a track with two seasons of history immediately matches every holder who
ever bought and is still eligible. Offer a **"only enrol passes purchased from today"** toggle,
default on, so activation does not blast the back catalogue.

### 4.5 Testing before arming

Every platform has this and it is cheap: **send a test to myself**, rendering the real template
with a real eligible pass's merge values. Nothing about a drip is verifiable by reading the
editor, and the first real send is a bad time to discover a broken merge field.

## 5. Schema

`pass-upgrades.md` §7 defined a single-purpose `season_pass_upgrade_campaign`. Generalise it to
carry the builder, since the shape is the same and a second single-purpose table per trigger
would not scale:

```sql
CREATE TABLE IF NOT EXISTS marketing_automation (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name               text NOT NULL,
    -- Phase 1 ships 'season_pass_purchased'. The column exists now so a second trigger is an
    -- INSERT rather than a schema change.
    trigger_kind       text NOT NULL CHECK (trigger_kind IN ('season_pass_purchased')),
    -- Trigger-specific configuration (e.g. { "fromProductId": "..." }). jsonb because each
    -- trigger needs different fields and a column per trigger would be mostly nulls.
    trigger_config     jsonb NOT NULL DEFAULT '{}'::jsonb,
    -- Exit conditions, all evaluated at SEND time.
    stop_on_upgrade    boolean NOT NULL DEFAULT true,
    stop_when_used_up  boolean NOT NULL DEFAULT true,
    -- Tenant-local send window; NULL/NULL = any hour.
    send_window_start  time NULL,
    send_window_end    time NULL,
    -- False at rest so authoring and arming are separate acts, and so saving never sends.
    is_active          boolean NOT NULL DEFAULT false,
    -- Set when armed. Enrolment ignores anything purchased earlier, which is what stops
    -- activation blasting the entire back catalogue.
    enrol_from_utc     timestamptz NULL,
    created_by_user_id uuid NULL REFERENCES users(id),
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS marketing_automation_step (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    automation_id  uuid NOT NULL REFERENCES marketing_automation(id) ON DELETE CASCADE,
    step_order     int  NOT NULL,
    -- Days after the TRIGGER, not after the previous step. Absolute offsets mean reordering or
    -- deleting a step cannot silently shift every later step's timing.
    delay_days     int  NOT NULL CHECK (delay_days >= 0),
    subject        text NOT NULL,
    body_html      text NOT NULL,
    body_text      text NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_automation_step_order
    ON marketing_automation_step (automation_id, step_order);

-- One send per (step, subject-of-the-trigger), ever. This IS the dedupe and the enrolment
-- record; a sweep that dies halfway through re-sends nothing on the next tick.
CREATE TABLE IF NOT EXISTS marketing_automation_send (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    automation_id  uuid NOT NULL REFERENCES marketing_automation(id) ON DELETE CASCADE,
    step_id        uuid NOT NULL REFERENCES marketing_automation_step(id) ON DELETE CASCADE,
    -- What the automation is about. For 'season_pass_purchased' this is the purchase id.
    subject_kind   text NOT NULL,
    subject_id     uuid NOT NULL,
    email          text NOT NULL,
    status         text NOT NULL CHECK (status IN ('sent','failed','skipped')),
    skip_reason    text NULL,
    sent_at        timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_automation_send_once
    ON marketing_automation_send (step_id, subject_kind, subject_id);

-- The sweep's read: everything this automation has already handled.
CREATE INDEX IF NOT EXISTS ix_automation_send_lookup
    ON marketing_automation_send (automation_id, subject_kind, subject_id);
```

**`delay_days` is measured from the trigger, not the previous step.** Relative offsets are the
more natural way to author ("then wait 3 more days") and the worse way to store: deleting step 2
silently moves step 3 earlier. Store absolute, and let the editor show relative if that reads
better.

## 6. The sweep

One tenant-spanning periodic job (`marketing_automation_sweep`), daily or hourly, following the
payout-drafter precedent. Per active automation, per step, find subjects where:

1. the trigger matches (`status = 'paid'` pass of the configured product)
2. `purchase.created_at <= now() - step.delay_days`
3. `purchase.created_at >= automation.enrol_from_utc` when set
4. no `marketing_automation_send` row for (step, subject)
5. exit conditions still unmet (not upgraded, not used up)
6. not suppressed for marketing
7. inside the send window in the tenant's timezone

Then send, and **write the send row for every outcome** including failures and skips, with the
reason. A failure that writes nothing is retried every tick forever.

Steps evaluate independently: a rider who becomes ineligible after step 1 simply never matches
step 2, with no "flow state" to track. That is the main simplification a linear model buys, and
it is why the graph is not needed yet.

## 7. Reporting

Per automation: sent, failed, skipped (by reason), and — the number the tenant actually wants —
**conversions**, from send rows joined to `upgraded_from_purchase_id`. "This automation sent 340
emails and produced 22 upgrades" is what justifies the ongoing email spend.

## 8. Decided

### 8.1 The offer email lives in Automations, with a cross-link

Decided 2026-07-25. The email is authored in Automations, not on the pass product, because the
second trigger will not be about passes and a per-feature email editor does not scale. The
discoverability cost that creates is paid by the status-aware panel in §3.1, which reports
whether an offer email exists and how it is performing without leaving the upgrades page.

## 9. Resolved during the build

1. **Merge fields.** `{{token}}`, substituted by `AutomationMergeFields` (Services/Email).
   Deliberately not a template engine: no expressions, no conditionals. Nine tokens ship
   (`first_name`, `holder_name`, `pass_name`, `expires_on`, `credits_remaining`, `upgrade_name`,
   `upgrade_price`, `upgrade_link`, `track_name`). Two decisions worth keeping:
   - **An unknown token renders EMPTY, not literal.** A typo produces an awkward sentence rather
     than shipping `{{frist_name}}` to a paying customer.
   - **No upgrade configured renders empty, not `$0.00`.** `$0.00` reads as a free upgrade.
   The editor affordance is a reference panel beside the body, not an insert-into-the-editor
   button: `RichTextEditor` has no merge-field concept and giving it one is not worth a token list
   a tenant can read and type.
2. **Many automations per trigger, allowed.** Two active automations on the same product both
   fire, and §3.1's panel names the one or counts the several. A validation rule would block the
   legitimate case (a 30-day nudge plus a 60-day last-chance) to prevent a mistake a tenant can
   see and fix.
3. **Hourly sweep, window evaluated in the tenant's local time.** `SendWindow.IsOpen` resolves
   each tenant's zone per automation. Overnight windows (22:00 to 06:00) wrap midnight, which a
   single `start <= now < end` range gets silently and permanently wrong; that case is unit-tested.

Also decided under the code rather than in the doc:

- **Editing an armed automation is refused.** Steps are replaced as a set and their send rows
  cascade, so an in-place edit would re-send to everyone who already got it. The editor asks the
  tenant to turn it off first.
- **The claim is written before the send, optimistically as `sent`, and corrected to `failed`
  after.** A crash between claim and send loses one email; the alternative loses the dedupe and
  sends twice. For marketing mail that is the right way round.
- **Billing is keyed per (automation, tick hour)**, not per automation. `uk_ledger_email_charge`
  is unique on (tenant, source_kind, source_id), so the automation id alone would have allowed
  exactly one charge for its entire lifetime.

## 10. Phasing

- **Phase 1** (built): upgrades admin page (§3), upgrade paths, eligibility, rider checkout.
- **Phase 2** (built): automation schema (`Script0256`), multi-step editor, hourly sweep, send
  log, activation estimate, test send, and the §3.1 cross-link panel in both directions.
- **Phase 3**: per-step reporting breakdown, skip reasons surfaced in the UI, reorder-by-drag.
- **Later, only if asked**: a second trigger, and only then revisit whether the linear model
  needs to become a graph.

Sources consulted for §1:
[Klaviyo Flows](https://www.klaviyo.com/features/flows),
[ActiveCampaign email flows](https://www.activecampaign.com/blog/email-flows),
[drip campaign software comparison](https://www.sender.net/blog/email-drip-campaign-software/),
[Klaviyo automation best practices](https://www.referralcandy.com/blog/klaviyo-automation-best-practices-your-complete-guide-to-maximizing-email-marketing-roi).
