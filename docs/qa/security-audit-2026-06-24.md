# RidePass Security Audit — 2026-06-24

Scope: full `webapi` controller surface (48 controllers), the `Services/Repositories` query layer, auth/JWT/CORS config, multitenancy middleware, payments, storage, sync, and workers. Method: 6 parallel read-only auditors running the `tenant-audit` + `self-code-review` OWASP checklist, tracing each by-id/list endpoint into its actual repository SQL. Branch: `master` (note: 62-file uncommitted diff present at audit time, mostly Stripe payments).

**Overall posture: strong.** Tenant isolation in the query layer is broadly excellent, the SQL-injection sweep came back clean across the whole repository layer, Stripe webhooks are signature-verified, refunds are clamped and idempotent, and there are no hardcoded secrets or unsafe deserializers. The findings below are real but mostly bounded; one is a confirmed cross-tenant leak that should be fixed before anything else.

---

## HIGH

### H1. Cross-tenant dashboard leak — `DashboardController` reads any tenant's data
**STATUS: FIXED 2026-06-24** — `GetSnapshot` now requires the JWT `tenant_id` to equal the resolved tenant (super_admin bypassed), returning 403 otherwise, mirroring `TenantPermissionHandler`. Also switched perms to union all `role` claims. Build green.

`webapi/Controllers/DashboardController.cs:13` (class `[Authorize]`) + `:45-141` (`GetSnapshot`) — **CONFIRMED (verified by hand) — OWASP A01 Broken Access Control / cross-tenant IDOR.**

Dashboard is the one controller in the per-tenant set gated by bare `[Authorize]` instead of a `TenantPermission` policy. It computes capabilities from the JWT `role` claim but **never verifies the token's `tenant_id` matches the subdomain-resolved tenant**. The cross-check that protects every other endpoint lives in `TenantPermissionHandler.cs:42` (`_tenantContext.TenantId != claimTenantId → fail`), which Dashboard bypasses. `TenantResolutionMiddleware.cs:78-99` only cross-checks `tenant_id` for *unpublished* tenants, and `Program.cs:223-232` registers no `FallbackPolicy`.

**Impact:** a `tenant_admin`/`tenant_manager`/`tenant_accountant` of Tenant A points their valid JWT at `https://tenantB.ridepass.io/api/Dashboard/Snapshot` and receives Tenant B's revenue (`:82-103`), recent purchases including purchaser names (`:115-127`, from `v_recent_sales`), open-dispute count (`:132`), and pending-refund count (`:141`). The underlying queries are correctly scoped to the resolved tenant — which is why they return the wrong tenant's data. Not browser-CORS-mitigated (server-to-server bearer call).

**Fix (minimum):** before computing `perms`, require `User.FindFirst("tenant_id")?.Value == _tenantContext.TenantId.ToString()` (allowing `super_admin`).
**Fix (durable, recommended):** add a global authorization requirement / middleware that, for any authenticated request on a resolved tenant, enforces token-tenant == resolved-tenant (except `super_admin`). The whole app currently relies on each policy handler to perform this check individually; a defense-in-depth global guard closes Dashboard and any future bare-`[Authorize]` controller in one place.

---

## MEDIUM

### M1. Unreconciled card-present charge path — `CounterController.CreateCardPresentPaymentIntent`
`webapi/Controllers/CounterController.cs:942` — CONFIRMED — A04 Insecure Design / audit gap. Takes the charge amount straight from the client (`req.AmountCents`) and creates a real auto-capture card-present PaymentIntent, but writes **no purchase row and no ledger entry** (`StripePurchaseFinalizer.cs:112-116` bails on the unknown PI). A `SalesCounter` cashier can capture an arbitrary amount off a physical card with zero sale/ledger record (in direct mode it lands in the tenant's Stripe account with no app fee). Gated to `SalesCounter` + needs a physical tap. **Fix:** remove this "validation milestone" endpoint before production, or make it create a `pending` sale + ledger row like `ConcessionController.CreateSale`; at minimum guard behind a non-prod build flag.

### M2. Public event calendar leaks drafts — `EventController.GetInRange`
`webapi/Controllers/EventController.cs:47` — CONFIRMED — A01. No `[Authorize]` (so public) and no status filter (`EventRepository.cs:30-42` has no `status` predicate), so anonymous visitors receive `draft` and `cancelled` events with full detail. The sibling `GetPublic` (`:142`) deliberately 404s non-`scheduled` events. **Fix:** require `CatalogManage` if admin-only, else filter to `status='scheduled'`. (Tenant-scoping itself is correct.)

### M3. Anonymous phone harvest — `EventSubscriptionController.StatusByEmail`
`webapi/Controllers/EventSubscriptionController.cs:89` — CONFIRMED — A01/A04. Anonymous `GET /Status?email=` returns the `Phone` (and `Email`) on file for any email at the tenant. Enumeration + PII harvest. **Fix:** return only the `Subscribed` boolean, or require a signed ownership token.

### M4. Fixed/reused IV in AES-256-CBC — `EncryptionHelper`
`Services/Helpers/EncryptionHelper.cs:25-33,93-106` — CONFIRMED — A02. One IV configured at startup and reused for every encryption (per-tenant Twilio auth tokens). CBC with a constant IV leaks plaintext equality and provides no integrity (no MAC). **Fix:** random IV per encryption prepended to ciphertext; prefer AES-GCM.

### M5. Stored XSS via SVG upload (local-disk storage path)
`webapi/Storage/LocalFilesystemImageStorage.cs` + `Program.cs:358` (`UseStaticFiles`) — SUSPECTED — A03/A05. `image/svg+xml` is whitelisted and `/uploads/*` is served inline same-origin with no `X-Content-Type-Options: nosniff` and no `Content-Disposition`. A `catalog.manage` user can upload an SVG with inline JS that runs same-origin on the tenant subdomain (escalation toward an admin/super-admin who views it). Reduced in prod when `SpacesImageStorage` serves from a separate origin; the disk path is same-origin. **Fix:** drop SVG from the allow-list (or sanitize server-side) and serve uploads with `nosniff` + `Content-Disposition: attachment`.

### M6. TenantSync shared key — same secret prod+stage, optional IP allowlist
`webapi/Sync/TenantSyncAuthAttribute.cs:41-49` — SUSPECTED (deployment-config dependent) — A07/A01. When `TenantSync:AllowedIps` is empty the IP check is skipped and auth is key-only; the same symmetric key is provisioned on prod and stage. If the lower-trust stage key leaks (or AllowedIps is unset on prod), an attacker can pull any *unpublished* tenant's full config bundle from prod via `Export/{id}`. Mitigations present: export refuses published tenants, key compare is constant-time from config. **Fix:** treat `AllowedIps` as mandatory (fail closed when empty) and use separate keys per direction.

### M7. Impersonation not audited — `SuperAdminController.Impersonate`
`webapi/Controllers/SuperAdminController.cs:502-544` — CONFIRMED — A09. Mints a 1-hour JWT as another user with no `_audit.Log` entry, while nearly every other action in the controller audits. Token carries `impersonated_by` (good) and refuses to impersonate another super_admin, but there's no server-side record of who impersonated whom. **Fix:** add an audit log entry before returning the token.

### M8. Public survey submit — no rate limit, no dedup, uncapped text
`webapi/Controllers/SurveyController.cs:451` — CONFIRMED — A04/API4. Public `POST Public/{token}/Submit` lets one client insert unlimited responses with uncapped free-text, polluting admin results and growing the table. Correctly tenant-scoped; results are admin-only (no data leak). **Fix:** rate-limit the public path + cap free-text length.

---

## LOW (hardening — batchable)

- **Auth-endpoint rate limiting** (`UserController` Login `:61`, `EmailExists` `:166`, `RequestPasswordReset` `:685`, `ResendVerification`, `CreateAccount`): no throttling → credential brute-force, account enumeration, email bombing, mass account creation. Add IP/email-partitioned limiter (+ CAPTCHA where appropriate). — A07
- **Password-reset token logged** (`UserController.cs:720`): full reset URL with live token written to Warning log when SMTP unconfigured. Log only the user id. — A09
- **Multiple live reset tokens** (`UserController.cs:706`): new reset doesn't invalidate prior outstanding tokens. Expire existing before insert. — A07
- **Apex login enumeration side channel** (`UserController.cs:80`): distinct message for a known non-super-admin email. Return the generic message. — A07
- **Profile photo content-type trust** (`UserController.cs:419`): type gated only on spoofable `file.ContentType`. Sniff magic bytes or re-encode. — A04
- **Public write spam** — `FeedbackController.cs:32`, `NewsletterController.cs:39`: unauthenticated, unrate-limited, uncapped body. Add rate limit + length cap. — API4
- **Anonymous waiver fetch** (`WaiverController.cs:53`): `GET /Waiver/{id}` returns any waiver (incl. draft/retired) in the tenant to anonymous callers (tenant-scoped, GUID-keyed). Consider `[Authorize]` or active-only. — API1
- **LoamPass link code brute-force** (`RiderLoampassController.cs:87`): `LinkConfirm` proxies a 6-digit code with no RidePass-side rate limit; throttle/expiry/single-use depend on LoamMx. Confirm LoamMx enforces lockout, else add a per-user limiter. — A07
- **`ClaimHelper.ClaimHasRole` dangerous substring match** (`webapi/Helpers/ClaimHelper.cs:7-24`): `"super_admin".Contains("admin")` is true — but it's DEAD CODE (no caller). Delete it so it can't be wired up. — A01
- **`FindFirst("role")` vs `FindAll("role")` inconsistency** (`SuperAdminRequirement.cs:14`, `TenantResolutionMiddleware.cs:98`, `DashboardController.cs:50`): super-admin/dashboard read only the first role claim; tenant handlers union all. Fails closed today (JwtIssuer emits primary first); use `FindAll` for consistency. — A01
- **No HSTS / security headers** (`Program.cs:352-355`): no `UseHsts()`, no `nosniff`/`X-Frame-Options`/CSP. Likely at nginx; confirm (relevant to M5). — A05
- **Zip-slip in promotion disk branch** (`Sync/TenantPromotionService.cs:191-205`): bundle entry name → local path without `..`/`uploads/` validation. M2M-auth'd, self-originated bundles, prod uses S3 branch. Validate key prefix. — A03
- **JWT hardening** (`Program.cs:297-307`): set `ValidateLifetime = true` explicitly, consider a shorter access-token lifetime + refresh/revocation, and assert `Jwt:SigningKey` ≥ 32 bytes at startup. — A07
- **Newsletter email case duplication** (`NewsletterRepository.cs:53`): `ON CONFLICT (tenant_id, email)` is case-exact but `GetByEmail` matches `LOWER(email)` → differently-cased duplicates, one survives unsubscribe. Normalize to lowercase on write (or index on `lower(email)`). — correctness / postgres-case-insensitive
- **RiderLoampassLink upsert drops tenant_id** (`RiderLoampassLinkRepository.cs:35-41`): `DO UPDATE` pins the row to the first track; not a leak (reads are tenant-scoped), but blocks multi-track linking. — correctness

---

## Confirmed clean / notable positives

- **Tenant isolation (query layer):** every by-id GET/UPDATE/DELETE and every list/report traced carries a `tenant_id` predicate (or a prior tenant-scoped fetch + ownership check). Token-based global lookups (unsubscribe/confirm/redemption/invite tokens) are followed by explicit `entry.TenantId != _tenantContext.TenantId` checks. The *one* gap is H1 (Dashboard), which fails at the controller gate, not the query.
- **SQL injection:** repository-wide sweep CLEAN. Every dynamic SQL fragment is a compile-time constant, a bool/null-toggled WHERE clause, or a whitelisted ORDER BY/column; all user values are Dapper `@parameters`.
- **Payments:** charge amounts server-computed from DB; refunds clamped to captured amount and idempotency-keyed; Stripe webhooks verify the signature with separate platform vs Connect secrets and reject when unset.
- **Auth model:** forged `X-Tenant-Subdomain` header is NOT exploitable for policy-gated endpoints — `TenantPermissionHandler`/`TenantAdminHandler` cross-check the JWT `tenant_id` against the resolved tenant. Password hashing uses `PasswordHasher<User>`; `DefaultMapInboundClaims = false`. Reset/verify tokens are 256-bit CSPRNG, SHA-256 at rest, expiry-checked, single-use.
- **Config:** no hardcoded secrets (committed appsettings ship empty; fail-fast at startup); CORS scoped to apex + its https subdomains; Swagger and dev exception page are Development-only; prod returns ProblemDetails with no stack trace; no unsafe deserializers anywhere.

---

## Suggested fix order
1. **H1** (cross-tenant dashboard leak) — fix now, ideally with the global token-tenant guard.
2. **M1** (unreconciled card-present path) — before production.
3. **M2, M3** (anonymous draft/PII exposure) — quick, high-value.
4. **M4, M5, M6, M7, M8** — next hardening pass.
5. **LOW** batch — a rate-limiting pass on the public/auth endpoints covers several at once.
