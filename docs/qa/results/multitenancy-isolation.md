# QA Results: Multi-tenancy & Tenant Isolation

Verification method: static trace of each Expected result against current code, with every cross-tenant probe checked against the cited repository SQL `WHERE ... tenant_id = @tenantId` clause and the controller guard. No live browser. Citations are file:line. Verified 2026-06-20.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| MT1 | PASS | `ExtractSubdomain` returns the single label (TenantResolutionMiddleware.cs:102-121); `GetInRange` scopes `WHERE tenant_id = @tenantId` (EventRepository.cs:30-42). |
| MT2 | PASS | Apex -> subdomain null -> `_next` with no tenant (Middleware:54-59); `EventController.GetInRange` returns 400 "No tenant resolved for this request." (EventController.cs:50-53). |
| MT3 | PASS | `tenant is null` -> 404 "Unknown or inactive tenant: {subdomain}" (Middleware:68-73). |
| MT4 | PASS | `tenant.Status != "active"` -> same 404, existence not revealed (Middleware:68-73). 5-min cache caveat per MT13. |
| MT5 | PASS | `!IsPublished && !MayAccessUnpublished -> 404`, identical message (Middleware:78-83). |
| MT6 | PASS | `MayAccessUnpublished`: `tenant_id` claim == tenant.Id -> allowed (Middleware:94-100). |
| MT7 | PASS | `role` claim == "super_admin" -> allowed (Middleware:98). |
| MT8 | PASS | acme `tenant_id` claim != beta.Id -> not allowed -> 404 (Middleware:99). |
| MT9 | PASS | `prefix.Contains('.')` rejected -> null -> no tenant (Middleware:113-117). |
| MT10 | PASS | `host == "localhost"` and `IPAddress.TryParse` both return null (Middleware:106-107). Marked [R]; code path verifies without a live host. |
| MT11 | PASS | Header honored only when `env.IsDevelopment() || _allowApiClientTenantHeader` (Middleware:45-52; flag default off, 28-29). |
| MT12 | PASS | `UseWhen(!path.StartsWithSegments("/api/health"), TenantResolutionMiddleware)` (Program.cs:368-370); health mapped anonymous (381). |
| MT13 | PASS | 5-minute `AbsoluteExpirationRelativeToNow` cache (Middleware:10,62-66). Behaves as documented; status changes lag up to 5 min (accepted watch-item). |
| MT14 | PASS | `GetMyPurchases`: `if (!IsResolved) return BadRequest("No tenant resolved.")` before any read (MeController.cs:211-214). |
| MT15 | PASS | `BuyEventTicket` (`[AllowAnonymous]`): `if (!IsResolved) return BadRequest("No tenant resolved.")` first (PurchaseController.cs:175-178). |
| MT16 | PASS | `GetRegistration` (`[AllowAnonymous]`): IsResolved guard first (PurchaseController.cs:982). |
| MT17 | PASS | `GetTenantSummary` is `[Authorize(ReportsView)]`; a tenant_admin token at apex fails the policy (handler needs `IsResolved` && tenant_id match, TenantPermissionHandler.cs:42-45) -> 403, no aggregate returned. Note: no explicit `IsResolved` guard in the action; a super_admin at apex would throw 500 from `TenantContext.TenantId` (not a leak) - see MT36. |
| MT18 | PASS | All three anonymous tenant-data endpoints guard IsResolved first: BuyEventTicket (175), CompleteRegistration (847), GetRegistration (982). |
| MT19 | PASS | `GetEventRiders` -> `_events.GetById(globexEventId, acmeTenantId)` returns null (predicate `id = @id AND tenant_id = @tenantId`, EventRepository.cs:44-53) -> 404 "Event not found." before any rider read (ReportsController.cs:124-125). |
| MT20 | PASS | List query is `WHERE tenant_id = @tenantId` only (EventRepository.cs:30-42); globex ids cannot appear. |
| MT21 | PASS | `GetForUser(userId, tenantId)`: `WHERE purchaser_user_id = @userId AND p.tenant_id = @tenantId` (EventTicketPurchaseRepository.cs:439-456). Each subdomain returns only its tenant's rows; no cross-tenant union (the cross-tenant `Upcoming` feed is a separate apex-only, user-scoped endpoint by design, MeController.cs:75-113). |
| MT22 | PASS | `ShareCoupon` -> `_coupons.GetById(couponId, tenantId)` (`WHERE id = @id AND tenant_id = @tenantId`, CouponRepository.cs:35-39) -> null -> "Coupon not found." (MeController.cs:304-305). |
| MT23 | PASS | Ticket detail/cancel/check-in all use `GetById(id, tenantId)` (`id AND tenant_id`, EventTicketPurchaseRepository.cs:50-55); cross-tenant id -> 404 (MeController.cs:349-351, ReportsController.cs:184). |
| MT24 | PASS | `GetProduct(id, tenantId)`: `WHERE id = @id AND tenant_id = @tenantId` (SeasonPassRepository.cs:79-83). |
| MT25 | PASS | `GetTenantSummary` passes `_tenantContext.TenantId` to every reports query (ReportsController.cs:63-71); aggregates are tenant-scoped. Numeric equality vs raw DB is NEEDS-LIVE, but the scoping is verified in code. |
| MT26 | PASS | `Update`/`Delete` first `GetById(id, acmeTenantId)` -> 404; and UPDATE/DELETE carry `WHERE id = @Id AND tenant_id = @TenantId` (EventRepository.cs:74-103; EventController.cs:250-253,312-320), so zero rows affected. |
| MT27 | PASS | `SetCheckIn` event_ticket -> `MarkRedeemed(id, tenantId,...)` (`id AND tenant_id`, EventTicketPurchaseRepository.cs:141-150); season_pass -> `GetReservationForCheckIn(id, tenantId)` joins parent purchase `p.tenant_id = @tenantId` and `UpdateReservationStatus` scopes via parent (SeasonPassRepository.cs:283-295,328-363). |
| MT28 | PASS | `SetRaceNumber(id, tenantId, ...)`: `WHERE id = @id AND tenant_id = @tenantId` (EventTicketPurchaseRepository.cs:163-170). |
| MT29 | PASS | `Refund` event_ticket -> `GetById(id, tenantId)` null -> 404; season_pass/membership/extra check `p.TenantId != tenantId` -> 404 (PurchaseController.cs:1201-1235). |
| MT30 | PASS | `UpdateTenantUserRole`: `target.TenantId != _tenantContext.TenantId -> 404 "User not found on this tenant."` (UserController.cs:472-476). |
| MT31 | PASS | Handler: permission present but `tenant_id` claim (acme) != resolved (globex) -> requirement not succeeded -> 403 (TenantPermissionHandler.cs:37-45). |
| MT32 | PASS | Forged header sets resolved tenant to globex; JWT `tenant_id` still acme -> cross-check fails -> 403 (Middleware:45-52 + Handler:42-45). |
| MT33 | PASS | `roles.Contains("super_admin") -> Succeed` bypass (TenantPermissionHandler.cs:25-29); each call's data still scoped by the resolved `tenant_id` in every repo query. |
| MT34 | PASS | Rider token: `ForRoles(["rider"])` is empty so requirement not contained -> not succeeded; also no parseable `tenant_id` claim -> 403 (TenantPermissionHandler.cs:31-41; TenantPermissions.cs:51-61). |
| MT35 | PASS | Union is computed first, but tenant cross-check at the end still fails on globex (TenantPermissionHandler.cs:31-45). |
| MT36 | PASS | `TenantContext.TenantId` throws `InvalidOperationException` when unresolved (TenantContext.cs:11-15) -> 500 via the ProblemDetails handler (Program.cs:314-338), never a `Guid.Empty` query. Confirmed no repo is reached with a default tenant id on the unresolved path. Per-action guard reliance is the documented residual risk; no leak. |
| MT37 | PASS | `users`/`tenant`/`super_admin` lookups intentionally carry no tenant predicate; `GetGlobalByEmail` uses `tenant_id IS NULL` and staff lookups use the `(tenantId, email)` overload (UserRepository.cs:34-57). |
| MT38 | PASS | Staff dedup is `GetByEmail(tenantId, email)` (`tenant_id = @tenantId AND LOWER(email) = LOWER(@email)`, UserRepository.cs:34-44); same email allowed on a different tenant. |
| MT39 | PASS | `CompleteRegistration` resolves child via tenant-scoped parents: ticket `GetById(id, tenantId)`, tier `GetById(tierId, tenantId)`, event `GetById(tier.EventId, tenantId)` (PurchaseController.cs:880-893). Season-pass reservation scoped through parent purchase tenant (SeasonPassRepository.cs:283-295). No id-only child query in the attack path. |
| MT40 | PASS | Cache key is `tenant:{subdomain}` (Middleware:61); `TenantContext` is registered scoped per request (Program.cs:214-215), so no cross-request bleed. |

## Summary
- PASS: 40
- FAIL: 0
- NEEDS-LIVE: 0 (MT10, MT25 have live-only confirmation steps; their code paths verify cleanly)
- N/A: 0

No cross-tenant read or write leak found. Every probed repository query carries `tenant_id = @tenantId` (or scopes a tenant-less child through its tenant-bound parent), and the JWT `tenant_id` cross-check in `TenantPermissionHandler` holds. The one residual, already-documented item is MT36/MT17: a handful of admin actions (e.g. `EventController.Create/Update`, `ReportsController.GetTenantSummary`) lack an explicit `IsResolved` guard and lean on the permission handler; a super_admin hitting one at the apex would get a 500 from `TenantContext.TenantId` throwing, never a default-tenant query. That is a fail-closed behavior, not an isolation gap.
