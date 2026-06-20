# QA Test Plan: Auth & Accounts

> Scope: rider self-signup, login + JWT issuance, the JWT short-claim ("role") survival rule, self-serve password reset, email verification, global rider accounts across tenant subdomains, profile fields (birthdate / emergency contact / phone / address / racer info), and tenant staff user management + role-based access. Last updated: 2026-06-20.

## Surface map
- **Login / accounts:** `UserController.Login` (`POST /api/User/Login`), `UserController.CreateAccount` (`POST /api/User/CreateAccount`), `UserController.EmailExists` (`GET /api/User/EmailExists`, anonymous).
- **Password reset:** `UserController.RequestPasswordReset` (`POST /api/User/ResetPassword`, anonymous), `UserController.ConfirmPasswordReset` (`POST /api/User/ResetPassword/Confirm`, anonymous). Token store: `password_reset_token` (`Script0022_PasswordReset.sql`), SHA-256 hashed, 60-minute expiry, single-use.
- **Email verification:** `UserController.VerifyEmail` (`POST /api/User/VerifyEmail`), `UserController.ResendVerification` (`POST /api/User/ResendVerification`), both anonymous; rider verification token 7-day expiry.
- **Profile (self-serve, `[Authorize]`):** `GET /api/User/Profile`; `PUT /api/User/Profile/{EmergencyContact|Phone|RacerInfo|Address|Birthdate}`.
- **Tenant user management (`[Authorize(Policy = UsersManage)]`):** `GET/POST /api/User/Tenant`, `PUT /api/User/Tenant/{id}/Role`, `PUT /api/User/Tenant/{id}/Status`, `POST /api/User/Tenant/{id}/ResetPassword`.
- **JWT:** `Helpers/JwtIssuer.cs` (claims: `UserId`, `role` one-per-role with primary first, `NameIdentifier`, `tenant_id` when tenant-scoped, optional `impersonated_by`; 24h default). `Program.cs:27` sets `JwtSecurityTokenHandler.DefaultMapInboundClaims = false`.
- **Authorization:** `AuthPolicies/TenantPermissions.cs` (capability -> role map), `AuthPolicies/TenantPermissionHandler.cs` (union over `role` claims; super_admin bypass; tenant_id claim must equal resolved tenant).
- **Repositories:** `Services/Repositories/UserRepository.cs` (`GetGlobalByEmail`, `GetByEmail(tenantId, email)`, both `LOWER()` on each side), `PasswordResetRepository`.
- **Migrations:** `Script0008_GlobalRiders.sql` (riders go `tenant_id NULL`, `chk_user_tenant_scope`), `Script0012_TenantUserRoles.sql` (role CHECK), `Script0022` (reset tokens), `Script0024_UserBirthdate.sql`, `Script0032_EmergencyContact.sql`, `Script0073_UserAddress.sql`.

## Concepts under test
- **Two account pools.** Riders and super_admins are GLOBAL (`tenant_id IS NULL`); tenant_admin / tenant_* staff are tenant-scoped (`tenant_id NOT NULL`). Enforced by `chk_user_tenant_scope` (Script0008). Login resolves the global pool first, then the tenant pool only if a tenant is resolved.
- **Apex vs subdomain login.** With no tenant resolved (apex host), only super_admins may sign in; riders and tenant staff are told to use a tenant subdomain.
- **Rider email gate.** Role `rider` cannot log in until `email_verified`. Verification is sent only when SMTP `IsConfigured`; without SMTP the rider is auto-verified at signup so the account is not locked out. Pre-existing accounts were grandfathered verified.
- **Password hashing.** `IPasswordHasher<User>` (ASP.NET Core Identity PBKDF2). `SuccessRehashNeeded` triggers a re-hash that is persisted via `UpdatePasswordHash`, not just held for the request.
- **Short claim survival.** `DefaultMapInboundClaims = false` keeps `role` / `tenant_id` as their literal short names through the `ClaimsPrincipal` instead of being rewritten to the long SOAP claim-type URIs. The permission handler reads `FindAll("role")` and `FindFirst("tenant_id")`; if mapping were on, those reads return nothing and every tenant policy silently fails.
- **Multi-role staff.** A staffer can hold several roles; the JWT emits one `role` claim per role with the highest-privilege primary first. Permissions are the UNION of all held roles (`TenantPermissions.ForRoles`).
- **Reset token hygiene.** Token is random 32 bytes hex; only its SHA-256 is stored. Confirm rejects used, expired, or unknown tokens with the same generic message; request endpoint always returns 200 to avoid account enumeration.
- **Birthdate set-once.** Self-serve birthdate update is allowed only when none is on file (drives the minor / guardian waiver requirement); corrections after that route through track staff.

## Preconditions / test data
- Two active, published tenants on distinct subdomains: `acme.<root>` and `globex.<root>`; plus the apex host `<root>`.
- SMTP configured in the environment under test for the verification + reset link cases (and a second pass with SMTP NOT configured to exercise the auto-verify branch).
- A new throwaway rider email; an existing verified global rider; one super_admin; one tenant_admin and one limited-permission staffer (e.g. tenant_scanner) on `acme`.
- Ability to read the issued JWT (browser devtools / API client) and decode its claims.
- DB read access (prod read-only MCP or staging) to inspect `users`, `password_reset_token`, `email_verified`.

---

## Registration & email verification

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| AU1 [NN] | Rider signup on a subdomain | On `acme.<root>` submit CreateAccount with valid name, email, password, phone, birthdate, emergency contact | 200; a `users` row with `role = rider`, `tenant_id NULL`, hashed `password_hash`. With SMTP on: `email_verified = false`, `emailVerificationSent = true`, verify email sent. |
| AU2 [NN] | Signup blocked at apex | Call CreateAccount on the apex host `<root>` | 400 "Account creation must happen on a tenant subdomain." No row created. |
| AU3 [NN] | Duplicate email rejected | Sign up, then sign up again with the same email (any tenant) | Second call 400 "An account with this email already exists." `GetGlobalByEmail` dedup is global, not per-tenant. |
| AU4 [NN] | Email case-insensitive dedup | Register `Rider@x.com`, then attempt `rider@x.com` | Second rejected; `GetGlobalByEmail` lowers both sides. |
| AU5 [NN] | Invalid birthdate rejected | Submit a future date, year < 1900, or age > 130 | 400 "Please enter a valid birthdate." (`IsValidBirthdate`). |
| AU6 [NN] | Missing emergency / phone rejected | Submit blank emergency name, a < 7-digit emergency phone, or < 7-digit rider phone | 400 with the matching validation message; no row created. |
| AU7 [NN] | Verify email consumes token | Open the verify link, POST VerifyEmail with the token | 200 "Email verified."; `email_verified = true`. Re-POST same token returns the invalid/expired message (token cleared). |
| AU8 [NN] | Resend verification is non-revealing | POST ResendVerification for (a) an unverified rider, (b) a verified rider, (c) an unknown email | All return 200 with the same generic message; only case (a) actually sends. |
| AU9 [R] | No-SMTP auto-verify | With SMTP not configured, sign up a rider | 200, `emailVerificationSent = false`, `email_verified = true` immediately; the rider can log in. |

## Login & JWT

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| AU10 [NN] | Rider login on subdomain | Verified rider logs in on `acme.<root>` | 200; `LoginResponse` with token, role `rider`, `TenantId` = resolved tenant (rider has no own tenant_id so it falls back to the resolved one). |
| AU11 [NN] | Unverified rider blocked | New rider (SMTP on, not yet verified) attempts login | 400 "Please verify your email before signing in." No token. |
| AU12 [NN] | Wrong password generic error | Correct email, wrong password | 400 "Invalid email or password." (same message as unknown email; no enumeration). |
| AU13 [NN] | Disabled account blocked | Login as a user whose `status != active` | 400 "Invalid email or password." |
| AU14 [NN] | Apex login: super_admin only | On apex `<root>`: (a) super_admin logs in, (b) a rider logs in | (a) succeeds; (b) 400 "Please log in from your tenant's subdomain." |
| AU15 [NN] | Tenant staff login | tenant_admin logs in on its own `acme.<root>` | 200; token carries `tenant_id` claim = acme. (Cross-subdomain staff login is covered in the isolation plan, MT.) |
| AU16 [NN] | Password rehash persists | Force a legacy/weaker hash for a user, log in with the right password | Login succeeds; `password_hash` in the DB is rewritten (SuccessRehashNeeded path), and a second login still works against the new hash. |
| AU17 [NN] | JWT short claim survives | Decode the token from AU15; confirm an authorized tenant call works | Token has literal `role` and `tenant_id` claims (not the long `schemas.xmlsoap.org/.../role` URI). A `UsersManage`-policy call succeeds, proving the handler read `role`/`tenant_id`. This is the `DefaultMapInboundClaims = false` regression guard. |
| AU18 [NN] | Multi-role union in JWT | Assign a staffer both tenant_cashier and tenant_scanner; log in | Token has two `role` claims; primary (higher precedence = cashier) first; the account can hit both SalesCounter and SalesRedeem endpoints (union of permissions). |

## Password reset

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| AU19 [NN] | Request reset is non-revealing | POST ResetPassword for (a) a real rider, (b) an unknown email | Both 200 "If that email exists, a reset link has been sent." Only (a) inserts a `password_reset_token` row and (with SMTP) sends mail. |
| AU20 [NN] | Confirm sets new password | Use the link's token in ResetPassword/Confirm with a new password | 200; old password fails login, new password works; token row `used_at_utc` is set. |
| AU21 [NN] | Token is single-use | Reuse the AU20 token a second time | 400 "This reset link is invalid or has expired." (UsedAtUtc guard). |
| AU22 [NN] | Token expiry | Confirm with a token whose `expires_at_utc` is in the past (60-minute window; force in DB or wait) | 400 invalid/expired; password unchanged. |
| AU23 [NN] | Token stored hashed | Inspect `password_reset_token.token_hash` after a request | Value is a 64-char hex SHA-256, not the raw token in the email link. |
| AU24 [NN] | Reset link host targeting | Request reset for (a) a global rider on `acme`, (b) a tenant_admin on `acme` | (a) link uses the request host; (b) `BuildResetUrl` points the link at the staffer's own tenant subdomain (`{subdomain}.{apex}`). |
| AU25 [R] | Confirm on disabled/missing user | Confirm a valid token whose user was since disabled | 400 invalid/expired (status checked before applying). |

## Profile fields (self-serve)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| AU26 [NN] | Get profile | Authed rider GET /Profile | Returns id, tenant_id, email, name, role, status, phone, birthdate, emergency contact, full address, bike, race number. |
| AU27 [NN] | Update emergency contact | PUT /Profile/EmergencyContact with valid name + phone; then with blank name | First 200 and persists; second 400 validation. |
| AU28 [NN] | Update phone validation | PUT /Profile/Phone with a < 7-digit number | 400; valid number persists. |
| AU29 [NN] | Update address normalization | PUT /Profile/Address with mixed blanks; omit country | Blanks stored as NULL; country defaults to "US" (`Script0073`). |
| AU30 [NN] | Birthdate set-once | Rider with no birthdate PUTs one (200); same rider PUTs a different birthdate | Second 400 "Your date of birth is already on file..." Confirms set-once guard. |
| AU31 [NN] | RacerInfo length caps | PUT /Profile/RacerInfo with bike > 100 chars or race number > 16 chars | 400 length message; valid values trim and persist (blank -> NULL). |
| AU32 [R] | Profile edits require auth | Call any /Profile PUT with no/expired token | 401. With a malformed `UserId` claim, 400 "Invalid token." |

## Tenant user management & role-based access

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| AU33 [NN] | Create staff user | tenant_admin POST /Tenant with a new email + roles | 200; tenant-scoped row (`tenant_id` = acme), primary role derived by precedence, temp password returned; welcome email if SMTP on. |
| AU34 [NN] | Staff email collides with rider | POST /Tenant with an email already registered as a global rider | 400 "That email is already registered as a rider on RidePass." (global + tenant uniqueness both checked). |
| AU35 [NN] | Unassignable role rejected | POST /Tenant with role `super_admin` or `rider` | 400 "Role '...' is not assignable." (`AssignableRoles` allowlist). |
| AU36 [NN] | Empty role set rejected | POST /Tenant with no Role and empty Roles[] | 400 "At least one role is required." |
| AU37 [NN] | Cannot remove own admin role | tenant_admin updates their own user to a role set without tenant_admin | 400 "You can't remove your own admin role." |
| AU38 [NN] | Cannot disable self | tenant_admin sets their own status to disabled | 400 "You can't disable your own account." |
| AU39 [NN] | Status validation | PUT /Tenant/{id}/Status with a value other than active/disabled | 400 "Status must be 'active' or 'disabled'." |
| AU40 [NN] | Limited role denied UsersManage | tenant_scanner (only SalesRedeem) calls any /Tenant management endpoint | 403 (policy not in role's permission set). |
| AU41 [R] | Admin reset of staff password | tenant_admin POST /Tenant/{id}/ResetPassword for a staffer on acme | 200; temp password returned; staffer's old password fails, new one works. |
| AU42 [R] | Disabled staff cannot log in | Disable a staffer, then attempt login | 400 invalid email or password (status gate in Login). |

---

## Edge & adversarial

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| AU43 [NN] | Forged role claim ignored at tenant scope | Hand-craft / replay a JWT for a real acme staffer but call from `globex` | TenantPermissionHandler requires `tenant_id` claim == resolved tenant, so all tenant policies fail (403). Cross-checked in the isolation plan (MT). |
| AU44 [NN] | Self-management of another tenant's user | acme tenant_admin calls PUT /Tenant/{id}/Role with the id of a globex staffer | 404 "User not found on this tenant." (target `tenant_id` mismatch). |
| AU45 [NN] | Profile of a tenant-scoped user on wrong subdomain | acme staffer's token used to GET /Profile while resolved tenant is globex | `Forbid()` (defence-in-depth tenant match in GetProfile). |
| AU46 | Reset-token user-id tampering | Confirm a valid token; verify the new password applies only to the token's bound `user_id`, never an attacker-supplied id | No user id is accepted from the request; user is derived from `token.UserId`. |
| AU47 | Temp-password entropy | Inspect generated temp/reset values | Reset token 32 random bytes; temp password 12 random bytes hex via `RandomNumberGenerator`. |

## Known risks / watch-items
- **Mapping-flag fragility (AU17).** The whole tenant authorization layer depends on `DefaultMapInboundClaims = false`. If that line is removed or an upgrade flips the default, `FindAll("role")` / `FindFirst("tenant_id")` return nothing and every tenant policy fails closed (403 storms), while super_admin bypass still works, making it easy to misdiagnose. Keep AU17 as a permanent regression guard.
- **Verification depends on SMTP at signup time (AU9 vs AU1).** A rider who signs up while SMTP is down is auto-verified; one who signs up while SMTP is up but mail never arrives is locked out until ResendVerification. Confirm the intended behavior per environment.
- **Reset/verify token tables are not tenant-scoped.** `password_reset_token.token_hash` has a global unique index and the verification token lives on the user row; security rests on token unguessability. Confirm tokens are never logged in plaintext except the deliberate no-SMTP `LogWarning` in RequestPasswordReset.
- **Apex rider/staff UX (AU14).** Apex login deliberately refuses non-super_admins; confirm the SPA surfaces the "use your subdomain" guidance rather than a generic failure.
- **Email is the identity key.** Lookups lower both sides (good), but there is no normalization of plus-addressing or unicode; treat as accepted behavior unless product says otherwise.
- See the **Multi-tenancy & Tenant Isolation** plan for cross-tenant login, JWT-tenant cross-checks, and per-tenant data probes.
