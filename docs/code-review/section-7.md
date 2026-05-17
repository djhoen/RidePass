# Section 7: Waivers, user identity & lifecycle

## Scope

Read end-to-end:

- `webapi/Controllers/WaiverController.cs` — admin CRUD, sign / per-waiver sign, "associated events" tab.
- `webapi/Controllers/UserController.cs` — login, public signup, profile updates, tenant-user create /
  promote / disable / reset, public password reset request + confirm.
- `webapi/Controllers/MeController.cs` — authenticated rider purchases / coupons / self-cancel (re-read
  for PII/auth lens; cancel mechanics were covered in Section 6 and aren't re-flagged here).
- `webapi/Controllers/SpectatorController.cs` — guest spectator buy + per-attendee waiver capture.
- `webapi/Controllers/CounterController.cs` — only the rider-create path (`CreateRider`) and the in-cart
  waiver sign branch in `CreateSale`. Counter PII / cart mechanics were Section 6.
- `Services/Repositories/WaiverRepository.cs`, `UserRepository.cs`, `PasswordResetRepository.cs`,
  `EventRepository.SetWaiverRole` / `ListByWaiverId`.
- `Services/Repositories/Data/PaymentData/TenantWaiver.cs` (also defines `RiderWaiverSignature`).
- `Services/Repositories/Data/UserData/PasswordResetToken.cs`.
- `webapi/Helpers/WaiverPolicy.cs`.
- `Services/Helpers/EncryptionHelper.cs` (and a repo-wide grep for usages).
- `webapi/Storage/LocalFilesystemImageStorage.cs`.
- Migrations: `Script0005_DayPassesWaivers.sql`, `Script0022_PasswordReset.sql`,
  `Script0023_WaiverSignatureImage.sql`, `Script0031_MinorWaiver.sql`,
  `Script0067_MultipleWaivers.sql`, `Script0069_EventWaiverSplit.sql`,
  `Script0071_SpectatorWaiverSignatures.sql`, `Script0072_EventWaiverPerAudience.sql`.
- Frontend: `vueapp/src/views/Waiver.vue`, `Admin/Waiver.vue`, `Login.vue`, `ResetPassword.vue`,
  `User/Profile.vue`, `BuySpectator.vue`, `BuyAdmissionFlow.vue` (waiver branch only),
  `Admin/CustomerDetail.vue` + `Admin/Counter.vue` (signature display), `components/SignaturePad.vue`.

There is no standalone `Signup.vue` or `PasswordReset.vue` — signup happens in `CreateAccount.vue`
(referenced by router only; not in scope today since the controller-side validation governs)
and `/ResetPassword` covers both the request-email and consume-token modes.

Sections 1 (auth/tenancy), 2 (payments/webhook/ledger), 3 (schema), 4 (day-pass + event-ticket
online), 5 (extras / season-pass / membership / gift-card / rental online), and 6 (counter / cancel
/ refund admin) findings are not repeated. Specifically not re-flagged here: the silent
`SuccessRehashNeeded` drop in `Login` (Section 1 H#5), the password-reset-timing oracle
(Section 1 M#13), the duplicate-email rider-vs-tenant-staff race in `CreateAccount` (Section 1 L#15),
the broken unique index for "one active waiver per tenant" since `Script0067` dropped
`uk_tenant_waiver_active` (noted in Section 3 — explicitly re-validated below as current
behavior, not as a new finding), and the 24-hour JWT TTL with no revocation (Section 1 M#11).

## Architecture summary

**Waiver model.** Each tenant has many `tenant_waiver` rows (`Script0067` removed the partial
unique index that enforced "at most one active per tenant"). The waiver carries `name` (admin
label), `title` (rider-facing heading), `body` (rich HTML), `is_active`, and an optional
`expires_at`. `WaiverRepository.GetActive` is a fall-back lookup that returns the **newest
non-expired active waiver** (`ORDER BY created_at DESC LIMIT 1`) — this is the tenant's
implicit default when an event doesn't pin a specific waiver. Events carry two nullable FKs:
`racer_waiver_id` (used by rider buy flows) and `spectator_waiver_id` (used by `SpectatorController`),
each independently set, each independently nullable. Per-audience required flags
(`requires_rider_waiver`, `requires_spectator_waiver`) decide whether the waiver is enforced
at purchase. The legacy `requires_waiver` bool is gone (`Script0072`).

**Signatures.** `rider_waiver_signature` carries `user_id` (nullable since `Script0071` to
allow guest spectator signatures), `waiver_id`, `signed_at`, `ip_address`, `signature_data_url`
(base64 PNG, inline, `text` column), `signed_by_parent` + `parent_name`/`parent_phone` for
minors, and spectator-specific columns (`signer_email`, `signer_name`, `spectator_first_name`,
`spectator_last_name`, `spectator_birthdate`). The partial unique index
`uk_rider_waiver_once_user (user_id, waiver_id) WHERE user_id IS NOT NULL` makes a registered
user's signature idempotent per waiver; the controller's `Sign` SQL `ON CONFLICT (user_id, waiver_id)
DO UPDATE SET signed_at = EXCLUDED.signed_at` deliberately does NOT overwrite the original
`signature_data_url` (the legal artefact) on a re-sign. Guest spectator signatures bypass that
unique constraint entirely and can pile up freely.

**Authentication.** `Login` checks the global pool (riders / super_admins, `tenant_id IS NULL`)
first, then a tenant-scoped pool only if a subdomain resolved. Apex login is super_admin-only.
JWT carries `UserId`, `role`, `tenant_id` (only when the user has one), `sub = email`, fresh
`jti`. No refresh tokens, no revocation list, no per-account lockout, no rate limit, no captcha.
24-hour default TTL.

**Public signup.** `CreateAccount` requires a resolved tenant subdomain (rider must sign up via
some track's domain), creates a **global** rider row (`tenant_id = NULL`), validates a non-empty
emergency contact name + ≥7 digits of contact phone + ≥7 digits of rider phone + a sane birthdate
(`< today`, `1900 ≤ year`, `age ≤ 130`). No email verification — account is active immediately.

**Password reset.** Self-serve. `RequestPasswordReset` always returns 200 (no email-enumeration
leak in the response shape). Token is 32 bytes of `RandomNumberGenerator`-derived hex, stored
hashed (SHA-256, hex) in `password_reset_token`, expires in 60 minutes, single-use enforced by
`used_at_utc`. `BuildResetUrl` rewrites the host to the tenant's subdomain for tenant-scoped
users so the link lands on the right `Login` flow.

**Counter rider create / account claim.** `Counter/Riders` POST generates a 32-byte random
"unknown password" the rider never sees, hashes it, persists the user as a global rider, and
trusts the rider to claim later via the public `/ResetPassword` email flow. Tenant-staff
provisioning (`UserController.CreateTenantUser`) follows a different shape: 12-byte hex temp
password emailed in cleartext, with a `ResetTenantUserPassword` admin override that does the
same thing.

**Minor + parent flow.** `WaiverPolicy.IsMinor(birthdate)` returns true iff a birthdate is on
file AND the rider is under 18 in UTC. Legacy users without a birthdate are treated as adults
(grandfathered). The controller demands a non-empty parent name and a parent phone with
`Length < 7` for minors — note this is `parentPhone.Length`, not digits-only length.

**Signature display.** Admin signature view is in `Admin/CustomerDetail.vue` (gated by
`CustomersView`) and `Admin/Counter.vue` (gated by `SalesCounter`). The signature is rendered
inline as `<img :src="dataUrl">`.

## Findings

| Severity | Location | Description | Suggested fix |
|---|---|---|---|
| **Critical** | `webapi/Controllers/UserController.cs:269-280` (`UpdateBirthdate`) | An authenticated rider can flip their `birthdate` to any past date at any time, including the night they're planning to sign a waiver, completely bypassing `WaiverPolicy.IsMinor` and the parent-guardian capture path. Today a 16-year-old creates an account with their real DOB, hits `PUT /api/User/Profile/Birthdate` with `2000-01-01`, signs the next waiver as an "adult," and the signed-by-parent / parent-name / parent-phone fields are never recorded. Same exploit lets a real adult say they're 12 to dodge a future race-day responsibility (less likely abuse but same hole). The tenant track owner is the one carrying the liability if a minor's signature stands on its own. | Treat birthdate as set-once-from-signup (or set-once-then-admin-overridable). Either remove the endpoint, restrict it to "only when current `birthdate IS NULL`" (covers legacy grandfathered users), or require an admin to action the change. At minimum, audit-log every birthdate change with old/new values + the requesting user, and reject the change if the rider has any signature on file as a minor. The same logic should reject `UpdateBirthdate` when there's an unredeemed pass/ticket the new DOB would re-classify (e.g. minor → adult through a future race day). |
| **Critical** | `webapi/Controllers/WaiverController.cs:73-100` (`Create` / `Update`) + `Services/Repositories/WaiverRepository.cs:64-76` (`Update`) | The admin "edit waiver in place" flow rewrites `title` and `body` on the existing row with no version bump. Every prior `rider_waiver_signature` whose `waiver_id` points to this row now references mutated legal text — the signed-at timestamp + IP audit row claims the rider agreed to whatever the admin most recently typed. The dialog shows a tonal info banner ("If you've changed the substance of the agreement, create a new waiver instead") but it's policy-by-suggestion, not enforcement. For a liability waiver this is the legal-integrity equivalent of allowing a notary to white-out the contract after the fact. The repository even comments "editing is in-place" as a deliberate choice on line 53. | Make `Update` either (a) refuse to change `body` (and probably `title`) when any signature exists for this `waiver_id`, returning a 409 with "create a new version instead," or (b) auto-fork: snapshot the old row as `is_active = false`, insert a new row with the bumped `version`, and have the admin's PUT actually return the new id. The "Publish New Version" legacy endpoint on line 148 already does the right thing — wire the admin UI through it for any change that touches `body`. As-is this is the single biggest waiver-integrity gap. |
| **Critical** | `webapi/Controllers/UserController.cs:148-150` (`CreateAccount` response shape) + `webapi/Controllers/CounterController.cs:135-190` (`CreateRider`) | There is no email-verification step on public signup. An attacker registers `victim@example.com` as a global rider, sets a password they know, and from that point owns the global account that any tenant will resolve as "the rider at this email." If the real owner later tries to claim their account via the counter's expected `/ResetPassword` path, the email is already in use — `RequestPasswordReset` sends them a reset link for the attacker's account, the attacker watches their mailbox, and the rider's identity gets bound to the attacker's account at every tenant. Worse, counter-created riders (line 163-166 generates a random unknown password) never get a "claim" email — they're expected to discover the `/ResetPassword` page on their own. So the first-mover wins. | Add an email-verification gate before a public-signup account can sign a waiver or buy. The lowest-friction shape: at signup, mark `status = 'pending_verification'`, email a confirm-token, and require it before `Login` will mint a JWT. Counter-created riders should be sent a "claim your account" email automatically (reuse the password-reset machinery — `_resetTokens.Insert(...)` + `_emailer.Send(...)`) so they don't need to know to visit `/ResetPassword`. Until then, riders cannot trust that the global pool's email is actually owned by the person logged in. |
| **High** | `webapi/Controllers/WaiverController.cs:311-317`, `SpectatorController.cs:309-314`, `CounterController.cs:800-805` (`IsValidPngDataUrl`, three copies) | The validator is implemented identically in three controllers — same magic numbers, same content-type pin (`data:image/png;base64,`). Lower bound 800 chars (≈ 600 bytes after base64) is a non-empty heuristic; upper bound 1,400,000 chars (≈ 1.05 MB raw, before any actual PNG decode) is the only abuse cap. Issues: (a) no decode is attempted — any 800-byte string starting with `data:image/png;base64,` passes, the database happily stores 1.05 MB of attacker-supplied bytes that aren't a real PNG and will fail rendering in every admin view; (b) the prefix check pins `image/png` exactly — the SignaturePad always emits PNG today so this works, but the scope notes mentioned an "OrJpeg" variant which doesn't exist anywhere in the repo (deferred or removed?); (c) triplication invites drift — a future fix to one copy won't propagate. | Extract into `Services/Helpers/SignatureValidator.cs`. Add a base64 decode + a PNG magic-bytes check (`89 50 4E 47 0D 0A 1A 0A`) so non-PNG payloads are rejected before the row is inserted. Keep the size cap; reject anything that doesn't actually decode. Optionally enforce a max raw byte count (e.g. 512 KB) which still comfortably fits a real handwritten signature. |
| **High** | `webapi/Controllers/UserController.cs:283-291` (parent-phone validation in `Sign`) | The minor-branch validation does `parentPhone.Length < 7` — this is the **string length** of whatever the rider typed, not the digit count. `"(555)"` (5 chars) is rejected as expected, but `"my dad"` (6 chars) is also rejected, and `"don't have"` (10 chars) is accepted. Other places use `UserController.DigitsOnly(phone).Length < 7` (the right pattern). This is the legal-paper "I need a parent phone if you're a minor" field and the only validation is "type at least 7 characters." | Use the same `DigitsOnly` helper that `CreateAccount`, `UpdatePhone`, `UpdateEmergencyContact`, and `CreateRider` all already use. The `SpectatorController` has a similar pattern (`.Replace("-", "").Replace("(", "")...` chain on line 180) that should also go through `DigitsOnly`. |
| **High** | `webapi/Controllers/UserController.cs:50-98` (`Login`) | No rate limit, no per-account lockout, no failed-attempt audit log. Combined with the 24-hour JWT TTL (Section 1) and email-as-username, a credential-stuffing attacker has unlimited shots against every account with no observable footprint. The "Always 200" pattern on password reset (correct) doesn't help here because Login does leak — the `BadRequestResult("Invalid email or password.")` is the same shape for both branches but the timing absolutely isn't (existing email + bad password walks through `PasswordHasher.VerifyHashedPassword`, which is intentionally slow; non-existent email returns immediately). | Add IP-based and account-based rate limiting on `POST /api/User/Login` (ASP.NET 7 has `Microsoft.AspNetCore.RateLimiting` built in). Log each failed attempt with IP + UA + email to the audit log; after N failures in M minutes, lock the account for K minutes (with admin-override). Consider equalizing timing: always run `_passwordHasher.VerifyHashedPassword` against a fixed throwaway hash when the user isn't found, so an attacker can't enumerate emails via timing. |
| **High** | `webapi/Controllers/WaiverController.cs:271-309` (`SignWaiverInternal` for expired waivers, called from `Sign` endpoint) | `POST /api/Waiver/Sign` (legacy single-waiver path) only calls `_repo.GetActive`, which already filters out `expires_at <= now()`. **But** the per-waiver `POST /api/Waiver/{id:guid}/Sign` correctly checks `waiver.ExpiresAt <= now` and returns 400 (lines 240-243). The asymmetry means a rider can race a `Sign` request the moment the active waiver flips inactive or expires — `_repo.GetActive` returns null, the controller returns "No active waiver" (200 NotFound), but a fast-follower can sign by id even after the admin deactivated a waiver because the `{id}/Sign` only blocks on expiry, not on `is_active=false`. Net result: signatures can accumulate against deactivated waivers, weakening the admin's ability to retire a waiver text. | In `SignWaiverById` (line 234) add `if (!waiver.IsActive) return BadRequestResult(...)` next to the existing expiry check. Equally important: every signature insertion path should re-check `is_active` server-side at the moment of insert so the admin's "deactivate" click has actual teeth. |
| **High** | `webapi/Controllers/WaiverController.cs:129-142` (`SetAssociatedEventRole`) + `Services/Repositories/EventRepository.cs:135-165` (`SetWaiverRole`) | When the admin checks "as rider" or "as spectator" on the Associated Events tab, the SQL **unconditionally clobbers** `event.racer_waiver_id` / `spectator_waiver_id` to the current waiver id — overwriting whatever waiver another admin attached previously, with no confirmation prompt and no audit log. The CASE statement on detach is defensive (only NULLs out when the column currently points at THIS waiver), but the attach path has no equivalent "is the slot already taken by a different waiver?" check. The admin UI presents this as a benign per-row checkbox and they have no way to see, from the Waiver edit dialog, that another waiver already covers that event for that audience. | Either (a) refuse to overwrite a non-null slot pointing at a different waiver (return 409 with the existing waiver's name + id, force a confirmation), or (b) write an audit-log entry on every attach/detach with the previous + new waiver id + the admin user. The "requires_*_waiver flag only flips true, never false on detach" comment in the SQL is also surprising — detaching the last waiver leaves the requires flag stuck on, and the event then falls back to the tenant default. That may or may not be intended; document it. |
| **High** | `webapi/Storage/LocalFilesystemImageStorage.cs:14-27` (`SaveAsync`) | Untrusted `fileExtension` is concatenated into the filename verbatim. Callers are admin endpoints (presumably catalog image uploads — out of Section 7 scope but worth noting because the storage is shared), but the extension is taken from the upload (`Path.GetExtension(file.FileName)` in the typical pattern). Path-traversal via `..%2F` in the extension is the obvious case; double-extension (`.jpg.exe`) for stored-XSS via served URL is another. Also: the entire `tenantId.ToString()` is trusted as a path segment (it's a GUID, so that's fine), but defence-in-depth would `Path.Combine` + canonicalize + verify the result still starts with `dir`. The bigger architectural concern: local-filesystem image storage on a single droplet means images vanish on rebuild/rotate, blocks horizontal scale, and means signatures-on-disk (if you ever migrate signatures off the row) would inherit this same fragility. | Validate `fileExtension` against an allowlist (`{.png, .jpg, .jpeg, .webp}`), lowercase before concat, and reject anything else. For production, move to S3 / DO Spaces (the comment on Section 7's open question 16 about signature storage applies here too) so the storage tier scales independently of the API droplet. |
| **High** | `vueapp/src/views/User/Profile.vue` (entire file) | The page renders a profile-edit form (`firstName`, `lastName`, `email`, `phone`, `aboutMe`), an avatar uploader, and a "Save" button that calls `userService.updateProfile(profile.value)` — but `UserController` has NO `UpdateProfile`, NO `UpdateEmail`, NO avatar-upload endpoint, and no `AboutMe` / `ImageUrl` columns on the `users` table. The page is dead UI: the rider clicks Save, the request 404s (or quietly succeeds against some other endpoint the helper happens to map to), and no data is persisted. The dedicated Phone and Emergency Contact cards above DO have working backends. This is a confusion / trust hazard — riders think they updated their name and email; they didn't. | Either delete the orphaned form section (riders can already edit phone + emergency contact via the working cards above), or wire up the backend endpoints — `UpdateName`, `UpdateEmail` (with re-verification!), `UploadAvatar` (with image-type allowlist + size cap). If `UpdateEmail` is added, it MUST require verification of the new address before flipping the column; otherwise the same takeover surface as the "no email verification on signup" Critical re-opens for active accounts. |
| **Medium** | `Services/Helpers/EncryptionHelper.cs` (entire file) | Dead code with hardcoded AES key + IV (lines 9-10), `AesManaged` (deprecated since .NET 6 in favor of `Aes`), swallowed exceptions, broken null-vs-string handling (`return null` from a non-nullable signature). Grep confirms zero callers anywhere in the repo. It's a chest of bad crypto patterns sitting in the codebase waiting to be cargo-culted by the next contributor. The hardcoded key is also a footgun if anyone DOES start using this — the key is in source control, so encryption is effectively obfuscation. | Delete the file. If PII encryption-at-app-layer becomes a requirement later, use ASP.NET `IDataProtectionProvider` (key rotation, FIPS-validated providers) or column-level encryption via Postgres `pgcrypto` with a key sourced from env / Key Vault. |
| **Medium** | `webapi/Controllers/UserController.cs:411-441` (`ResetTenantUserPassword`) + `:313-368` (`CreateTenantUser`) | Both endpoints generate a temporary password (`GenerateTemporaryPassword`, 12 bytes of hex → 24 chars), email it in cleartext, **and** return it in the response body so the admin's screen shows it (`CreateTenantUserResponse.TemporaryPassword`, `ResetTenantUserPasswordResponse.TemporaryPassword`). Two distribution channels for the same credential doubles the leak surface (email AND screen, screen also showing up in browser DevTools / HAR captures / screen recordings of admin sessions). Worse: the welcome email instructs the new user to log in with the temp password and reset *after first sign-in*, rather than forcing a one-time reset link that expires on use. There's no flag (`must_change_password`) tracked on the user row, so a tenant manager who got their temp password and never changed it can keep using it indefinitely. | Send a one-time password-reset token email (reuse `_resetTokens.Insert`) instead of a temporary password. Drop `TemporaryPassword` from the response shapes; admins don't need to see it. If admin-visible feedback is desired, return `"emailed": true` and let the admin confirm. Add a `users.password_must_change` boolean and gate Login until it's cleared by a successful self-reset. |
| **Medium** | `webapi/Controllers/UserController.cs:521-536` (`BuildResetUrl`) + `:538-546` (`ApexHostFromCurrent`) | Host construction is "trust whatever `Request.Host.Value` is and rewrite the leading label if there are 3+ dots." The middleware in Section 1 only validates that the resolved subdomain doesn't contain dots, but `Request.Host.Value` itself comes from the `Host:` header which a request can spoof on most reverse-proxy configs unless `KnownProxies` / `AllowedHosts` are pinned. If `Host:` is `evil.com`, the password-reset email contains a link to `https://tenantsub.evil.com/ResetPassword?token=...` — when a rider clicks, they'll either get a wildcard cert error (if the attacker's domain doesn't have one) or, if the attacker controls a wildcard, harvest the reset token. The mitigation depends on `AllowedHosts` being set in `appsettings.json` and on the reverse proxy stripping spoofed `Host:` headers; neither is verified in code here. | Build the reset URL from configured `Tenant:RootDomain` + the resolved tenant's `Subdomain` (already in `ITenantContext`), not from `Request.Host`. The current pattern only works correctly when the request actually came in on the right host; relying on that for security-bearing links is fragile. Also configure `app.UseHostFiltering()` / `AllowedHosts` explicitly to block spoofed `Host:` headers at the framework layer. |
| **Medium** | `webapi/Controllers/SpectatorController.cs:140-145` (Gate-fee vs. spectator-entry parity) | The `spectators.Count != gateFeeUnits` check is correct, but the check is ONLY made when a waiver exists (line 140's outer `if (waiver is not null)`). If the event requires a spectator waiver but no waiver can be resolved (e.g. the tenant has no active waiver AND the event's pinned `spectator_waiver_id` was deleted leaving the FK NULL via `ON DELETE SET NULL`), `waiver` ends up null at line 76, the per-spectator validation is skipped, and the buy proceeds with zero signatures captured. The event admin set `requires_spectator_waiver = true` expecting enforcement; the rider experiences no waiver UI; the legal record is silently empty. | If `ev.RequiresSpectatorWaiver` is true but `waiver` ends up null, fail loud: `return BadRequestResult("This event requires a spectator waiver but the tenant has no active waiver. Contact the track.")`. Same fix in `BuyAdmissionFlow.vue`'s race-entry flow if the same shape is reproducible there. |
| **Medium** | `webapi/Controllers/UserController.cs:100-151` (`CreateAccount`) | The endpoint validates emergency contact, phone, and birthdate but does NOT validate the password — no minimum length, no complexity, no length cap. `request.Password = ""` is rejected by `PasswordHasher` (it'll happily hash an empty string), so a rider could sign up with an empty password and the only barrier to login would be a future `request.Password == ""` line in `Login` (which there isn't). Quick test: an attacker can register accounts with `password=""` and use them to brute-force-script other riders' purchases / waivers. There's also no captcha / bot protection on this endpoint, no rate limit. | Add server-side password validation: minimum length 8 (matches the frontend `ResetPassword.vue` rule on line 16), reject all-whitespace, cap at 256 chars to keep `PasswordHasher` happy. Add an IP-based rate limit (≤ 5 signups per IP per hour). For the long term, add a captcha (hCaptcha / Cloudflare Turnstile) on the public signup endpoint specifically. |
| **Medium** | `webapi/Controllers/UserController.cs:153-198` (`GetProfile` response) | Returns the rider's full PII payload — email, phone, birthdate, address, emergency contact — over `/api/User/Profile`. Fine when called by the rider themselves; the tenant scope check on line 171 correctly rejects cross-tenant tenant_staff. But every admin path that displays "this customer" lands on `CustomerController.GetById` (Section 1), whose policy gate is `CustomersView`. The role permission catalog gives `CustomersView` to `tenant_admin`, `tenant_manager`, and `tenant_cashier`. A cashier looking up a walk-in to ring up their day pass legitimately needs name + phone + waiver state — they probably don't need birthdate, full address, or emergency contact PII for that decision. The current model exposes them all. | Decide per-role what counts as "customer PII" — at minimum, gate `address_line*` and `birthdate` to `CustomersView` only when the user has paid here (the current customer projection is already scoped to "users who've purchased / signed at this tenant" per the controller comment). Consider a separate `CustomersViewSensitive` permission for full PII. Documented as a defence-in-depth recommendation, not a bug — current access model is consistent with the role catalog. |
| **Medium** | `Services/Repositories/WaiverRepository.cs:162-181` (`Sign`) — `ON CONFLICT DO UPDATE SET signed_at` | The conflict-resolution writes a new `signed_at` timestamp but **keeps the original `signature_data_url`** (good for legal-artefact integrity per the comment) **and also keeps the original `ip_address`, `signed_by_parent`, `parent_name`, `parent_phone`**. If a rider was a minor when they first signed (parent name + phone captured) and the same row is "re-signed" after they turn 18, the row still says `signed_by_parent=true` with the parent's contact info — even though the rider could now sign as an adult. Conversely, if a minor's PARENT info changes (different guardian), the second sign attempt updates only `signed_at` so the audit row still names the prior parent. | Two options: (a) refuse to UPDATE — if a signature exists, return 409 "already signed; visit the per-waiver flow to view"; or (b) preserve the SIGNATURE bytes but DO update `signed_by_parent`, `parent_name`, `parent_phone`, `ip_address` to the new attempt. The current half-update mixes legal preservation with stale metadata in a way that's worse than either pure option. |
| **Medium** | `webapi/Controllers/UserController.cs:480-486` (password-reset SMTP not configured) | When `_emailer.IsConfigured` is false, the reset URL is logged at `LogWarning` level **with the user's email**: `_logger.LogWarning("Password reset requested for {Email} but SMTP is not configured. Reset URL: {Url}", user.Email, resetUrl)`. The URL contains a single-use 60-minute token. Anyone with log-read access (DevOps, on-call, any service that ingests logs) now has a reset link they can use to take over the account. This is a dev-affordance pattern that absolutely shouldn't survive contact with production. Compare with the email-in-logs concern raised by the Section 1 timing-side-channel discussion. | Either fail closed (`if (!_emailer.IsConfigured) return BadRequest("Password reset is not available — contact support.")`) or hash/redact the URL before logging it. At minimum, log only the user id, not the email + full URL. Better: structured-log the event without the secret token at all, and surface the unsent reset-link only via an authenticated super-admin endpoint. |
| **Medium** | `webapi/Controllers/UserController.cs:413-423` (`ResetTenantUserPassword` no audit log) | A tenant admin can reset any other tenant user's password — including another tenant admin's — and there's no `_audit.Log` call recording who reset whose password when. Same for `UpdateTenantUserRole` (lines 370-389) and `UpdateTenantUserStatus` (391-410): role escalations and account disables are silently mutational. Section 1's Findings #11 (auditing impersonation) raises a related concern but specifically about super-admin impersonation; this is the in-tenant equivalent. | Plumb the actions through `IAuditLogger` with kind `tenant_user.role_changed` / `tenant_user.status_changed` / `tenant_user.password_reset`, target = the affected user id, payload = old + new values. Tenant admins routinely investigate "who demoted my account" incidents — give them the trail. |
| **Medium** | `webapi/Controllers/UserController.cs:295-311` (`ListTenantUsers`) returns staff list including emails | Gated correctly by `UsersManage`, but the response includes every staff user's email + role + status. If the admin tier accepts a tenant_cashier role into `UsersManage` (it doesn't today — `UsersManage` is admin-only — but the permission catalog is configuration), this becomes a tenant-staff email harvester. Verified the current catalog isn't permissive; flagged for the model invariant. | None today. Documenting for future review of the permission catalog: `UsersManage` should remain `tenant_admin`-only, and any "users.view" relaxation should return ids + names but not emails by default. |
| **Low** | `webapi/Helpers/WaiverPolicy.cs:10-17` (`IsMinor`) | Returns `false` for users with no birthdate on file. The comment calls this "legacy users without a birthdate" — but Section 7's `CreateAccount` validates birthdate as required on signup (line 116-119), so the legacy path is closing. The fallback-to-adult policy is a soft hole: an account whose birthdate is unset (could happen if a tenant-staff user without a DOB tries to sign a waiver) silently signs as an adult. Confirmed by reading `CreateTenantUser`: tenant staff are created WITHOUT a birthdate column write, so a tenant_cashier with a global rider account (which would have a DOB) is fine, but a tenant-pool-only user signing a tenant waiver bypasses the minor check entirely. Probably never happens in practice (staff don't sign customer waivers) but the policy is too permissive. | Make `IsMinor(null)` return null-ish ("unknown"), and have the controller force a DOB capture before any waiver sign — or, simpler, treat unknown DOB as "yes, minor" and require parent info to proceed. The risk of a 16-year-old slipping through with no DOB is unbounded; the cost of an adult being asked once for their DOB before signing is one extra field. |
| **Low** | `webapi/Controllers/UserController.cs:567-572` (`GenerateTemporaryPassword`) | 12 bytes of randomness rendered as 24 hex chars — that's 96 bits of entropy, well above the strength a `PasswordHasher` would protect anyway, so the entropy itself is fine. The smell is the all-hex character set: harder to type without errors than a Diceware-style word phrase would be, and most rider-facing temp-password emails wind up requiring the recipient to copy/paste. Cosmetic. | Optional: switch to a 6-word Diceware passphrase generator for human-readability, or to a 16-char alphanumeric-symbol mix. No security delta. |
| **Low** | `vueapp/src/views/BuySpectator.vue:144-152` ("I have read and agree" checkbox) | The checkbox is local-only state (`waiverAgreed`); the server never sees it. The signature itself is the legal proof, so the checkbox is UX confirmation, not security. The agreement banner sits ABOVE a click-shield that prevents the rider from signing until name+DOB are filled (lines 159-166) — clever pattern, but it does mean the rider has to do two distinct UI actions ("check the box" + "trace a signature") for one legal intent. | No change required. Documenting as "this is UX, not a security control" so a future contributor doesn't accidentally make the checkbox load-bearing. |
| **Low** | `vueapp/src/components/SignaturePad.vue:90` (data URL via `toDataURL('image/png')`) | The canvas data URL is generated at the moment of `endStroke` and emitted up the chain — so the image bytes exist in the JS heap until the page unloads. Not a server-side concern, but for a tablet kiosk used at a registration counter, the previous rider's signature can be retrieved via DevTools by anyone who has the device until the page reloads. Combined with the `LocalFilesystemImageStorage` shared-disk shape, this is a soft "PII lives longer than the workflow" pattern. | For kiosk deployments, force a hard navigation away from the signing route after each completion. The `setTimeout(goBack, 700)` in `Waiver.vue` already does roughly this; verify the kiosk-Counter flow does the same after each rider. |
| **Low** | `webapi/Controllers/MeController.cs:74-78` and similar (`UserId` claim parsing) | The pattern `User.FindFirst("UserId")?.Value` then `Guid.TryParse(...)` is duplicated across every controller in scope (and most controllers outside scope). When the claim is missing the response is `BadRequest("Invalid token.")` rather than `Unauthorized()` — which is a status-code smell, not a security one. A consistent helper would also let us add an audit hook ("a request came in with `[Authorize]` succeeded but the UserId claim was malformed") that today nobody would notice. | Move to a `Services/Helpers/CurrentUser.cs` accessor that lazy-resolves the claim and throws / 401s consistently. Update every controller to use it. |

## Patterns worth replicating

- **`PasswordResetRepository` stores SHA-256 hex of the token, not the token.** Correctly cited
  in the migration comment. A leaked DB snapshot cannot be used to assume identities — the
  attacker needs the actual emailed string. Single-use enforcement via `used_at_utc` + 60-minute
  expiry is exactly the right shape.
- **`SpectatorController.Buy` per-spectator-signature loop with `NeedsSign` precomputation.**
  Adult buyers who already self-signed don't re-sign; children always re-sign; the
  `isMinor && isSelf` edge case is handled. This is the right kind of correct-by-construction
  loop for a multi-attendee flow.
- **`WaiverPolicy.IsMinor` UTC-anchored age calculation** is leap-year-safe (uses
  `AddYears(-age)` adjustment). The standard "(today - DOB).TotalDays / 365" pattern is wrong
  near birthdays; this one isn't.
- **`UserController.RequestPasswordReset` always-200 response** combined with
  `BuildResetUrl`'s tenant-subdomain rewrite is exactly the right shape for a tenant-aware
  password reset. The findings above suggest hardening the host construction and the SMTP-not-
  configured logging, not changing this overall shape.
- **`WaiverRepository.GetSignatureBySignerEmailForSelf`** correctly excludes
  `signed_by_parent` and `spectator_first_name IS NOT NULL` so the "already-signed for self"
  shortcut can't accidentally match a parent-of-child signature. Concise SQL with the right
  predicates.
- **`Admin/Waiver.vue` "duplicate then edit" flow** lets admins clone a waiver as a new row
  without risking the in-place-edit hazard from the Critical finding. If the in-place edit
  shipped through the duplicate path instead, the Critical disappears.

## Open questions

1. **Should counter-created riders get an automatic "claim your account" email?** Today the
   counter generates a random unknown password (correct — keeps the rider unable to log in)
   but never tells the rider that an account exists. Per Critical #3, this leaves the global
   email pool unprotected from first-mover takeover. Proposed fix: send a reset-link email at
   create-time with copy like "We created your RidePass account at $TenantName. Set your
   password to access it online."
2. **Signature storage migration plan.** `signature_data_url` is `text` (effectively unlimited
   in Postgres) with a 1.4 MB validator cap. A tenant with 5,000 active riders × ~50 KB per
   signature = 250 MB on the row. Index-busting for `rider_waiver_signature` queries that
   don't `SELECT signature_data_url` is avoided today because Postgres TOASTs the column out of
   line, but full-table scans + backups still carry the bytes. Plan: move to S3 / DO Spaces,
   keep a URL in the column, set the column type to `varchar(2048)` once migrated.
3. **`EncryptionHelper` removal.** Confirmed zero callers. Worth a one-PR cleanup before
   someone wires it up by accident.
4. **`UpdateBirthdate` policy.** Critical #1 calls this out, but the design intent isn't
   captured anywhere — is "rider can edit DOB freely" the actual policy, or a leftover from
   an early build? If the former, the minor-waiver story is fundamentally broken and needs a
   different mechanism (e.g. parent attestation captured at signup, immutable from there).
5. **Multi-tenant rider PII shared across tenants.** A rider's birthdate, address, and
   emergency contact live in the global `users` table — every tenant the rider has interacted
   with sees the same record. If a rider updates emergency contact via Profile.vue, every track
   they've ridden at gets the new info. Probably correct behavior, but document the model so
   admins understand they don't have per-tenant overrides.
6. **`ResetPassword.vue` rate limit.** Both the `requestReset` and `confirmReset` actions hit
   public endpoints without rate limiting. A bot could attempt token-guessing on
   `ConfirmPasswordReset` (64-hex-char tokens are 256-bit so brute-force is infeasible, but a
   timing oracle on `_resetTokens.GetByTokenHash` could narrow it). Worth adding an IP-based
   limit on `POST /api/User/ResetPassword/Confirm` specifically.
7. **`tenant_user_email` uniqueness within a tenant.** `CreateTenantUser` checks both
   `GetByEmail(tenantId, email)` and `GetGlobalByEmail(email)` — good. But two different
   tenants can both have tenant_admin rows with the same email (each row has a different
   `tenant_id`). Combined with the apex-subdomain handling, this means the same email is a
   valid login at site A as a `tenant_admin` and at site B as a separate `tenant_admin` —
   correct by design, but worth documenting in the admin onboarding flow.

## Coverage notes

- Read every file in the explicit scope end-to-end. The two largest (`UserController.cs` and
  `WaiverController.cs`) were the focus; everything else (`MeController`, `SpectatorController`,
  `CounterController` rider/wave-sign branches) was re-read for the auth + PII + lifecycle lens
  even though Sections 5 / 6 had already walked the purchase mechanics.
- Migrations relevant to waiver versioning and signature storage were read end-to-end
  (`Script0005`, `0022`, `0023`, `0031`, `0067`, `0069`, `0071`, `0072`). The current schema
  shape was confirmed live, not inferred from controller SQL.
- `EncryptionHelper` was grepped repo-wide to confirm zero callers.
- `Signup.vue` was searched for and not found; `CreateAccount.vue` (the actual signup view) is
  out of scope today — the backend validation in `UserController.CreateAccount` is the
  load-bearing layer and was reviewed.
- `BuyAdmissionFlow.vue` (race-entry buy with waiver) was spot-read for the
  `props.event?.racerWaiverId` resolution logic to confirm it falls through to the tenant
  default; the full purchase flow is Section 4 / 5 territory.
- Admin signature display was confirmed gated by `CustomersView` (CustomerDetail.vue) and
  `SalesCounter` (Counter.vue) — both routes are in the existing permission catalog and Section
  1 noted the policy handler is the right shape.
