# On-mountain alerts: plan

Status: **proposed**, 2026-09-10. Companion to `docs/dynamic-campaigns-plan.md`; shares its
audience machinery but is an OPERATIONS broadcast, not marketing.

## 1. The ask

"Text everyone who is on the mountain today": an extreme-weather closure, a lift stoppage, a
lost child, a change of venue. Reach must be fast, complete, and honest about who it could not
reach. Recipients are whoever the platform knows is here today: season pass holders who checked
in, day-pass and event-ticket holders, riders with a rental or a lesson.

## 2. What the data already knows

| Source | Table | "Here today" signal | Contact |
|---|---|---|---|
| Event tickets (lift days, races, camps, clinics) | `event_ticket_purchase` via `event_ticket_tier` to `event` | event starts today, status paid or redeemed; **checked in** = `event_ticket_attendance.on_date = today` | `purchaser_email`, phone via `users.phone` (purchaser_user_id) |
| Season pass holders | `season_pass_reservation` to `event` | reservation for an event today; **checked in** = `status = 'checked_in'` | pass `purchaser_email`, phone via `users.phone` |
| Rentals | `rental_purchase` | `start_date <= today <= end_date` | `purchaser_email`, phone via `users.phone` |
| Day passes | `day_pass_purchase` (present in migrations, absent on production today) | `valid_on_date = today`, paid or redeemed | same shape |
| Lessons | booked as events (Clinic type), so covered by tickets | | |

Phone is the weak link: it lives on the user record, is typed loosely, and is normalized to E.164
at send time. Half of production's users have one. The feature must always show "reachable by
text" separately from "here today", and offer email for the rest.

## 3. Design

### 3.1 Audience: "On the mountain" as a fifth campaign audience

Reuse the audience resolver built for campaigns (`CampaignAudienceRepository`) rather than a
parallel query set. New kind `on_mountain` with config:

- `date` (tenant-local calendar day, default today)
- `scope`: `checked_in` (attendance row, checked-in reservation, redeemed ticket or day pass)
  or `expected` (everything above plus anyone holding a ticket, reservation, rental, or day pass
  for the day, whether or not they have been scanned)
- `sources`: tickets, passes, rentals, day passes, each on by default

Recipients come back deduplicated per person with BOTH email and normalized phone, and a flag
per source so the preview can say "142 here today: 98 pass holders, 31 ticket holders, 13
rentals; 120 have a phone, 22 email only".

This same audience then also works for a regular email campaign ("thanks for coming today"),
which is why it belongs in the shared resolver.

### 3.2 Sending: a dedicated alert, not a campaign row

A new `mountain_alert` record per send (tenant, date, scope, sources, body, channel policy,
sent by, counts) with `mountain_alert_recipient` rows (email, phone, source, channel used,
status, error, message sid). Separate from `email_campaign` because:

- the confirm and the report are about reach ("who did we miss"), not opens and unsubscribes;
- it is transactional/operational mail and text, so it is NOT subject to the marketing
  suppression list and carries no unsubscribe footer (opt-outs still apply to SMS, see 3.4);
- it needs to run NOW, synchronously in batches, with a live progress count, not on the hourly
  sweep or the 60-second dispatcher.

Channel policy per alert: **SMS first, email to anyone without a phone** (default), SMS only,
or email only. A rider with both gets the text only, so nobody gets the alert twice.

### 3.3 The page

`/Admin/Alerts`, a single screen built for a stressed person on a phone:

1. Date (default today) and scope toggle (checked in only / everyone expected today).
2. Live preview card: total people, breakdown by source, reachable by text vs email only,
   and the estimated SMS segments and cost (`SmsSegmentCounter`).
3. Message box with a character/segment meter. Short by default; a 2-segment alert costs
   double. Prefix is automatic: "[Highland Bike Park] " so the text is identifiable.
4. Optional email subject (defaults to the first line of the message).
5. "Send test to me" (your own phone and email).
6. Send, with a confirm that repeats the counts and cost and requires typing SEND for anything
   over 50 recipients.
7. Progress and result: sent, failed, no contact, opted out, each expandable to the names.
8. History of past alerts below, with their reports.

### 3.4 Compliance and safety rails

- **SMS opt-outs are honored.** STOP is a carrier-level instruction and ignoring it is a
  TCPA problem regardless of urgency. Riders who opted out are listed under "not reachable by
  text" and get the email instead if they have one.
- **Marketing suppression does not apply** to alerts, but hard bounces (`scope = 'all'`) do,
  since those addresses are dead.
- **Outbound delivery gate applies**, so staging cannot reach anyone outside the allowlist.
- **Toll-free verification is a prerequisite for real use.** An unverified toll-free number is
  capped by carriers at roughly 10 messages a day; a weather alert to 140 riders would stall at
  10. The Alerts page shows a red banner with the cap until the tenant's number is verified, and
  the provisioning checklist already carries the step.
- **Rate limits.** Send in batches of 25 with a short pause, so Twilio's per-second limit does
  not fail the tail of a large alert; failures retry once.
- **Permission.** New `alerts.send`, granted to admins by default and assignable to gate and
  counter staff, since the person who sees the storm is rarely the marketing manager.
- **Audit.** Every alert writes an audit row with the body and the counts.

### 3.5 What is out of scope for the first cut

- Two-way replies beyond STOP/START (they land in the existing Inbox thread already).
- Geofencing or app push. The platform has no location signal; "on the mountain" means the
  records above.
- Scheduling an alert for later. Alerts are now; a scheduled one is a campaign.

## 4. Schema (additive, rerunnable)

```sql
CREATE TABLE IF NOT EXISTS mountain_alert (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    on_date date NOT NULL,
    scope text NOT NULL CHECK (scope IN ('checked_in','expected')),
    sources jsonb NOT NULL DEFAULT '["tickets","passes","rentals","day_passes"]',
    channel_policy text NOT NULL CHECK (channel_policy IN ('sms_then_email','sms_only','email_only')),
    body text NOT NULL,
    email_subject text NULL,
    status text NOT NULL CHECK (status IN ('sending','sent','failed')),
    total_people int NOT NULL DEFAULT 0,
    sent_sms int NOT NULL DEFAULT 0, sent_email int NOT NULL DEFAULT 0,
    failed int NOT NULL DEFAULT 0, unreachable int NOT NULL DEFAULT 0, opted_out int NOT NULL DEFAULT 0,
    created_by_user_id uuid NULL REFERENCES users(id),
    created_at timestamptz NOT NULL DEFAULT now(), finished_at timestamptz NULL
);
CREATE TABLE IF NOT EXISTS mountain_alert_recipient (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_id uuid NOT NULL REFERENCES mountain_alert(id) ON DELETE CASCADE,
    tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    user_id uuid NULL, name text NULL, email text NULL, phone text NULL,
    sources text[] NOT NULL,
    channel text NULL CHECK (channel IN ('sms','email')),
    status text NOT NULL CHECK (status IN ('sent','failed','no_contact','opted_out','suppressed')),
    error text NULL, twilio_message_sid text NULL, sent_at timestamptz NULL
);
```

## 5. Phasing and effort

| Phase | Scope | Effort |
|---|---|---|
| 1 | `on_mountain` audience in the resolver (date, scope, sources), preview endpoint with breakdown and reachability, and the same audience available to email campaigns | 1 day |
| 2 | `mountain_alert` tables, batched sender with SMS-then-email policy, opt-out and bounce handling, audit, progress and per-recipient report | 1.5 days |
| 3 | The Alerts page: preview, meter, test-to-me, typed confirm, history; `alerts.send` permission | 1 day |
| 4 | Verification banner and Twilio readiness checks on the page; provisioning checklist update | half a day |

## 6. Decisions needed

1. Default scope: checked in only, or everyone expected today? Proposed: **expected**, since a
   closure matters most to the rider still driving up.
2. Should gate and counter staff get `alerts.send` by default, or admins only?
3. Alert prefix: the track's display name in brackets, or a tenant-configurable prefix?
4. Whether an alert to riders who opted out of SMS should fall back to email (proposed yes) or
   be left alone entirely.
