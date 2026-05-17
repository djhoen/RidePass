# Section 1: Multi-tenancy, authentication & permissions

## Inline fixes applied during this review

Three findings below were patched in-place during the review because each was an actively-exploitable
cross-tenant data leak or write and the fix was a one- or two-file change. Each is still listed in the
Findings table below for the historical record.

1. **Critical — `SuperAdminController.ListTenantLedger`** missing `[Authorize]`: added
   `[Authorize(Policy = SuperAdminRequirement.PolicyName)]` (matches every neighbor action).
2. **High — `SeasonPassController.CheckIn` cross-tenant write**: `ISeasonPassRepository.UpdateReservationStatus`
   now takes `tenantId` and the SQL joins through `season_pass_purchase` to filter by tenant; the controller
   passes `_tenantContext.TenantId`. Also updated the second caller in `ReportsController.SetCheckIn`
   (`season_pass` branch) which had the same gap by extension.
3. **High — duplicate `[Authorize]` on `UpdateTenantServiceCharge`**: removed the duplicate (cosmetic but
   adjacent to the Critical above and a smell that pointed at missing-attribute discipline in the controller).

Everything else in the Findings table is still open and should be triaged before Section 2 work begins.

## Scope

Files read end-to-end:
- `webapi/Middleware/TenantResolutionMiddleware.cs`
- `webapi/Multitenancy/ITenantContext.cs`, `webapi/Multitenancy/TenantContext.cs`
- `webapi/AuthPolicies/SuperAdminRequirement.cs` (also contains `SuperAdminHandler`)
- `webapi/AuthPolicies/TenantAdminRequirement.cs`, `TenantAdminHandler.cs`
- `webapi/AuthPolicies/TenantPermissionRequirement.cs`, `TenantPermissionHandler.cs`, `TenantPermissions.cs`
- `webapi/Helpers/JwtHelper.cs`, `webapi/Helpers/JwtIssuer.cs`, `webapi/Helpers/ClaimHelper.cs`
- `webapi/Program.cs` (auth + middleware wiring)
- `webapi/appsettings.json` (no per-env file in repo)

Controllers walked action-by-action for `[Authorize]`/policy, `IsResolved` guard, and tenant scope on repo calls:
- `BlackoutController`, `CampaignController`, `CounterController`, `CouponController`, `CustomerController`,
  `DashboardController`, `DiscoverController`, `EventController`, `EventSubscriptionController`,
  `EventTicketTierController`, `EventTypeController`, `ExtraController`, `FeedbackController`,
  `MeController`, `MembershipController`, `NewsletterController`, `NotificationController`,
  `PassProductController`, `PaymentController` (webhook only; bulk-of-state lookups by PI),
  `PurchaseController`, `QrController`, `RedemptionController`, `RentalController`, `ReportsController`,
  `RewardController`, `SeasonPassController`, `SpectatorController`, `SuperAdminController`,
  `SurveyController`, `TenantController`, `TenantPayoutController`, `UserController`,
  `WaitlistController`, `WaiverController`.

Repository method signatures spot-checked to verify `tenantId` is accepted/passed where applicable
(`PassPurchaseRepository`, `EventTicketPurchaseRepository`, `RentalRepository`, `EventExtraRepository`,
`SeasonPassRepository`, `NotificationRepository`, `DisputeRepository`, `TenantLedgerRepository`).

## Architecture summary

**Tenant resolution.** `TenantResolutionMiddleware` runs before authentication. It lowercases `Request.Host.Host`,
strips the configured `Tenant:RootDomain` suffix, and accepts only a single non-dotted label as a tenant
subdomain. The apex host (`ridepass.io`), `localhost`, and IP literals resolve to "no tenant" and fall through
to subsequent middleware. When development cannot rely on a host header (Vite dev server on a separate origin),
the middleware optionally falls back to a `X-Tenant-Subdomain` request header **but only in `env.IsDevelopment()`**
— production cannot be coerced by this header. Resolved tenants are cached in `IMemoryCache` for 5 minutes by
subdomain; inactive or missing tenants get a `404`. The resolved `Tenant` is stored on a scoped `TenantContext`
that controllers read via `ITenantContext` — `IsResolved`, `Tenant`, `TenantId`, `Subdomain`. Importantly,
`TenantContext.Tenant` throws if accessed before resolution, so the `IsResolved` checks scattered through
controllers are the documented way to short-circuit before that throw.

**JWT authentication.** `JwtIssuer` produces HS256-signed tokens with claims `UserId`, `role`, `ClaimTypes.NameIdentifier`,
`tenant_id` (only when `User.TenantId.HasValue`), and optionally `impersonated_by`. The token also carries
`sub = email` and a fresh `jti`. Default lifetime is 24 hours (impersonation overrides to 1 hour).
`Program.cs` configures `JwtBearer` with `ValidateIssuer/Audience`, `ValidateIssuerSigningKey`, `RoleClaimType = "role"`,
and disables `MapInboundClaims` so short claim names survive. Signing key + issuer are pulled from configuration
(via `dotnet user-secrets`/env vars; `appsettings.json` ships the empty placeholders). No refresh tokens, no
token revocation list, no audience separation.

**Authorization.** Three families of requirements:
- `SuperAdminRequirement` — succeeds iff `role == "super_admin"`. Singleton handler.
- `TenantAdminRequirement` — succeeds for super admins (unconditionally) or for `tenant_admin` users whose
  `tenant_id` claim equals the subdomain-resolved `_tenantContext.TenantId`. Scoped handler injecting
  `ITenantContext`.
- `TenantPermissionRequirement(permission)` — central catalog in `TenantPermissions.cs` mapping eleven
  capability keys (`users.manage`, `settings.manage`, `catalog.manage`, `sales.counter/redeem/view/cancel`,
  `reports.view`, `disputes.view`, `campaigns.manage`, `customers.view`) to per-role permission sets
  (`tenant_admin` gets all; `tenant_manager` gets a curated subset; `tenant_cashier`/`tenant_scanner`/
  `tenant_accountant` are tightly scoped). Super admins always pass these checks. Tenant-scoped users
  must additionally have a `tenant_id` claim matching the resolved subdomain — so even with a stolen JWT,
  a `tenant_admin` for site A cannot act on site B because the subdomain establishes a different
  `_tenantContext.TenantId`.

**Tenant scoping pattern.** Repositories that mutate or read per-tenant rows take `tenantId` as a parameter
(`Cancel(id, tenantId, …)`, `GetById(id, tenantId)`, `UpdateRole(id, …)` after a tenant-scoped fetch, etc.).
Controllers pass `_tenantContext.TenantId` consistently. A few repositories — notably
`PassPurchaseRepository.MarkRefunded`, `RentalRepository.MarkOut/MarkReturned/UpdateStatus/SetCheckoutCondition/
SetReturnCondition`, `EventExtraRepository.GetPurchase`, and `SeasonPassRepository.UpdateReservationStatus`
— accept a row id without tenant scope; in those cases the controller must perform a tenant-scoped
fetch first, then call the mutation. This pattern is followed in most places I read but has exceptions
flagged below.

**Public/anonymous endpoints.** `DiscoverController` is cross-tenant by design (no `IsResolved`).
`SuperAdminController.Bootstrap` is anonymous one-shot. `QrController` renders QR PNGs for any token —
the token itself is the secret. `EventController.GetPublic`, `Event/{id}/Public`, `Survey/Public/{token}`,
`Newsletter/Unsubscribe`, `EventSubscription/Unsubscribe`, `SpectatorController.Buy`,
`PurchaseController.BuyEventTicket` (guest checkout), and `FeedbackController.Submit` all keep
`IsResolved` checks and verify the token/id belongs to the resolved tenant in the SQL layer.

## Findings

| Severity | Location | Description | Suggested fix |
|---|---|---|---|
| **Critical** | `webapi/Controllers/SuperAdminController.cs:556-561` (`ListTenantLedger`) | Endpoint `GET /api/SuperAdmin/Tenants/{tenantId:guid}/Ledger` is missing `[Authorize(Policy = SuperAdminRequirement.PolicyName)]` while every sibling endpoint on the controller carries it. The controller has no class-level `[Authorize]`. Any unauthenticated caller can read any tenant's full financial ledger by guessing/enumerating tenant GUIDs. Compared to `GetReconciliation`, `ListTenantBalances`, `ListTenantPayouts`, etc., this is clearly an omission, not an intentional public endpoint. | Add `[Authorize(Policy = SuperAdminRequirement.PolicyName)]` to the action; consider adding a class-level `[Authorize(Policy = SuperAdminRequirement.PolicyName)]` so future endpoints inherit the default and only `Bootstrap` opts out with `[AllowAnonymous]`. |
| **High** | `webapi/Program.cs:115-124` (CORS) | `policy.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials()` — accepts any origin **with credentials**, including in production. This effectively negates CORS protection: a malicious site loaded in any tenant admin's browser could issue authenticated requests via JWT in cookies (if used) or by reading the JWT from `localStorage` and replaying it. The comment claims "dev defaults; tighten for prod via config", but no production override is wired up. | Pin CORS to the configured `Tenant:RootDomain` and its subdomain pattern (`*.ridepass.io`) when not in Development. `policy.SetIsOriginAllowed(o => Uri.TryCreate(o, UriKind.Absolute, out var u) && u.Host.EndsWith("." + rootDomain))` is a typical fix. Validate per-environment via configuration. |
| **High** | `webapi/Controllers/SuperAdminController.cs:531-554` (`UpdateTenantServiceCharge`) | The action has two stacked `[Authorize(Policy = SuperAdminRequirement.PolicyName)]` attributes — likely a copy-paste duplicate. Both will resolve to the same policy so ASP.NET will not deny in error, but the duplicate is a clear smell that suggests other endpoints might be missing it. Treated as High because it sits next to the Critical above and is visible evidence of inconsistent attribute discipline in this controller. | Remove the duplicate. Then audit every action in `SuperAdminController.cs` for the policy attribute (only `Bootstrap` should be `[AllowAnonymous]`). |
| **High** | `webapi/Controllers/UserController.cs:80-84` (`Login`) | When `_passwordHasher.VerifyHashedPassword` returns `SuccessRehashNeeded`, the comment says `// TODO: persist re-hashed password.` and the new hash is silently dropped. This leaves users authenticated on old (weaker) password parameters indefinitely. Not exploitable by itself, but the TODO undermines the value of using `PasswordHasher<TUser>`. | Persist the rehashed `PasswordHash` via `_userRepository.UpdatePasswordHash(user.Id, user.PasswordHash)` before issuing the token. |
| **High** | `webapi/Controllers/EventSubscriptionController.cs:89-107` (`StatusByEmail`) | `[AllowAnonymous] GET /api/EventSubscription/Status?email=...` returns `Subscribed = true/false` (plus the stored phone) for any email the caller guesses. This is a tenant-scoped email-presence oracle: an attacker can enumerate which addresses are subscribed to a given tenant and, where they are, harvest the stored phone number. | Either require auth, return a uniform response shape regardless of match, or rate-limit by IP. At minimum, do not echo back the stored phone number in this anonymous response — that's PII leakage. The Mine endpoint already covers the authenticated case. |
| **High** | `webapi/Controllers/RewardController.cs:163-168` (`ListRiderRedemptions`) | Guarded with `TenantPermissions.Policy.SalesCounter`, but a cashier role's `SalesCounter` permission is largely about ringing up sales — exposing arbitrary `userId` redemption history at the controller level is broader than the policy intent. More importantly, the endpoint never validates that `userId` exists in this tenant: a cashier at tenant A can probe redemption history for any user GUID. Filtering happens by program-tenant in `RedemptionsForUser`, so it's not a cross-tenant leak, but it's an enumeration affordance over global rider-program enrollment data. | Resolve the user through `_users.GetById(userId)` first; if `user.TenantId.HasValue && user.TenantId != _tenantContext.TenantId` return 404. Add `IsResolved` guard. Consider whether `SalesCounter` is the right gate — `CustomersView` may be more appropriate. |
| **High** | `webapi/Controllers/SeasonPassController.cs:491-501` (`CheckIn`) | The action takes a reservation id and calls `_passes.UpdateReservationStatus(id, "checked_in", staffId)`. There is no SQL-level tenant filter on `UpdateReservationStatus`, and the controller's comment explicitly admits: *"Cheapest: just mark and rely on the upstream Pass lookup having scoped by tenant"* — but that lookup happens on a separate request. A tenant staff user can mutate any reservation in the database whose GUID they know. Cross-tenant write. | Either add a `tenantId` parameter to `UpdateReservationStatus` and filter in SQL, or do a join-based fetch (e.g., `_passes.GetReservation`) and verify `purchase.TenantId == _tenantContext.TenantId` before mutating. The comment about defense-in-depth needs to become real code. |
| **Medium** | `webapi/Controllers/EventController.cs:209-263, 277-414, 422-443` (`Create`, `Update`, `Delete`, `Duplicate`, `UploadImage`) | Admin actions guarded by `CatalogManage` but missing the `IsResolved` short-circuit. Because the policy handler requires `_tenantContext.IsResolved && claim == TenantId`, the policy itself blocks unresolved-tenant callers (the requirement returns without `Succeed`), so this isn't exploitable — but if someone ever loosens the handler, every CRUD body would dereference `_tenantContext.Tenant`/`_tenantContext.TenantId` and throw from the `TenantContext` accessor. | Add the standard `if (!_tenantContext.IsResolved) return BadRequest("No tenant resolved.");` early-out, matching the convention in `BlackoutController`, `PurchaseController.ListForAdmin`, etc. |
| **Medium** | `webapi/Controllers/PassProductController.cs:38-44, 64-83, 86-95, 98-108`; `EventTypeController.cs:42-58, 62-76, 90-111, 119-140`; `WaiverController.cs:62-101, 108-141, 146-152`; `RewardController.cs:28-87`; `MembershipController.cs:182-200`; `CouponController.cs` (whole class via `[Authorize(Policy=CampaignsManage)]`); `CustomerController.cs`; `CampaignController.cs`; `TenantPayoutController.cs` non-CSV endpoints; `ReportsController.cs` admin actions | Same pattern: admin endpoints whose `[Authorize(Policy=...)]` handler already enforces `IsResolved`, but the action body still dereferences `_tenantContext.TenantId` directly without the early-out. Same defense-in-depth concern as above. | Adopt one of two conventions and apply consistently: (a) make the policy handler the single source of truth and let `_tenantContext.TenantId` throw under misuse, or (b) require every body that touches `_tenantContext.TenantId` to gate on `IsResolved`. Current codebase mixes both; the mix is the smell. |
| **Medium** | `webapi/AuthPolicies/TenantAdminHandler.cs:19-23` and `TenantPermissionHandler.cs:23-28` | Both handlers grant a super admin every tenant permission "during support work" without verifying `_tenantContext.IsResolved`. In combination with the impersonation flow (`SuperAdminController.Impersonate` issues a token with the target user's role, not super_admin), this is fine for direct super-admin actions — but if a super admin's JWT is ever used against an unresolved tenant (e.g., apex), the policy succeeds and the action body may then crash on `_tenantContext.Tenant`. Not a security bug per se, just brittle. | Add `if (!_tenantContext.IsResolved) return Task.CompletedTask;` short-circuit for tenant-scoped permission checks where IsResolved is implied. Super-admin-only endpoints can use `SuperAdminRequirement` (the policy that genuinely doesn't need a resolved tenant). |
| **Medium** | `webapi/Helpers/JwtIssuer.cs:48` (default token lifetime 24 hours) + `Program.cs:148` (no `ClockSkew = 0`) | Default 24-hour token life with the default 5-minute clock skew means a stolen JWT is valid for up to 24h05m and there is no revocation. Tenant admins managing money can be hijacked for a working day. No refresh-token rotation. | Lower the default to 1–4 hours, add refresh tokens with rotation + server-side revocation, or at minimum add an `iat`-based session marker that you can invalidate by storing a `users.password_changed_at` watermark and rejecting tokens older than it. |
| **Medium** | `webapi/Helpers/JwtIssuer.cs:21-52` (impersonation) | When a super admin impersonates a target user, the token carries `role = target.Role` and `tenant_id = target.TenantId`, plus `impersonated_by = currentSuperAdminId`. The `impersonated_by` claim is recorded but no handler reads it — neither for audit log enrichment nor for rejecting writes to high-risk surfaces. Auditing impersonated actions can't be distinguished from real ones at the controller level. | Plumb `impersonated_by` into `HttpContextAuditLogger` so audit rows carry both `actor_user_id` (the impersonated user) and `impersonated_by_user_id` (the super admin). Consider whether refunds / payout actions / user-management should be disallowed (or require step-up) under impersonation. |
| **Medium** | `webapi/Controllers/UserController.cs:445-491` (`RequestPasswordReset`) | The handler short-circuits non-existent emails with the same shape but performs a DB lookup against the resolved tenant's tenant-scoped users when no global rider matches. Different SQL execution time between (found vs not-found) creates a timing side channel an attacker can use to confirm an email is a tenant staff account. | The constant-time pattern is fine here in practice (Postgres email lookups are millisecond-scale), but consider always running the global lookup followed by the tenant-scoped lookup with a fixed sleep, or always go through both branches. Low-priority — note for future hardening. |
| **Medium** | `webapi/Middleware/TenantResolutionMiddleware.cs:35-42` (dev header escape hatch) | `X-Tenant-Subdomain` header is only honored when `env.IsDevelopment()`, which is correct, but the gating is on the .NET environment, not on a separate "allow dev tenant header" flag. If anyone ever deploys to production with `ASPNETCORE_ENVIRONMENT=Development` (e.g., staging-as-prod), tenant takeover becomes trivial. | Add an explicit configuration flag (`Tenant:AllowDevSubdomainHeader: true`) and gate the header off both `env.IsDevelopment()` AND the flag, so misconfigured environments can't accidentally enable it. |
| **Medium** | `webapi/Controllers/TenantPayoutController.cs:34-45` (`GetPayoutCsv`) | Class-level `[Authorize(Policy = ReportsView)]` covers this, but the endpoint streams the full CSV with no rate limit. Combined with the very-long-lived JWT, a leaked tenant accountant token could enumerate payout history. Same class as the JWT-lifetime issue but worth calling out for the financial surface specifically. | Cap response size or rate-limit per user. Lower JWT TTL (per Medium above) materially helps. |
| **Low** | `webapi/Helpers/ClaimHelper.cs:7-24` (`ClaimHasRole`) | Uses `requiredRoles.Contains(role.Value)` which is a substring match (`"tenant_admin".Contains("admin")` is `true`). If any code path uses this for authorization checks against a comma-separated list, a `tenant_staff` user could pass an `"admin"` requirement. Grep shows this helper isn't called from any active controller, so the bug is unreachable today — but the implementation is dangerous to leave around. | Either delete `ClaimHelper.cs` (no callers) or rewrite to use `==` (or `Split(',')` + exact match). |
| **Low** | `webapi/Controllers/UserController.cs:147` (`CreateAccount`) | Rider creation runs `_userRepository.Create(user)` directly. If the email is already in use as a tenant_admin/staff at this tenant, we check via `GetGlobalByEmail` first but **not** `GetByEmail(tenantId, email)`. A rider can sign up with the same email as a tenant-scoped admin user on that tenant; the system then has two rows with the same email and the Login flow may pick the global one preferentially. | Mirror the dual-check that `CreateTenantUser` performs (lines 322-333). |
| **Low** | `webapi/Controllers/MeController.cs:174-200` (`ShareCoupon`) | The action fires off an SMTP send asynchronously with `_ = SendShareEmailAsync(...)`. The recipient email is operator-supplied. No rate limit, no per-user-per-day cap. A signed-in rider can use the platform as a free transactional email gateway by repeatedly sharing the same coupon to different addresses. | Add a per-user-per-day cap (e.g. 50 shares/day) on the coupon-share endpoint, or move email sends into the background worker with explicit throttling. |
| **Low** | `webapi/Controllers/QrController.cs:13-26` | `[AllowAnonymous] GET /api/Qr/{token:guid}` renders any token's QR PNG. Comment says "the token itself is the secret." That's true, but if an email with this QR is ever forwarded, the recipient gets a redeemable token they can present at the gate. The redemption endpoints (`RedemptionController`) still require staff auth, so the actual abuse vector is one staff member redeeming a rider's pass — already a trust issue independent of this endpoint. | No change required, but worth documenting the "token = bearer" model in product docs so admins know forwarding rider emails is a soft attack vector. |
| **Low** | `webapi/Controllers/EventSubscriptionController.cs:109-130` (`Mine`) | Authenticated rider endpoint that calls `_tenantContext.TenantId` without `IsResolved` check. The `[Authorize]` policy doesn't enforce IsResolved. Would throw `InvalidOperationException` from `TenantContext.Tenant` if hit on the apex. | Add the standard `IsResolved` early-out. |
| **Low** | `webapi/Helpers/JwtHelper.cs:13` | `additionalClaims = null` as a nullable reference type without `?` annotation. Cosmetic. | Mark `Claim[]? additionalClaims = null`. |
| **Low** | `webapi/Controllers/MeController.cs:80-81` (`GetMyPurchases`) | The repo calls `_passes.GetForUser(userId, _tenantContext.TenantId)` and `_tickets.GetForUser(userId, _tenantContext.TenantId)` — good. But there's no auth-level check that the JWT's user is consistent with the resolved tenant: a rider's global token (`tenant_id` claim absent) is correctly scoped by the repo; a tenant-admin token whose `tenant_id` claim differs from the subdomain would silently see only the subdomain's tickets, which is the desired behavior but is incidental to the repo's `tenantId` argument. | None — current behavior is correct. Flagging for documentation: the security argument here rests on every repo method that returns rider-scoped data taking a `tenantId` and using it in the predicate. |

## Patterns worth replicating

- **`TenantResolutionMiddleware` running before auth** — gives `ITenantContext` to the authorization handlers so they can cross-check the `tenant_id` claim against the subdomain. This is the cornerstone of the multi-tenant security model and is implemented correctly.
- **`TenantPermissionHandler.HandleRequirementAsync`** — the explicit double check of (a) role-has-permission and (b) `tenant_id_claim == resolved_tenant_id` is exactly right. Other reviewers should point to this as the canonical pattern.
- **`UserController.Login` apex/subdomain split** — riders sign up globally and can use any tenant; tenant admins must be on their tenant's subdomain. The apex-login-is-super-admin-only branch (line 69-72) is a nice touch and prevents tenant-admin credentials from accidentally working at apex.
- **`UserController.RequestPasswordReset` "always 200, never leak"** — good defensive shape: returns the same response whether the email exists, only emails when SMTP is configured, logs the URL otherwise. The `BuildResetUrl` helper correctly directs tenant-scoped users back to their tenant subdomain.
- **`PurchaseController.ListForAdmin` reading from `v_recent_sales`** — single read model for "all sales kinds" with consistent tenant scoping (handled in the SQL view definition, presumably). Reviewers can point to this when arguing against per-table list endpoints.
- **`SuperAdminController.Bootstrap`** — the `AnySuperAdminExists` check turning the endpoint into a one-shot is the right shape; mirror this pattern for any future "first-run setup" endpoints.
- **`SeasonPassController.Reserve`** lines 370-378 — explicit double-check that the pass belongs to the user AND the pass.TenantId equals the resolved tenant. That's the pattern for any controller method that takes a row id and acts on it.

## Open questions

1. **Cookies vs `Authorization: Bearer`.** The codebase issues JWTs via API and presumably the Vue SPA stores them in `localStorage` (no cookie setup visible). That's intentional given the cross-subdomain SPA model, but it sacrifices `HttpOnly`/`Secure`/`SameSite` protection. Worth a deliberate design note: is the tradeoff acceptable, or should you move to per-subdomain cookies with a CORS-friendly auth flow?
2. **Service-charge mutation by SuperAdmin.** `UpdateTenantServiceCharge` lets a super admin change a tenant's effective revenue split. Should this require step-up auth (e.g., a 2FA confirmation) given the financial impact? Same question for `Impersonate` and `SendPayoutViaStripe`.
3. **Refresh tokens.** Discussed in the High finding above — decide whether you need them now (probably yes given 24h TTL) or whether shortening the TTL is enough for v1.
4. **`tenant_id` claim is informational only.** The architecture is robust because the *subdomain* (not the JWT) is the source of truth for the resolved tenant. The `tenant_id` claim only exists so the authz handlers can reject a tenant_admin issued at site A from acting on site B. Worth a comment in `TenantPermissionHandler` so future contributors don't try to "simplify" by dropping the claim check.
5. **Cross-tenant rider behavior.** Riders are global (no `TenantId`). When a rider buys passes at multiple tenants, every `MeController` endpoint scopes by `_tenantContext.TenantId`. Is there a planned "all my purchases across every track" page? If so, the design decision for that endpoint will be load-bearing for tenant isolation.
6. **Dispute handler in `PaymentController.HandleDispute`.** `tickets.FirstOrDefault()?.Id` is captured to `ticketId` only when `passes.Count == 0` (line 591). On a mixed-cart PI with passes AND tickets the dispute only links to the first pass — which is fine for the Dispute row but means the ticket isn't directly linked. The chargeback loop later iterates *both* passes and tickets, so ledger impact is correct, but the `Dispute` table reference is incomplete. Worth a follow-up: should `Dispute` have its own join table for multi-line PIs?
7. **`appsettings.json` ships with empty `Jwt:SigningKey` etc.** Correct — production gets these from env / user-secrets. But there's no startup validation that the key has minimum entropy (e.g., 32 bytes of base64). A weak key in production would silently work but be brute-forceable.

## Coverage notes

- I read every file in scope. The two largest controllers (`SuperAdminController` and `PurchaseController`) were read end-to-end, not spot-checked, because their tenant-scope surface is the most consequential.
- I did NOT re-read each repository implementation line by line — I grepped for `tenantId` parameter presence on the specific methods called from each controller action, and for `tenant_id =` predicates in the SQL strings of `PassPurchaseRepository`, `RentalRepository`, `EventExtraRepository`, `SeasonPassRepository`, `DisputeRepository`, `TenantLedgerRepository`, and `NotificationRepository`. A full repository-by-repository audit is a separate review section.
- `PaymentController.StripeWebhook` is anonymous (Stripe signs the body — verified in `_payments.VerifyAndParseWebhook`). I confirmed the signature check exists but did not deeply review the webhook secret rotation story.
- `Services/Audit/HttpContextAuditLogger.cs` was not in scope but is the obvious place to plumb the `impersonated_by` claim — flagging for a follow-up.
- Frontend (`vueapp`) was out of scope and not read.
