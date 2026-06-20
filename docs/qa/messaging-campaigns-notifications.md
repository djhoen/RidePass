# QA Test Plan: Messaging (Newsletter/Campaigns, SMS, Notifications, Suppression/Unsubscribe)

> Scope: newsletter subscriber lists + import, email campaign compose/send (chunking, suppression, scheduling), per-tenant Twilio SMS (provisioning, opt-out/STOP, toll-free verification gating, per-segment pricing), in-app notifications + email preferences, email suppression (bounce/complaint), and one-click unsubscribe token flow. Last updated: 2026-06-20.

## Surface map
- **Newsletter (public/rider):** `NewsletterController.Subscribe` (anon, tenant subdomain), `Unsubscribe/{token}/Status`, `Unsubscribe/{token}`, `Resubscribe/{token}` (token is the secret), `Me/Status`, `Me/Subscribe`, `Me/Unsubscribe` (authed rider).
- **Newsletter (admin):** `NewsletterController` `Admin/Subscribers` (list/add/import/delete), `Admin/ActiveCount` (all `CampaignsManage`).
- **Campaigns:** `CampaignController` (CRUD on drafts only), `{id}/Send` (materialize + enqueue), `{id}/Unschedule`. Delivery in `Services/Scheduling/Handlers/SendCampaignHandler.cs` (kind `send_campaign`). Email pricing in `EmailPricing`.
- **SMS send:** `Services/Helpers/SmsSender.cs` (`TwilioSmsSender`, tenant-first credential resolution, MG SID vs From, opt-out short-circuit, `tenant_message` persist). Segment math in `SmsSegmentCounter.cs`; pricing in `SmsPricing.cs` / `ISmsPricing`; keywords in `SmsKeywords.cs`.
- **SMS settings/provisioning:** `SmsSettingsController` (`Status`, `Search`, `Provision`, `Enable`, `Disable`, `Release`, all `SettingsManage`), `Services/Sms/TwilioSubaccountProvisioner.cs`, `TollfreeVerificationController` + `Services/Sms/TwilioTollfreeVerifier.cs`.
- **SMS webhooks:** `TwilioWebhookController` `StatusCallback` (delivery + billing) and `IncomingSms` (inbound + STOP/START/HELP keyword handling); `Services/Sms/TwilioSignatureValidator.cs`.
- **Notifications:** `NotificationController` (inbox list, unread count, mark-read, catalog, preferences), `Services/Notifications/NotificationService.cs` (`EmitToSuperAdmins` / `EmitToTenantAdmins` / `EmitToUser`), `NotificationKinds`.
- **Email suppression / unsubscribe:** `SuppressionController` (tenant-admin view, `CampaignsManage`), `UnsubscribeController` (`OneClick`, `AllTracks`, `Status`, anon, token-keyed), `SesWebhookController` + `Services/Email/SesNotificationService.cs`, `EmailSuppressionRepository`, `ISmtpEmailer`.
- **Migrations:** `Script0014_Newsletter.sql`, `Script0019_Notifications.sql`, `Script0020_NotificationPreferences.sql`, `Script0083_TenantSmsConfig.sql`, `Script0087_TenantSmsOptOut.sql`, `Script0089_TenantMessagingServiceSid.sql`, `Script0090_TenantTollfreeVerification.sql`, `Script0102_EmailSuppression.sql`.

## Concepts under test
- **Subscriber list:** one `newsletter_subscriber` row per `(tenant_id, email)`. Soft unsubscribe flips `unsubscribed_at` (re-subscribe never loses history). `unsubscribe_token` (uuid) is the per-row secret in outbound links. `source` is one of signup/account/import/admin.
- **Import guardrails:** import requires `ConsentConfirmed=true` (CAN-SPAM/SES attestation), skips anyone on the marketing blocklist, and `InsertFromImport` only adds brand-new rows so an import can never resurrect a prior opt-out.
- **Campaign lifecycle:** draft -> (scheduled | sending) -> sent | failed. Only `draft` is editable/deletable; sent/sending cannot be deleted. `Send` is gated on `ISmtpEmailer.IsConfigured`, filters the audience against the suppression blocklist at enqueue, snapshots recipients as `email_campaign_send` rows (`pending`), then enqueues a background `send_campaign` task. `SendCampaignHandler` re-checks the blocklist at delivery, only sends `pending` rows (retry-safe), and appends both a `List-Unsubscribe` header (RFC 8058) and a visible footer link.
- **Email suppression model:** `email_suppression` scope `all` (hard bounce, blocks everything) vs `marketing` (complaint/unsubscribe, transactional still flows); `tenant_id NULL` = platform-wide, set = one tenant. Dedupe via unique index on `(COALESCE(tenant_id, sentinel), lower(email), scope)`. Membership is case-insensitive (`lower(email)`). Tenant admin view (`ListForTenant`) deliberately hides platform-wide rows to avoid cross-tenant address leakage.
- **SMS credential resolution:** `TwilioSmsSender.ResolveCredentials` prefers per-tenant subaccount creds only when `sms_enabled` AND subaccount SID + encrypted token + from-number are all present and the token decrypts; otherwise falls back to the global `Sms:Twilio:*` config (or no-op if neither). When `twilio_messaging_service_sid` is set it sends via `MessagingServiceSid`, else via raw `From`.
- **SMS opt-out:** carrier filters STOP on toll-free, but `tenant_sms_opt_out` is our own list (one row per `(tenant, phone)` in E.164) so outbound can short-circuit before Twilio. STOP/STOPALL/UNSUBSCRIBE/CANCEL/END/QUIT opt out; START/UNSTOP/YES opt back in; HELP is a no-op (carrier auto-replies). Keyword match is whole-body, trimmed, case-insensitive ("please STOP texting" is NOT a STOP).
- **SMS segments + pricing:** `SmsSegmentCounter` counts GSM-7 (160 single / 153 per concat part, extended chars cost 2 septets) vs UCS-2 (70 / 67), the moment any non-GSM char appears. `SmsPricing` bills `segments * recipients * OutboundPerSegmentCents` (default 2c). Billing is reconciled against Twilio's `Price` on the delivered `StatusCallback`, idempotent on `MessageSid`.
- **Toll-free verification gating:** unverified toll-free numbers hit a ~10 msg/day carrier cap; `tenant_tollfree_verification` tracks the TFV submission lifecycle (NULL draft -> PENDING_REVIEW/IN_REVIEW -> TWILIO_APPROVED/REJECTED -> CARRIER_APPROVED/REJECTED).
- **Notifications:** `notification` is one row per recipient user (broadcasts fan out at emit time, independent read state). Email delivery is gated on `ISmtpEmailer.IsConfigured` AND per-user `notification_preference.email_enabled` (absent row defaults to enabled). Today only super admins receive notification emails; `NotificationKinds.ForRole` returns an empty catalog for non-super-admins. In-app inbox is strictly user-scoped.

## Preconditions / test data
- Two tenants on distinct subdomains (Tenant A = acme, Tenant B = other) so isolation can be checked both directions.
- A tenant admin with `CampaignsManage` and one with only `SettingsManage`; a plain rider account; a platform super admin.
- SMTP/SES configurable two ways for the gating cases: one run with `ISmtpEmailer.IsConfigured=false`, one with it true (or a capture/mailtrap inbox).
- For SMS: Twilio master creds configured (`IsMasterConfigured=true`) for provisioning, OR a tenant pre-seeded with `twilio_subaccount_sid` + encrypted token + `twilio_from_number` + `sms_enabled=true`. A test phone you control for STOP/START. `Sms:Twilio:StatusCallbackUrl` and `InboundSmsWebhookUrl` set so webhook signatures validate.
- Seed Tenant A's subscriber list with a few active subscribers, one previously-unsubscribed row, and one address already on the suppression list (marketing scope).
- A draft campaign with known subject + body for send tests; a body containing an emoji and a 161-char GSM body for segment math.

---

## Newsletter / Campaigns

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MSG1 [NN] | Public subscribe on tenant subdomain | POST `Newsletter/Subscribe` on Tenant A with a new email + no name | 200 `{subscribed:true}`; row upserted for Tenant A; if email matches a user, name is borrowed. |
| MSG2 [NN] | Subscribe requires a tenant | Call `Subscribe` on the apex / unresolved host | 400 "Subscribing must happen on a tenant subdomain." |
| MSG3 [R] | Authed rider self-subscribe / status / unsubscribe | `Me/Subscribe`, `Me/Status`, `Me/Unsubscribe` as a rider | Status reflects each transition; `source='account'`; unsubscribe flips `unsubscribed_at`, status shows `subscribed:false`. |
| MSG4 [NN] | Admin add + active count | `Admin/Subscribers` POST a new address, then `Admin/ActiveCount` | Added with `source='admin'`; active count increments (excludes unsubscribed). |
| MSG5 [NN] | Import without consent | `Admin/Subscribers/Import` with `ConsentConfirmed=false` | 400 demanding opt-in confirmation; nothing inserted. |
| MSG6 [NN] | Import respects suppression + no opt-out resurrection | Import lines including (a) the suppressed address, (b) a previously-unsubscribed subscriber, (c) two new, (d) a malformed no-`@` line | Response counts: suppressed bucket for (a), skipped for (b malformed reuse) and (d), added for (c). Previously-unsubscribed row stays `unsubscribed_at` set (InsertFromImport adds new only). |
| MSG7 [NN] | Draft campaign CRUD | Create, edit subject/body, then attempt edit after status leaves draft | Create/edit succeed on draft; edit of a non-draft returns "Only draft campaigns can be edited." |
| MSG8 [NN] | Send gated on email config | With `ISmtpEmailer.IsConfigured=false`, POST `{id}/Send` | 400 "Email isn't configured yet..."; campaign stays draft; no send rows; no task enqueued. |
| MSG9 [NN] | Send now, suppression filtered | With email configured, send a draft whose audience includes the suppressed address | Status -> `sending`; `email_campaign_send` rows created for active minus suppressed; response `RecipientCount` excludes suppressed and notes "(N suppressed skipped)"; `send_campaign` task enqueued for now. |
| MSG10 [NN] | Background delivery is retry-safe | Let `SendCampaignHandler` run; re-run the same task | Only `pending` rows are sent; re-run re-sends nothing; campaign `MarkSent` reflects total `sent` across runs; no double email. |
| MSG11 [NN] | Suppression honored between enqueue and delivery | After Send (status sending) but before the handler runs, add a recipient to suppression; run handler | That row is marked `suppressed`, not sent; summary shows the suppressed count. |
| MSG12 [NN] | List-Unsubscribe header + footer present | Inspect a delivered campaign email | `List-Unsubscribe` header points to `/api/Unsubscribe?token=...`, `List-Unsubscribe-Post: List-Unsubscribe=One-Click`; body has the visible Unsubscribe footer link with the tenant name. |
| MSG13 [NN] | Schedule + unschedule | Send with `scheduledForUtc` ~10 min out; then `{id}/Unschedule` | Send returns status `scheduled` (60s skew grace); Unschedule cancels the pending task, drops send rows, reverts to draft. Unschedule on a non-scheduled campaign returns 400. |
| MSG14 [NN] | Send with empty audience | Send when no active subscribers, or every subscriber suppressed | 400 "No active subscribers..." or "Every subscriber is on the suppression list...". |
| MSG15 [R] | Delete rules | Delete a draft vs a sent/sending campaign | Draft deletes; sent/sending returns "Cannot delete a campaign that has been sent." |
| MSG16 [R] | Email billing on delivery | After a successful send, check tenant ledger | One `email_charge` ledger entry (negative, marginal monthly tier via `EmailPricing`); a retry does not double-charge (unique index on tenant+source). |

## SMS

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MSG17 [NN] | Provision requires master config + a number | `SmsSettings/Provision` with a blank `phoneNumber`, then a valid toll-free from `Search` | Blank -> 400 "phoneNumber is required."; valid -> subaccount provisioned, `twilio_from_number` set, audit `sms.provision` logged. |
| MSG18 [NN] | Enable gated on a provisioned number | `SmsSettings/Enable` before provisioning, then after | Before: 400 "SMS isn't provisioned yet..."; after: `sms_enabled=true`, audit `sms.enable`. |
| MSG19 [NN] | Send uses tenant subaccount + MG SID | With tenant provisioned + `sms_enabled` + `twilio_messaging_service_sid` set, send via `TwilioSmsSender.Send(tenant,...)` | Request to Twilio uses `MessagingServiceSid` (not `From`); outbound `tenant_message` row appended to the conversation. |
| MSG20 [NN] | Pre-MG tenant sends via From | Tenant with subaccount but `twilio_messaging_service_sid` NULL | Send uses `From=twilio_from_number`; still records the message. |
| MSG21 [NN] | Disabled tenant falls back / no-ops | Tenant with `sms_enabled=false`; send via tenant overload | Per-tenant creds skipped; falls back to global config if set, else returns false (silent no-op), no `tenant_message` row. |
| MSG22 [NN] | Decrypt failure falls back to global | Tenant with a token that fails to decrypt (rotated key); send | Warning logged, falls back to global config (or no-op); tenant not "dead in the water". |
| MSG23 [NN] | Outbound opt-out short-circuit | Add a `tenant_sms_opt_out` row (opted_out) for the destination, then send | Send returns false before calling Twilio; info log "Suppressing SMS... opted out"; no Twilio request, no `tenant_message` row. |
| MSG24 [NN] | Inbound STOP records opt-out | POST `TwilioWebhook/IncomingSms` (valid signature) body `STOP` from the test phone | `tenant_sms_opt_out` row opted_out=true, `last_keyword='STOP'`, `opted_out_at_utc` set; inbound message appended; 200 empty. |
| MSG25 [NN] | START re-opts in | Inbound `START` (or UNSTOP/YES) after a STOP | `opted_out=false`, `opted_in_at_utc` set, `last_keyword` updated; subsequent send to that phone is no longer suppressed. |
| MSG26 [NN] | Keyword is whole-body only | Inbound "please STOP texting me" | Classified `None`; no opt-out recorded (matches carrier behavior). |
| MSG27 [NN] | HELP is a no-op | Inbound `HELP` | No opt-state change; message recorded; 200 (carrier sends the stock HELP reply). |
| MSG28 [NN] | Inbound webhook signature enforced | POST `IncomingSms` with a bad / missing `X-Twilio-Signature`, and with `InboundSmsWebhookUrl` unset | Bad signature -> 401; unset config -> 401 (fail closed); unknown subaccount -> 200 (no retry) but no state change. |
| MSG29 [NN] | Inbound dedupe | Replay the same inbound `MessageSid` | Unique index on `twilio_message_sid` rejects the dup; handler treats as success (200), no second row. |
| MSG30 [NN] | StatusCallback bills only on delivered | POST `StatusCallback` (valid signature) with status `delivered` + `Price` + `NumSegments` | One `tenant_billing_event` (kind `sms`) with `billed_cents = NumSegments * OutboundPerSegmentCents`; `tenant_message` status updated. |
| MSG31 [NN] | StatusCallback idempotent + failure no-charge | Replay the same delivered callback; separately send `failed`/`undelivered` | Duplicate -> `RecordIfNew` no-op (no second bill); failed/undelivered -> status updated, no charge. |
| MSG32 [NN] | StatusCallback signature / config gating | Send a callback with bad signature, and with `StatusCallbackUrl` unset | Bad signature -> 401; unset URL -> 401 (fail closed, no billing); unknown subaccount -> 200 no-op. |
| MSG33 [NN] | Segment counting | Count a 160-char GSM body, a 161-char GSM body, a body with one emoji at 71 chars | 160 -> 1 seg GSM-7; 161 -> 2 segs; emoji forces UCS-2, 71 units -> 2 segs (70/67 boundary). Extended GSM char (e.g. `€`) costs 2 septets. |
| MSG34 [NN] | Estimate matches recipients * segments | `ISmsPricing.EstimateOutboundCents(body, recipientCount)` | Returns `segments * recipients * perSegmentCents`; 0 when recipientCount <= 0. Compose UI total agrees. |
| MSG35 [R] | Toll-free verification lifecycle | Open the TFV form, save a draft (status NULL), submit, simulate a status change | Draft has no `twilio_verification_sid`; submit sets the SID + `last_submitted_at_utc`; rejected can be edited + resubmitted; status reflects Twilio lifecycle. |
| MSG36 [R] | Release provisioning | `SmsSettings/Release` on a provisioned tenant, then on an unprovisioned one | Provisioned: subaccount/number/MG released, audit `sms.release`; unprovisioned: 400 "No SMS provisioning to release." |
| MSG37 [R] | E.164 normalization | Send to "5551234567", "+1 555 123 4567", and a 7-digit string | 10-digit -> `+1...`; already-`+` preserved (digits only); too-short -> null -> reject; opt-out lookup uses the same normalized form so it matches. |

## Notifications

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MSG38 [NN] | Inbox is user-scoped | As user X, GET `Notification` and `UnreadCount`; emit a notification to user Y | X sees only their own rows; Y's notification never appears for X. |
| MSG39 [R] | Mark read / read-all | `{id}/Read` then `ReadAll` | Single row flips `is_read`+`read_at`; ReadAll clears unread for the caller only; UnreadCount updates. |
| MSG40 [NN] | Catalog filtered by role | GET `Notification/Catalog` as super admin vs tenant admin vs rider | Super admin gets the configurable kinds; non-super-admins get an empty list (`NotificationKinds.ForRole`). |
| MSG41 [NN] | Preference honored for email | As super admin set a kind's `EmailEnabled=false`, then trigger `EmitToSuperAdmins` for that kind | In-app row still inserted; no email sent for the disabled kind; an enabled kind does send (when SMTP configured). |
| MSG42 [NN] | Email gated on SMTP config | With `ISmtpEmailer.IsConfigured=false`, emit a super-admin notification | In-app row inserted; no email attempt (fire-and-forget skipped). |
| MSG43 [NN] | Default-on preference | Emit a kind for a user with no `notification_preference` row | Email sent (absent row defaults `email_enabled=true`) when SMTP configured. |
| MSG44 [R] | Tenant-admin + direct-user emits are in-app only | `EmitToTenantAdmins` and `EmitToUser` | Rows inserted for the right recipients; `EmitToUser` sends no email; tenant-admin fan-out is per-recipient with independent read state. |
| MSG45 [R] | Emit resilience | Force an insert failure for one recipient in a super-admin fan-out | Warning logged; remaining recipients still get their notification (loop continues). |

## Suppression & Unsubscribe

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MSG46 [NN] | One-click unsubscribe (RFC 8058) | POST `Unsubscribe?token=<valid>` (the campaign header token) | 200 `{unsubscribed:true}`; `email_suppression` row reason `unsubscribe`, scope `marketing`, source `one_click`, scoped to that tenant. Transactional mail still allowed. |
| MSG47 [NN] | Unsubscribe all tracks | POST `Unsubscribe/AllTracks?token=<valid>` | Platform-wide (`tenant_id NULL`) marketing suppression; blocks marketing for that address across every tenant, receipts untouched. |
| MSG48 [NN] | Invalid / tampered token | Call `OneClick`, `AllTracks`, `Status` with a garbage token | 400 "Unsubscribe link is invalid."; no suppression written. |
| MSG49 [NN] | Unsubscribe status page data | GET `Unsubscribe/Status?token=<valid>` before and after unsubscribing | Returns email + tenant display name + `unsubscribed` flag reflecting current marketing-suppression state. |
| MSG50 [NN] | SES hard bounce -> global all-scope | Send a permanent-bounce SNS notification to `SesWebhook` (webhook enabled, valid signature) | `email_suppression` row reason `bounce`, scope `all`, `tenant_id NULL` (blocks everything everywhere); transient bounce is ignored. |
| MSG51 [NN] | SES complaint -> marketing scope | Send a complaint notification with a `tenant_id` message tag | Row reason `complaint`, scope `marketing`, scoped to the tagged tenant (platform-wide marketing if tag absent); receipts still flow. |
| MSG52 [NN] | SES webhook gating + signature | Hit `SesWebhook` with `WebhookEnabled=false`, then enabled with a bad SNS signature | Disabled -> 404 (ships dark); bad signature -> 403; malformed body -> 400. |
| MSG53 [NN] | Admin manual suppress + remove | `Suppression` POST a marketing suppression, list it, DELETE it | Add writes reason `manual`/scope `marketing`/source `admin`; appears in tenant list; remove deletes only that tenant's row. Invalid email -> 400. |
| MSG54 [R] | Newsletter token unsubscribe vs suppression | `Newsletter/Unsubscribe/{token}` and `Resubscribe/{token}` | Newsletter token flips `unsubscribed_at` on the subscriber row (list membership), distinct from the `email_suppression` blocklist used by the campaign send path. Both must be respected. |
| MSG55 [NN] | Case-insensitive suppression match | Suppress `Foo@bar.com`; attempt a send to `foo@BAR.com` | Blocklist match is case-insensitive (`lower(email)` + OrdinalIgnoreCase set); the differently-cased address is dropped. |

## Edge

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MSG56 [NN] | Subscriber list tenant isolation | Subscribe the same email on Tenant A and Tenant B; admin-list each | Two independent rows (unique per tenant+email); each admin sees only their tenant's subscribers and counts. |
| MSG57 [NN] | Campaign cross-tenant access | As Tenant A admin, GET/PUT/DELETE/Send a Tenant B campaign id | 404 "Campaign not found." for every verb (all reads scoped by tenant). |
| MSG58 [NN] | Suppression list does not leak cross-tenant | Trigger a platform-wide hard bounce, then GET `Suppression` as a tenant admin | Platform-wide (`tenant_id NULL`) rows are NOT listed (avoids exposing another tenant's address); still enforced at send time. |
| MSG59 [NN] | SMS opt-out isolation | STOP from the same phone for Tenant A only | Tenant A suppresses that phone; Tenant B can still send to it (opt-out keyed by tenant+phone). |
| MSG60 [NN] | Unsubscribe token cross-tenant scope | Use a token minted for Tenant A against the same address subscribed under Tenant B | One-click suppresses only the tenant in the token; the other tenant's marketing is unaffected (use AllTracks for platform-wide). |
| MSG61 [R] | Concurrent double-send of a campaign | Fire `{id}/Send` twice quickly on a draft | Second call sees status != draft and returns 400 (no second batch of send rows); confirm no duplicate `email_campaign_send` rows (unique `(campaign_id, email)`). |
| MSG62 [R] | Subscriber delete is tenant-scoped | DELETE a subscriber id belonging to Tenant B as Tenant A | No-op / not removed (delete scoped by tenant id); Tenant B row intact. |

## Known risks / watch-items
- **Multi-tenant isolation:** campaign reads/sends are tenant-scoped via `GetById(id, tenantId)`; subscriber and suppression lists scope by `tenant_id`; `tenant_sms_opt_out` keys on `(tenant_id, phone)`. The webhook controllers resolve tenant from `AccountSid`/message tag, not the subdomain, so verify a spoofed `AccountSid` cannot land a row under the wrong tenant (signature is keyed by that subaccount's token, which is the guard). Confirm `SuppressionController.ListForTenant` continues to exclude `tenant_id NULL` rows (intentional, prevents address leakage).
- **Opt-out / consent compliance:** import attestation (`ConsentConfirmed`) and the "import never resurrects an opt-out" guarantee are the CAN-SPAM/SES basis. RFC 8058 one-click header + visible footer must BOTH ship on every campaign email. STOP handling relies on our own list short-circuiting before Twilio; verify the carrier auto-reply is not duplicated by us. HELP is currently a no-op (carrier handles it). Toll-free sends above ~10/day silently cap until TFV is carrier-approved, so high-volume blasts before approval is a real failure mode to flag.
- **Send idempotency:** campaign delivery is retry-safe only because `SendCampaignHandler` sends `pending` rows only and `MarkSent` recomputes from `sent` rows; the `(campaign_id, email)` unique index prevents dup send rows. SMS billing is idempotent on `MessageSid` via `RecordIfNew`; inbound SMS dedupes on `twilio_message_sid`. Watch the `Send` -> background gap: recipients are snapshotted at enqueue, and suppression is re-checked at delivery, but a new subscriber added after enqueue will NOT receive the campaign (by design).
- **Pricing reconciliation:** `SmsSegmentCounter` is a client-side estimate; the authoritative charge is Twilio's `Price` on the delivered `StatusCallback`. `billed_cents` is computed from `NumSegments * OutboundPerSegmentCents` independent of Twilio's actual `Price` (stored as `TwilioCostMicros`), so confirm the estimate, the bill, and Twilio's cost are reconciled and margins behave when a message splits into more segments than the compose UI predicted.
- **Email config gating:** both `CampaignController.Send` and notification email fan-out short-circuit when `ISmtpEmailer.IsConfigured=false`, so during pre-SES testing campaigns refuse to send and notifications stay in-app only. Re-test the full path once SES is wired and `Email:Ses:WebhookEnabled=true`.
- **Notification email reach:** only super admins receive notification emails today (`NotificationKinds.ForRole` empty for others); tenant-admin and direct-user emits are in-app only. If that scope expands, the preference + suppression gating must be re-verified for the new audience.
