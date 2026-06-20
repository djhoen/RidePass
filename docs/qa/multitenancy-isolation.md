# QA Test Plan: Multi-tenancy & Tenant Isolation

> Scope: subdomain -> tenant resolution, the unresolved-tenant rejection on tenant-scoped endpoints, the per-tenant `tenant_id` scoping rule on every query, the JWT `tenant_id` cross-check, and adversarial cross-tenant read / write probes against events, tickets, passes, coupons, reports, and rider purchases. Cross-cutting. Last updated: 2026-06-20.

## Surface map
- **Resolution:** `webapi/Middleware/TenantResolutionMiddleware.cs` (subdomain -> `TenantContext`; `X-Tenant-Subdomain` header honored only in Development or when `Tenant:AllowApiClientTenantHeader` is on; unknown/inactive -> 404; unpublished -> 404 unless super_admin or member of that tenant; 5-minute cache).
- **Context:** `webapi/Multitenancy/ITenantContext.cs` + `TenantContext.cs` (`IsResolved`, `TenantId`, `Tenant`, `Subdomain`; reading `TenantId` while unresolved throws).
- **Pipeline order:** `Program.cs` `UseAuthentication` -> (`UseWhen` skip `/api/health`) `TenantResolutionMiddleware` -> `UseAuthorization`. Auth runs first so the unpublished-tenant gate can read the caller's claims.
- **Authorization cross-check:** `AuthPolicies/TenantPermissionHandler.cs` (super_admin bypass; else permission union must contain the requirement AND the JWT `tenant_id` claim must equal `_tenantContext.TenantId`).
- **Repository scoping pattern (representative):** `Services/Repositories/EventRepository.cs` (`GetById(id, tenantId)` -> `WHERE id = @id AND tenant_id = @tenantId`; UPDATE/DELETE same shape). `UserRepository.GetByEmail(tenantId, email)` is tenant-scoped; `GetGlobalByEmail` is deliberately global (`tenant_id IS NULL`).
- **Probe endpoints:** `EventController` (`GET /api/Event`, `GET /api/Event/{id}` family), `ReportsController` (`/api/Reports/Admin/EventRiders/{eventId}`), `MeController.GetMyPurchases` (`GET /api/Me/Purchases`), `MeController` coupons + ticket cancel, `PurchaseController` (`POST /api/Purchase/EventTicket`, anonymous public buy).
- **Per-tenant table catalog + global exceptions:** `.claude/skills/tenant-audit/SKILL.md`. Globally-scoped (no predicate): `tenant`, `users`, `super_admin`, `super_admin_session`, `event_subscription`.

## Concepts under test
- **One subdomain, one tenant.** `ExtractSubdomain` accepts only a single-level label under the configured `Tenant:RootDomain`; apex, `localhost`, IP literals, and multi-level prefixes (`foo.acme`) resolve to no tenant.
- **Unresolved means rejected.** Every tenant-scoped action (public ones included) must short-circuit with `if (!_tenantContext.IsResolved) return BadRequest("No tenant resolved.")` before touching tenant data. "Public" (`[AllowAnonymous]`) does not mean "tenant-optional"; it still needs a resolved subdomain.
- **Defence in depth, three layers.** (1) Middleware resolves and may 404. (2) `TenantPermissionHandler` requires the JWT `tenant_id` claim to match the resolved tenant, so a valid token for tenant A is useless on tenant B even with the right permission. (3) Every repository query carries `tenant_id = @tenantId`, so even a controller bug cannot read another tenant's rows by id.
- **Global identities cross tenants by design.** Riders and super_admins (`tenant_id NULL`) authenticate on any subdomain; their VISIBLE DATA is still scoped, e.g. `GetMyPurchases` filters by `(userId, resolvedTenantId)`, so the same rider sees only the current tenant's purchases.
- **Unpublished tenants are dark.** A not-yet-published tenant returns the same 404 as an inactive one to the public; only its own staff (matching `tenant_id` claim) or a super_admin can reach it.
- **Forged header is inert without a JWT.** When the API-client header is enabled, a forged `X-Tenant-Subdomain` cannot escalate because the permission handler still cross-checks the JWT `tenant_id`.

## Preconditions / test data
- Two active, published tenants on distinct subdomains: **acme** (`acme.<root>`) and **globex** (`globex.<root>`). One **unpublished** tenant: `beta.<root>`. The apex host `<root>`.
- In each of acme and globex: at least one published event with ticket tiers, one pass product, one coupon, and one rider purchase. RECORD the resource ids (event id, tier id, pass id, coupon id, ticket purchase id) per tenant for cross-probing.
- Accounts: a global rider with purchases at BOTH tenants; an acme tenant_admin; an acme tenant_scanner; a globex tenant_admin; a super_admin.
- An API client (or curl) that can set an arbitrary `Host` / `X-Tenant-Subdomain` and attach a chosen Bearer token.
- DB read access to confirm row counts per `tenant_id`.
- Note the environment: the `X-Tenant-Subdomain` header is honored only in Development or when `Tenant:AllowApiClientTenantHeader` is configured on; in plain production you must vary the real `Host`.

---

## Subdomain resolution

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MT1 [NN] | Subdomain resolves | `GET /api/Event?fromUtc=..&toUtc=..` on `acme.<root>` | 200 with acme events only; `TenantContext.Subdomain == acme`. |
| MT2 [NN] | Apex resolves to no tenant | Same call on apex `<root>` | Middleware sets no tenant; the endpoint returns 400 "No tenant resolved for this request." |
| MT3 [NN] | Unknown subdomain | Call on `doesnotexist.<root>` | 404 "Unknown or inactive tenant: doesnotexist". |
| MT4 [NN] | Inactive tenant | Set a tenant `status != active`, call its subdomain (allow up to the 5-min cache to clear) | 404 with the same message; existence not revealed. |
| MT5 [NN] | Unpublished tenant is dark to public | Anonymous request to `beta.<root>` | 404, identical message to inactive. |
| MT6 [NN] | Unpublished reachable by its own staff | Authenticated as beta's tenant_admin (JWT `tenant_id` = beta), call `beta.<root>` | Resolves (200 path); `MayAccessUnpublished` allows it. |
| MT7 [NN] | Unpublished reachable by super_admin | super_admin token to `beta.<root>` | Resolves; super_admin always allowed. |
| MT8 [NN] | Unpublished NOT reachable by other-tenant staff | acme tenant_admin token to `beta.<root>` | 404; `tenant_id` claim (acme) != beta. |
| MT9 [NN] | Multi-level subdomain rejected | Call `foo.acme.<root>` | No tenant resolved (prefix contains a dot); tenant-scoped endpoints 400 / platform routes proceed tenant-less. |
| MT10 [R] | localhost / IP apex | Call `localhost` and a raw IP host | Treated as apex (no tenant); no crash. |
| MT11 [R] | Header honored only where allowed | Send `X-Tenant-Subdomain: acme` with no subdomain host in (a) Development / API-client-on, (b) plain production | (a) resolves acme; (b) ignored, stays tenant-less. |
| MT12 [R] | Health route bypasses resolution | `GET /api/health` on apex and on a bad subdomain | Responds without requiring a tenant (UseWhen exclusion). |
| MT13 | Resolution cache TTL | Flip a tenant active->inactive, immediately re-call | May still resolve for up to 5 minutes (cache), then 404. Document the window; confirm it is acceptable for status changes. |

## Unresolved-tenant guards

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MT14 [NN] | Tenant-scoped GET needs resolution | `GET /api/Me/Purchases` on apex with a valid rider token | 400 "No tenant resolved." (guard before reading data). |
| MT15 [NN] | Public buy still needs resolution | `POST /api/Purchase/EventTicket` (anonymous) on apex | 400 "No tenant resolved." Confirms `[AllowAnonymous]` != tenant-optional. |
| MT16 [NN] | Public registration lookups need resolution | `GET /api/Purchase/EventTicket/Registration/{token}` on apex | 400 "No tenant resolved." |
| MT17 [NN] | Admin reports need resolution | `GET /api/Reports/Admin/Summary` on apex with an admin token | Resolution/authorization fails; no cross-tenant aggregate returned. |
| MT18 [R] | Sweep AllowAnonymous endpoints | Enumerate `[AllowAnonymous]` actions that read tenant data (Purchase EventTicket, CompleteRegistration, Registration lookup) and call each on apex | Every one returns the no-tenant 400 before any DB read. |

## Cross-tenant read isolation (adversarial)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MT19 [NN] | Read globex event from acme | On `acme.<root>` with acme tenant_admin, `GET /api/Reports/Admin/EventRiders/{globexEventId}` | 404 (`_events.GetById(id, acmeTenantId)` returns null; predicate `id AND tenant_id`). No globex rider data. |
| MT20 [NN] | Event list never bleeds | On acme, list events for a wide date range | Response contains only acme events; globex ids absent even though they share the table. |
| MT21 [NN] | Rider purchases are tenant-partitioned | As the dual-tenant rider, `GET /api/Me/Purchases` on acme, then on globex | Each call returns only that tenant's purchases (`GetForUser(userId, tenantId)`); the union is never returned on a single subdomain. |
| MT22 [NN] | Coupon read scoped | acme staffer `POST /api/Me/Coupons/{globexCouponId}/Share` (or any coupon GetById path) | "Coupon not found" (GetById is `(id, tenantId)`-scoped). |
| MT23 [NN] | Ticket detail scoped | acme staffer requests a globex ticket purchase by id (e.g. cancel/check-in path) | 404 "Ticket not found." (`_tickets.GetById(id, acmeTenantId)`). |
| MT24 [NN] | Pass product read scoped | On acme, fetch a globex `pass_product` id through the relevant endpoint | Not found; pass queries carry `tenant_id`. |
| MT25 | Reports aggregate isolation | Compare `Reports/Admin/Summary` totals on acme vs DB `SELECT ... WHERE tenant_id = acme` | Numbers match acme only; globex sales never counted. |

## Cross-tenant write isolation (adversarial)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MT26 [NN] | Mutate globex event from acme | acme tenant_admin attempts edit/delete of a globex event id | 404 / no-op; UPDATE/DELETE carry `id AND tenant_id`, so zero rows affected. Confirm globex row unchanged in DB. |
| MT27 [NN] | Check-in another tenant's rider | acme scanner `PUT /api/Reports/Admin/EventRiders/{globexPurchaseId}/CheckIn` | Rejected / not found; no globex purchase state change. |
| MT28 [NN] | Set race number cross-tenant | acme staffer sets race number on a globex ticket purchase id | No-op (`SetRaceNumber(id, acmeTenantId, ...)`); globex row unchanged. |
| MT29 [NN] | Refund a globex sale | acme user with SalesRefund calls `POST /api/Purchase/Refund` for a globex purchase id | Not found / rejected; no refund issued on globex. |
| MT30 | Manage a globex staff user | acme tenant_admin `PUT /api/User/Tenant/{globexUserId}/Role` | 404 "User not found on this tenant." (target `tenant_id` mismatch). |

## JWT tenant cross-check

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MT31 [NN] | Acme token used on globex | Log in as acme tenant_admin; take that token and call a `UsersManage` endpoint on `globex.<root>` | 403. Handler: permission present but `tenant_id` claim (acme) != resolved tenant (globex). |
| MT32 [NN] | Forged header cannot escalate | With API-client header enabled, send acme staffer's token but `X-Tenant-Subdomain: globex` | 403; resolved tenant becomes globex, JWT cross-check fails. |
| MT33 [NN] | Super_admin acts cross-tenant | super_admin token calls an acme admin endpoint, then a globex one | Both succeed (super_admin bypass), and each call's data stays scoped to the resolved tenant. |
| MT34 [NN] | Token with no tenant_id on staff endpoint | A rider token (no `tenant_id` claim) calls a tenant policy endpoint on acme | 403; handler cannot parse a tenant claim. |
| MT35 [R] | Permission union still tenant-bound | Multi-role acme staffer on globex | Even with a broad permission union, all tenant policies fail on globex (tenant mismatch). |

---

## Edge & adversarial

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MT36 | Unresolved TenantId throws, not leaks | Find any tenant-scoped action lacking an `IsResolved` guard (regression hunt; run `/tenant-audit` on recent diffs) | A missing guard should surface as a 500 from `TenantContext.TenantId` throwing, never a query with a default/empty tenant id. Any action that reaches a repo with `Guid.Empty` is a finding. |
| MT37 | Global-table queries are intentionally unscoped | Confirm `users`, `tenant`, `super_admin`, `event_subscription` lookups do NOT add a tenant predicate | These are the documented global exceptions; isolation for `users` comes from `tenant_id NULL` for global accounts plus the `(tenantId, email)` overload for staff. |
| MT38 | Case-insensitive staff email per tenant | Create `Staff@x.com` on acme; attempt the same casing variations on globex | Allowed on globex (different tenant); acme dedup is `(tenant_id, LOWER(email))`. Confirms scoping does not over-block across tenants. |
| MT39 | Child-row scoping via parent | Probe a child entity (e.g. survey_question, event_extra) by id from the wrong tenant | Blocked because the controller verifies the parent's tenant before touching children; confirm no id-only child query exists. |
| MT40 | Shared subdomain cache poisoning | Hit `acme` then immediately `globex` on the same warm process | No bleed; cache key is `tenant:{subdomain}` and context is per-request scoped. |

## Known risks / watch-items
- **IsResolved guard is per-action, not enforced centrally.** There is no global filter that rejects unresolved-tenant requests; each controller must include the guard. A newly added tenant-scoped action that forgets it will throw a 500 (from `TenantContext.TenantId`) rather than fail cleanly, and a worse variant could pass a default id. The `/tenant-audit` skill (run on every backend diff) is the primary defense; MT36 is the manual backstop.
- **5-minute resolution cache (MT13).** Activation, deactivation, publish, and rename of a tenant take up to 5 minutes to take effect. Confirm this is acceptable for go-live and suspension flows, or add a cache bust on those admin actions.
- **Header trust surface (MT11, MT32).** `Tenant:AllowApiClientTenantHeader` widens the resolution input to a client-supplied header. Safe only because the JWT `tenant_id` cross-check holds. Verify the flag is OFF in any environment where the mobile cashier app is not in use, and that the cross-check is never bypassed for anonymous endpoints that mutate data.
- **Anonymous-but-tenant-scoped endpoints (MT15, MT18).** The public purchase/registration endpoints rely solely on the `IsResolved` guard plus per-query `tenant_id` for isolation (no JWT). A regression that drops the guard on one of these is the highest-impact leak path. Keep MT18 as a recurring sweep.
- **Global rider data partitioning (MT21).** A rider's purchases are split across tenants by the resolved subdomain. Confirm product intent: there is deliberately no "all my passes across every track" view on a tenant subdomain.
- See the **Auth & Accounts** plan for login pool resolution, the `DefaultMapInboundClaims = false` claim-survival guard, and tenant staff provisioning.
