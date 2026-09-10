# Dynamic email campaigns: plan

Status: **Phases 0 to 3 built on stage 2026-09-10** (audiences; trigger registry, timing anchors, event + pass triggers, editor; newsletter trigger; per-step reporting). Not yet on production. Builds on the drip system that already exists
(`docs/drip-campaigns.md`, `Script0256_MarketingAutomations.sql`, `MarketingAutomationSweep`,
`Automations.vue`). Nothing here replaces that system; it generalizes it.

## 1. What tenants asked for

Two examples set the shape:

- "Send a series of emails to people who bought a camp." A trigger tied to an event (or a kind
  of event), several emails, some of them timed to the camp itself: what to bring three days
  before, a thank-you and photo link the day after.
- "Send a welcome email two days after someone buys a season pass." One trigger, one wait, one
  email, with the pass details merged in.

Both are **trigger, wait, send** with exit conditions. That is exactly the spine the current
automation builder already implements for one trigger (`season_pass_purchased`, wired to pass
upgrade offers). So the work is not a new feature; it is removing the "one trigger" limit and
adding event-relative timing.

## 2. What exists today, and what is reusable as-is

| Piece | State | Reuse |
|---|---|---|
| `marketing_automation` (name, `trigger_kind`, `trigger_config` jsonb, stop flags, send window, `enrol_from_utc`, active) | Built, one trigger allowed by CHECK | Keep. Widen the CHECK, add a generic exit-rules column. |
| `marketing_automation_step` (`step_order`, `delay_days`, subject, body) | Built | Keep. Add a timing anchor so a step can be relative to the event, not only the purchase. |
| `marketing_automation_send` (one row per step + subject, `subject_kind`, `subject_id`, status, skip reason) | Built, already generic | Keep unchanged. This is the enrolment and the dedupe: one email per step per purchase, ever. |
| `MarketingAutomationSweep` (hourly, per active automation, per step, "due subjects" query, send window, suppression, unsubscribe headers, billing) | Built | Keep the loop. Replace the single pass-only "due subjects" query with a per-trigger source. |
| `AutomationMergeFields` (`{{first_name}}`, `{{pass_name}}`, ...) | Built, pass-shaped | Keep the substitution; add per-trigger field sets. |
| `AutomationController` (CRUD, Estimate, Activate with bill preview, TestSend, MergeFields) | Built | Keep. Estimate and TestSend become trigger-aware. |
| `Automations.vue` editor (trigger select disabled at one option, steps with day delays, stop checkboxes, test send dialog) | Built | Enable the trigger select, add per-trigger config, add timing anchors. |
| Outbound gate, allowlist, suppression, `List-Unsubscribe`, per-email billing, campaign permission | Built | No change. Every automated email already passes through them. |

The important consequence: the "once per purchase" enrolment rule and the exit-condition
discipline that make the current drips safe carry over to every new trigger for free.

## 3. Design

### 3.0 One-shot campaigns to a purchase audience (the other half of the ask)

"Send a campaign to anyone who bought event X" is not a drip; it is the existing broadcast
campaign with a different audience. Today `email_campaign` can only address the newsletter list.
It gains an **audience** stored on the campaign:

| Audience | Recipients |
|---|---|
| Newsletter subscribers (default, today's behaviour) | active `newsletter_subscriber` rows |
| Purchasers of a specific event | distinct `purchaser_email` from paid `event_ticket_purchase` rows for that event |
| Purchasers of a specific event type | same, for every event of that type (optionally limited to a date range) |
| Holders of a specific pass product | distinct purchaser emails from paid `season_pass_purchase` rows for that product |

Rules shared with drips: suppression and marketing opt-outs are removed before the send rows
are created, every email carries `List-Unsubscribe` and the footer link, the estimate and cost
show before sending, and a recipient appearing in two sources is emailed once. Purchase
audiences are a legitimate customer relationship for marketing purposes, but the opt-out has to
work identically, so the same unsubscribe token applies and adds the address to the tenant's
marketing suppression regardless of which audience it came from.

Schema: `email_campaign.audience_kind text NOT NULL DEFAULT 'subscribers'` and
`audience_config jsonb` (event id, event type id, product id, date range). The recipient
snapshot at send time already lives in `email_campaign_send`, so reporting needs no change.
Editor: an "Audience" select above the subject with the matching picker, and the confirm
dialog names the audience and count ("Send to 143 purchasers of Spring Camp?").

The audience sources (event, event type, pass product) are the same three queries the drip
triggers use, so they are written once and shared.

### 3.1 Triggers become a registry

Each trigger is a small server-side class with five parts. The sweep, the estimate, the test
send, and the editor all read from the registry, so adding a trigger later is one class plus
one editor sub-form.

| Part | What it answers |
|---|---|
| `Kind` | The stable id stored in `trigger_kind`. |
| `ConfigSchema` | What the tenant picks (an event, an event type, a product, or "any"). Stored in `trigger_config`. |
| `DueSubjects(automation, step, batch)` | The rows that qualify for this step right now: purchase-shaped, with an email, a user id, the anchor dates, and the merge values. Excludes rows already sent this step (via `marketing_automation_send`) and rows failing the exit rules. |
| `ExitRules` | The stop conditions this trigger supports, each with a label for the editor. |
| `MergeFields` | The tokens this trigger can fill, with sample values for test sends. |

Triggers in the first release, in priority order:

1. **Event ticket purchased** (`event_ticket_purchased`). Config: `scope` = one specific event
   or one event type (decided: no "any event" option; a tenant who wants every event picks the
   type). Subject = `event_ticket_purchase` with `status = 'paid'`. Anchor
   dates: purchase time, event start, event end. Merge: `event_name`, `event_date`,
   `event_start_time`, `event_location`, `ticket_tier`, `quantity`, `tickets_link`. Exit rules:
   purchase refunded or cancelled, event cancelled, rider already attended (for pre-event
   steps only). This covers the camp series.
2. **Season pass purchased** (`season_pass_purchased`). Already exists for upgrade offers;
   config stays scoped to one specific pass product (decided, mirroring events), and the
   upgrade-specific exit rules become optional so a plain welcome series is possible. Merge fields unchanged. This covers the welcome email.
3. **Newsletter subscribed** (`newsletter_subscribed`). Subject = `newsletter_subscriber`;
   anchor: subscribed_at. A welcome series for the list. Cheapest to add and the most
   conventional.

Deliberately not in the first release: memberships (decided: wait for a tenant that sells
them; the registry makes it one class when the time comes), gift cards (recipient consent is unclear), abandoned
checkout (no abandoned-cart state exists to trigger on), rentals and lessons (bike shop data is
per-tenant-feature and worth its own pass), birthday (no date of birth collected).

### 3.2 Step timing gets an anchor

Today `delay_days` counts from the trigger. Camps need "3 days **before** the event". Each step
gains:

- `anchor`: what the wait is measured from. Offered per trigger:
  - `purchase` (default, today's `delay_days` behaviour): "2 days after they bought the pass",
    "1 day after they bought the ticket". Available on every trigger.
  - `event_start` and `event_end` (event trigger): "7 days before the event starts", "2 days
    after it ends".
  - `pass_expiry` (pass trigger): "14 days before the pass expires".
  - `fixed_date` (every trigger): a calendar date the tenant picks for that step, for example
    "send the opening-day email to every pass holder on May 1". Each enrolled subject gets it
    once; anyone who buys after that date skips it.
- `offset_days`: signed, so "before" is a negative offset. `delay_days` stays as the stored
  column for `purchase`-anchored steps so existing automations keep working; the other anchors
  use `offset_days`, and `fixed_date` uses a `send_on` date column.

Rules that keep this predictable:

- A step whose send time is already in the past when the rider enrols is **skipped**, not sent
  late (someone buying a camp ticket the day before does not get the "two weeks out" email).
  Skip reason recorded on the send row. Decided 2026-09-10.
- Day granularity only. The hourly sweep plus the tenant's send window already decide the hour.
- Steps are sorted by their computed send time per subject, not by `step_order`, when anchors
  mix. `step_order` remains the editor's display order. A single automation can mix anchors:
  a camp series might be "1 day after purchase" (welcome), "7 days before the event" (what to
  bring), "2 days after the event" (thanks and photos).

### 3.3 Exit rules become data

`stop_on_upgrade` and `stop_when_used_up` are pass-specific booleans. They stay for
compatibility, and a new `exit_rules` jsonb (`["refunded", "event_cancelled", "attended"]`)
carries trigger-specific rules. Two rules apply to every trigger and cannot be turned off:
the recipient is suppressed or unsubscribed from marketing, and the purchase is no longer paid.

### 3.4 Enrolment stays "once per purchase, from activation forward"

Unchanged and worth restating, because it is what makes automations safe to arm: a subject is
enrolled the first time the sweep sees it after `enrol_from_utc`, receives each step at most
once, and is never re-enrolled. Buying two camp tickets is two subjects only if they are two
purchases; a single purchase with quantity 3 is one subject and one email.

Estimate before activation shows: subjects that would enrol immediately (already-paid
purchases newer than `enrol_from_utc`, which defaults to now so the answer is usually zero),
the expected volume per month based on the last 90 days of matching purchases, and the bill at
the tenant's email price.

### 3.5 Editor

`Automations.vue` becomes a real trigger picker:

1. **Trigger** select, enabled: "A rider buys a ticket to an event", "... a season pass",
   "Someone joins the newsletter".
2. **Trigger options** sub-form that changes with the trigger: for events, a choice of "this
   event" (searchable picker over upcoming and past events) or "any event of this type" (event
   type picker); for passes, the pass product picker.
3. **Steps**: each with subject, body, and timing as a sentence: `[3] days [before ▾] [event
   start ▾]`, or `on [date]` for a fixed date. Anchor options come from the trigger: purchase
   for all; event start and end for events; pass expiry for passes; a fixed date for all. Merge fields button lists this trigger's
   tokens and inserts at the cursor.
4. **Stop sending when**: the trigger's exit rules as checkboxes, plus the two fixed ones shown
   as always-on.
5. **Test send**: picks a real recent subject for this trigger (or the sample values) and sends
   one step to the admin's own address, through the normal gate.
6. **Activate**: the estimate and the bill, then arm.

The list view gains a trigger column and the per-step counts from Phase 3 of the original drip
plan (sent, skipped with reasons, failed).

### 3.6 Reporting

Per automation and per step: sent, skipped (by reason), failed, and a trigger-specific
conversion where one exists (pass upgrades today; for events, "bought another event ticket
within 60 days" is the honest first metric). No open or click tracking in this release; if it
is wanted later it comes from the SendGrid event webhook rather than pixels in our HTML.

## 4. Schema changes (one migration, additive, rerunnable)

```sql
-- Widen the trigger set.
ALTER TABLE marketing_automation DROP CONSTRAINT IF EXISTS chk_marketing_automation_trigger;
ALTER TABLE marketing_automation ADD CONSTRAINT chk_marketing_automation_trigger
    CHECK (trigger_kind IN ('season_pass_purchased', 'event_ticket_purchased',
                            'newsletter_subscribed'));
-- Generic exit rules alongside the two legacy booleans.
ALTER TABLE marketing_automation ADD COLUMN IF NOT EXISTS exit_rules jsonb NOT NULL DEFAULT '[]'::jsonb;
-- Step timing anchor; delay_days stays for trigger-anchored steps.
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS anchor text NOT NULL DEFAULT 'purchase';
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS offset_days int NOT NULL DEFAULT 0;
ALTER TABLE marketing_automation_step ADD COLUMN IF NOT EXISTS send_on date NULL;   -- fixed_date anchor
-- Skipped-before-enrolment needs a status the send table already allows ('skipped').
```

`marketing_automation_send.subject_kind` already distinguishes subjects, so event purchases and
pass purchases can share the table without a change.

## 5. Sweep changes

`MarketingAutomationSweep.RunAutomation` currently calls `ListDuePassSubjects`. It changes to
`registry.For(a.TriggerKind).DueSubjects(a, step, batch)`, and the anchor math moves into a
shared helper the trigger sources call. Everything after that point (send row, send window,
headers, merge, billing, failure marking) is untouched.

Two operational notes:

- Tick stays hourly. A step "0 days after purchase" therefore lands within the hour, which is
  the right expectation to set in the editor ("within about an hour"). A transactional receipt
  is not what automations are for; receipts already send instantly.
- Event-anchored steps make the sweep look at future events. The due query for `event_start`
  anchors is bounded to events starting within the next 60 days so it stays cheap.

## 6. Phasing and effort

| Phase | Scope | Effort |
|---|---|---|
| 0. Broadcast audiences | `audience_kind` + config on campaigns, the three purchase-audience queries, audience picker and count in the campaign editor, dedupe and suppression | 1 to 2 days |
| 1. Registry, timing engine, event and pass triggers | Trigger registry; anchors purchase / event start / event end / pass expiry / fixed date with signed offsets and skip-if-past; migration; sweep refactor; `event_ticket_purchased` (specific event or event type) and `season_pass_purchased` (specific product, upgrade exits optional); editor trigger picker and timing sentence; estimate and test send showing the computed send date | 4 to 5 days |
| 2. Newsletter trigger | `newsletter_subscribed` welcome series on the same engine | half a day |
| 3. Reporting | Per-step sent/skipped/failed in the list, skip reasons, event conversion metric | 1 day |
| Later, if asked | Memberships, "any event" scope, audience filters (first-time buyers only, tier X only, no season pass), gift card and bike shop triggers, open/click via SendGrid events, branching | not planned |

Phase 0 delivers "email everyone who bought this camp" immediately and its queries are reused by
Phase 1. Phase 1 delivers both the camp series (event-relative timing) and the pass series
(purchase-relative timing) on one engine. The welcome-after-pass example works today with the
existing trigger if the tenant sets a 2-day step and leaves the upgrade exits unchecked; Phase 2
makes that clean rather than a workaround.

## 7. Risks and guardrails

- **Volume surprise**: an "any event" trigger on a busy track can enrol hundreds of purchases a
  week. The activation estimate and the monthly bill preview exist for this; keep them
  mandatory and show the 90-day projection, not just today's count.
- **Timing bugs are silent**: a wrong anchor sends "what to bring" after the camp. The test send
  must show the computed send date for a real subject, not only the merged body.
- **Staging**: every automated send passes the super-admin outbound gate, so staging tests are
  confined to the allowlist. Keep it that way while building; do not add a bypass.
- **Compliance**: automations are marketing mail. `List-Unsubscribe`, the footer link, and
  suppression apply to every trigger, including the newsletter welcome series.

## 8. Decisions

All three were decided on 2026-09-10:

1. Event scope: a specific event or a specific event type. No "any event". Passes mirror this
   with a specific pass product.
2. Late buyers skip past-due steps; the skip and its reason are recorded on the send row.
3. Memberships wait until a tenant sells them.
