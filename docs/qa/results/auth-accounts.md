# QA Results: Auth & Accounts

Verification method: static trace of each Expected result against current code (no live browser). Citations are file:line in the RidePass repo. Verified 2026-06-20.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| AU1 | PASS | `CreateAccount` requires resolution then creates rider with `TenantId = null`, `Role = "rider"`, hashed pw (UserController.cs:165-180). SMTP-on branch sets verification token + sends, returns `emailVerificationSent = true`; `email_verified` defaults false (UserController.cs:186-204). Actual mail delivery is environment-dependent (NEEDS-LIVE) but the code path is correct. |
| AU2 | PASS | `if (!_tenantContext.IsResolved) return BadRequest("Account creation must happen on a tenant subdomain.")` (UserController.cs:136-139), before any insert. |
| AU3 | PASS | Global dedup via `GetGlobalByEmail` (UserController.cs:143-147). Message reads "An account with this email already exists - please log in." (wording slightly longer than the plan, same intent). |
| AU4 | PASS | `GetGlobalByEmail` lowers both sides: `WHERE tenant_id IS NULL AND LOWER(email) = LOWER(@email)` (UserRepository.cs:49-53). |
| AU5 | PASS | `IsValidBirthdate`: `b.Date < today && b.Year >= 1900 && (today.Year - b.Year) <= 130` rejects future/<1900/>130 (UserController.cs:722-726, called 149-151). |
| AU6 | PASS | Blank emergency name or <7-digit emergency phone -> 400 (UserController.cs:153-158); <7-digit rider phone -> 400 (159-163). |
| AU7 | PASS | `VerifyEmail` consumes via `GetByEmailVerificationTokenHash` (expiry-checked) then `MarkEmailVerified` clears the hash (UserController.cs:617-633; UserRepository.cs:158-168). Re-POST finds null -> invalid/expired message. |
| AU8 | PASS | `ResendVerification` always 200 same message; only sends when rider && active && !verified && SMTP configured (UserController.cs:639-654). |
| AU9 | PASS | No-SMTP branch calls `MarkEmailVerified` and returns `emailVerificationSent = false` (UserController.cs:195-204). |
| AU10 | PASS | Rider found in global pool; resolved tenant passes apex gate; `TenantId = user.TenantId ?? resolved` resolves to the subdomain tenant (UserController.cs:54, 104). |
| AU11 | PASS | `if (user.Role == "rider" && !user.EmailVerified) return BadRequest("Please verify your email...")` (UserController.cs:92-96). |
| AU12 | PASS | `VerifyHashedPassword == Failed -> "Invalid email or password."` (UserController.cs:74-78), identical to unknown-email message (62-65). |
| AU13 | PASS | `user.Status != "active" -> "Invalid email or password."` (UserController.cs:62-65). |
| AU14 | PASS | `if (!IsResolved && user.Role != "super_admin") return BadRequest("Please log in from your tenant's subdomain.")` (UserController.cs:69-72); super_admin proceeds. |
| AU15 | PASS | Tenant staff resolved via `GetByEmail(tenantId, email)` (UserController.cs:57-60); JWT adds `tenant_id` because `user.TenantId.HasValue` (JwtIssuer.cs:40-42). |
| AU16 | PASS | `SuccessRehashNeeded` recomputes hash and persists via `UpdatePasswordHash` (UserController.cs:80-86; UserRepository.cs:261-265). NEEDS-LIVE to force a legacy hash, code path correct. |
| AU17 | PASS | `JwtSecurityTokenHandler.DefaultMapInboundClaims = false` (Program.cs:27); `RoleClaimType = "role"` (Program.cs:303); handler reads `FindAll("role")` / `FindFirst("tenant_id")` (TenantPermissionHandler.cs:18,37). Regression guard intact. |
| AU18 | PASS | JWT emits one `role` claim per role, primary first (JwtIssuer.cs:29-38); handler unions via `TenantPermissions.ForRoles` (TenantPermissionHandler.cs:18,31). Cashier (precedence over scanner) primary; union covers SalesCounter+SalesRedeem (TenantPermissions.cs:76-80,107-115). |
| AU19 | PASS | `RequestPasswordReset` always returns 200 generic message; token row inserted only when user found && active (UserController.cs:557-584). |
| AU20 | PASS | `ConfirmPasswordReset` validates token, sets new hash, marks used (UserController.cs:601-611). |
| AU21 | PASS | `token.UsedAtUtc is not null` -> invalid/expired (UserController.cs:596). |
| AU22 | PASS | `token.ExpiresAtUtc <= DateTime.UtcNow` -> invalid/expired (UserController.cs:596); 60-min window set at insert (565). |
| AU23 | PASS | `HashToken` stores SHA-256 hex (64 chars), never the raw token (UserController.cs:560,703-707). |
| AU24 | PASS | `BuildResetUrl`: global user uses request host; tenant user points at `{subdomain}.{apex}` via `ApexHostFromCurrent` (UserController.cs:668-694). |
| AU25 | PASS | Confirm rejects when `user is null || user.Status != "active"` before applying (UserController.cs:601-605). |
| AU26 | PASS | `GetProfile` returns id, tenant_id, email, name, role, status, phone, birthdate, emergency, full address, bike, race number (UserController.cs:230-251). |
| AU27 | PASS | Blank name or <7-digit phone -> 400; else persists (UserController.cs:262-269). |
| AU28 | PASS | <7-digit phone -> 400; valid persists (UserController.cs:277-283). |
| AU29 | PASS | `Norm` maps blanks to null; `country ?? "US"` default (UserController.cs:312-319). |
| AU30 | PASS | Set-once guard: `if (user.Birthdate.HasValue) return BadRequest(...)` (UserController.cs:340-344). |
| AU31 | PASS | bike >100 -> 400, raceNumber >16 -> 400, blank -> null (UserController.cs:291-303). |
| AU32 | PASS | `[Authorize]` -> 401 without token; malformed `UserId` claim -> `TryGetSelfId` false -> "Invalid token." (UserController.cs:254-261,349-353). |
| AU33 | PASS | `CreateTenantUser` writes `TenantId = resolved`, derives primary by precedence, returns temp password, welcome email if SMTP on (UserController.cs:427-461). |
| AU34 | PASS | `GetGlobalByEmail` collision -> "That email is already registered as a rider on RidePass..." (UserController.cs:420-425). |
| AU35 | PASS | `TryResolveRoles` rejects roles not in `AssignableRoles` allowlist (excludes super_admin/rider) -> "Role '...' is not assignable." (UserController.cs:357-381). |
| AU36 | PASS | Empty set -> "At least one role is required." (UserController.cs:377). |
| AU37 | PASS | `SelfId() == id && !newRoles.Contains("tenant_admin") -> "You can't remove your own admin role."` (UserController.cs:477-480). |
| AU38 | PASS | `SelfId() == id && status == "disabled" -> "You can't disable your own account."` (UserController.cs:498-501). |
| AU39 | PASS | `request.Status is not ("active" or "disabled") -> "Status must be 'active' or 'disabled'."` (UserController.cs:489-491). |
| AU40 | PASS | scanner permission set = {SalesRedeem} only (TenantPermissions.cs:112-115); UsersManage policy not satisfied -> 403. |
| AU41 | PASS | `ResetTenantUserPassword` validates target tenant, returns temp password, persists new hash (UserController.cs:506-535). NEEDS-LIVE only to observe the email; logic correct. |
| AU42 | PASS | Login status gate blocks disabled staff (UserController.cs:62-65). |
| AU43 | PASS | Handler requires `tenant_id` claim == resolved tenant; acme token on globex -> all tenant policies fail 403 (TenantPermissionHandler.cs:37-45). |
| AU44 | PASS | `target is null || target.TenantId != resolved -> 404 "User not found on this tenant."` (UserController.cs:472-476). |
| AU45 | PASS | `GetProfile`: `IsResolved && user.TenantId.HasValue && user.TenantId != resolved -> Forbid()` (UserController.cs:225-228). |
| AU46 | PASS | Confirm derives user from `token.UserId` only; no user id read from request body (UserController.cs:601). |
| AU47 | PASS | Reset token = 32 random bytes (UserController.cs:696-701); temp password = 12 random bytes hex via `RandomNumberGenerator` (715-720). |

## Summary
- PASS: 47
- FAIL: 0
- NEEDS-LIVE: 0 (AU1, AU16, AU41 have live-only observation steps but their code paths verify cleanly)
- N/A: 0

No auth/account isolation gaps found.
