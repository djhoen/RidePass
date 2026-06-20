# QA Test Plan: LoamPass Credit Integration

> Scope: a super-admin flags a RidePass track with a LoamMx destination; riders link their LoamMx account by email + code, then redeem one count-based credit for a $0 race entry; admin refund un-redeems the credit. Last updated: 2026-06-20.

## Surface map
- **Super-admin setup:** `SuperAdminController.UpdateTenant` writes `tenant.loampass_mx_destination_id` via `TenantRepository.UpdateAdminDetails` (NULL = not a LoamPass track). `EventTypeController.SetLoampassRedemption` (`PUT api/EventType/{id}/LoampassRedemption`, policy `CatalogManage`) toggles `tenant_event_type.allow_loampass_redemption`; **practice is forced on in app code** and cannot be turned off.
- **Rider linking:** `RiderLoampassController` (all `[Authorize]`): `GET Status`, `POST LinkStart` (email -> LoamMx emails a 6-digit code), `POST LinkConfirm` (email + code -> stores `rider_loampass_link`), `DELETE ?accountId=` (unlink). Frontend `vueapp/src/services/LoampassLinkService.ts`.
- **Redeem:** `PurchaseController.RedeemLoampassForTicket` (`POST api/Purchase/EventTicket/RedeemLoampass`, `[Authorize]`). Capacity recheck + dedupe + $0 pending row under advisory lock `event-capacity:{eventId}`, then a network redeem; records `loampass_redemption` and a $0 `tenant_ledger_entry` (`loampass_credits`).
- **Gate check-in (related, not a credit spend):** `RiderLoampassController.GateCheckIn` (policy `SalesRedeem`) scans a Loam Pass QR and flips an EXISTING paid `race_entry` to `redeemed`. Never spends a credit.
- **Un-redeem / refund:** `PurchaseController.Refund` (`POST api/Purchase/Refund`, policy `SalesRefund`) calls `_loampass.RefundAsync(idempotencyKey)` (LoamMx `Unredeem`) and `LoampassRedemptionRepository.MarkRefunded` for `payment_method = 'loampass_credits'`.
- **M2M client (outbound only):** `Services/LoamPassMx/LoamPassMxService.cs` calls LoamMx `/RidePassIntegration/*` with header `X-Api-Key`. Endpoints used: `VerifyStart`, `VerifyConfirm`, `Credits`, `Redeem`, `Unredeem`, `PassOwner`. `IsConfigured` requires both `LoamPassMx:BaseUrl` and `LoamPassMx:ApiKey`. RidePass exposes **no** inbound LoamMx endpoint here; all rider routes are JWT-authed.
- **Repos / data:** `RiderLoampassLinkRepository`, `LoampassRedemptionRepository`, `EventTicketPurchaseRepository`.
- **Migrations:** `Script0110_LoampassMx.sql` (tenant + event-type columns, `loampass_credits` payment-method check constraints), `Script0111_RiderLoampassLink.sql`, `Script0112_LoampassRedemption.sql`.

## Concepts under test
- A **participating track** has a non-empty `loampass_mx_destination_id`. The rider `Status` endpoint reports `trackParticipates` from that flag.
- A RidePass rider may link **many** LoamMx accounts (1:many). The unique index `uk_rider_loampass_link_user_account (user_id, loampass_account_id)` makes re-linking the same account a no-op refresh of email + timestamp. `Status.creditsAvailable` is the **sum** of credits across all linked accounts at this destination.
- **Redeem eligibility** requires all of: participating track, at least one linked account, an active `race_entry` tier, an event with `status = 'scheduled'`, an event type that accepts Loam Pass (practice always; others per the toggle), and any required rider waiver signed.
- One credit = one admission. The redeem records the entry **paid at $0** (`amount_cents = 0`, `service_charge_cents = 0`, `payment_method = 'loampass_credits'`); the track is reimbursed off-platform.
- **Idempotency** is keyed on the RidePass `purchaseId` (used as the LoamMx `idempotencyKey`). A retry of the same purchase cannot double-spend; the in-lock `HasActiveRaceEntry` dedupe guards a client double-submit (which would otherwise mint two purchase ids).
- A redeemed credit is restored only by the **admin Refund** path (`Unredeem`). One `loampass_redemption` row per ticket (`event_ticket_purchase_id` is UNIQUE), with status `redeemed` -> `refunded`.

## Preconditions / test data
- A super-admin login, plus a tenant admin with `CatalogManage`, `SalesRefund`, and `SalesRedeem` permissions.
- The LoamMx partner API reachable from the test environment with valid `LoamPassMx:BaseUrl` + `LoamPassMx:ApiKey` (so `IsConfigured` is true). A way to toggle/break it for the unreachable cases (bad key or blocked host).
- Two RidePass rider accounts (Rider A, Rider B) on a participating tenant; one rider account on a **non**-participating tenant for isolation checks.
- LoamMx test accounts: Account-1 (>= 2 credits at the destination), Account-2 (0 credits), Account-3 (>= 1 credit) for the multi-account draw. A known email + the live 6-digit code flow.
- One `scheduled` practice event and one `scheduled` race event with a `race_entry` tier (one with finite `inventory`, one with none), plus a spectator/gate tier for regression. A race-event ladder class with two steps for the cross-step dedupe case.

---

## Super-admin setup

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| LP1 [NN] | Flag a track participating | Super-admin opens a tenant, sets a LoamMx destination id, saves | `tenant.loampass_mx_destination_id` persists; rider `Status` returns `trackParticipates = true`. |
| LP2 [NN] | Unflag a track | Clear the destination id, save | Flag goes NULL; `Status` returns `trackParticipates = false`; `RedeemLoampass` rejects with "This track doesn't accept Loam Pass credits." |
| LP3 [NN] | Event-type opt-in toggle | Admin (`CatalogManage`) toggles `LoampassRedemption` on a non-practice event type on/off | `allow_loampass_redemption` flips; redeem on that type's event allowed only when on. |
| LP4 [NN] | Practice forced on | Admin tries to turn Loam Pass OFF for the `practice` event type | Server coerces `allow = true` (practice can't be disabled); response shows accepted. Confirm `Script0110` also backfilled existing practice rows to true. |
| LP5 [R] | Permission gate on toggle | A user lacking `CatalogManage` calls the toggle | 403 / rejected. |
| LP6 | Super-admin only sets destination | Confirm only the super-admin tenant-update path writes the destination id (no tenant-admin route exposes it) | Tenant admins cannot self-assign a destination. |

---

## Rider linking

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| LP10 [NN] | Link success | Rider A `LinkStart` with Account-1 email, then `LinkConfirm` with the emailed code | `rider_loampass_link` row created (tenant-scoped); `Status` shows `linked = true`, the account in `accounts[]`, and `creditsAvailable` summed from LoamMx. |
| LP11 [NN] | Wrong / expired code | `LinkConfirm` with a bad or stale code | "That code is invalid or expired."; no link row written. |
| LP12 [NN] | Neutral LinkStart response | `LinkStart` with an email LoamMx does not recognize | Returns `{ sent: true }` regardless (no account enumeration). |
| LP13 [NN] | Link multiple accounts | Rider A links Account-1 and Account-3 | Two link rows; `Status.accounts` lists both; `creditsAvailable` is the sum across both. |
| LP14 [NN] | Re-link same account (idempotent) | Rider A links Account-1 again with a refreshed email | `ON CONFLICT (user_id, loampass_account_id)` updates email + `linked_at_utc`; still exactly one row. |
| LP15 [NN] | Unlink | Rider A `DELETE ?accountId=Account-3` | That link row removed (scoped to user + tenant); `Status` no longer lists it; remaining account intact. |
| LP16 [NN] | Linking unavailable | Set the integration unconfigured (`IsConfigured = false`), call `LinkStart` | "Loam Pass linking isn't available right now." |
| LP17 [R] | Auth required | Call any `RiderLoampass` route unauthenticated | 401 / "Invalid token." |
| LP18 | Tenant isolation of links | Rider A links on tenant X; query `Status` for the same person's account on tenant Y | Tenant Y shows not-linked; `ListByUserId` and `GetUserIdByAccount` are tenant-scoped. |

---

## Redeem

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| LP20 [NN] | Happy path $0 entry | Rider A (linked, has credits) redeems for a `race_entry` on a `scheduled`, Loam-Pass-accepting event | Returns a `CreatePurchaseResponse` with `amountCents = 0`; `event_ticket_purchase` is `paid`, `payment_method = 'loampass_credits'`; one `loampass_redemption` (`status = redeemed`); a $0 `tenant_ledger_entry` (all cents zero). |
| LP21 [NN] | Credit decremented exactly once | Note LoamMx balance before/after a single redeem | Balance drops by exactly 1; `Status.creditsAvailable` reflects the new sum. |
| LP22 [NN] | Multi-account draw | Rider A linked to Account-2 (0 credits) and Account-1 (>=1); redeem | Draw skips the empty account and charges Account-1; only Account-1 is decremented; `loampass_redemption.loampass_account_id` = Account-1. |
| LP23 [NN] | No credits anywhere | Rider whose every linked account has 0 credits redeems | Pending row is cancelled (spot freed), no `loampass_redemption` written, no credit spent; returns the LoamMx error or "No Loam Pass credits available." |
| LP24 [NN] | Idempotent retry | Redeem, then re-issue the SAME request id / retry the same `purchaseId` against LoamMx | LoamMx treats it as already-processed (`alreadyProcessed`); no second credit spent; no duplicate `loampass_redemption` (UNIQUE on `event_ticket_purchase_id`); ledger insert dedupes on `23505`. |
| LP25 [NN] | Double-submit (two clicks) | Fire two RedeemLoampass calls for the same rider/tier near-simultaneously | Advisory lock serializes; the second sees the first pending row via `HasActiveRaceEntry` and is rejected with "You're already entered in this class." Exactly one credit spent, one paid entry. |
| LP26 [NN] | No double-enter a race class | After a successful redeem on a tier, redeem again on the same tier | Rejected ("already entered in this class"); no extra credit spent. |
| LP27 [NN] | Capacity respected (finite inventory) | Tier `inventory` set; sell it to the limit, then redeem | Under the lock `sold + 1 > inventory` rejects with "'{tier}' is sold out."; no credit spent. |
| LP28 [NN] | Not a race_entry tier | Redeem against a spectator/gate tier | "Loam Pass credits cover rider entry only." |
| LP29 [NN] | Event type not accepting | Redeem on a `scheduled` event whose type has the toggle OFF (non-practice) | "Loam Pass credits aren't accepted for this event." Practice always allowed. |
| LP30 [NN] | Non-scheduled event | Redeem on a cancelled/completed event | "Event not found." (guard is `status != 'scheduled'`). |
| LP31 [NN] | Waiver gate | Event `RequiresRiderWaiver` with an active unsigned waiver; redeem | "You must sign the current waiver..."; no credit spent. Sign, retry -> succeeds. |
| LP32 [NN] | Not linked | A rider with no link on a participating track redeems | "Connect your Loam Pass on your profile first." |
| LP33 [NN] | Track not participating | Redeem on a tenant with NULL destination | "This track doesn't accept Loam Pass credits." |
| LP34 [NN] | LoamMx unreachable at redeem | Break the LoamMx host/key, then redeem (track still flagged) | `RedeemAsync` returns not-redeemed ("Could not reach LoamPassMx."); pending row cancelled (spot freed); no `loampass_redemption`; rider keeps credits; user sees the error. No orphaned paid $0 entry. |
| LP35 [NN] | $0 appears in sales/reports | After LP20, open Admin -> Purchases and the rider report | The $0 `loampass_credits` entry shows (event_ticket kind), with rider name + tier; confirm it is not miscounted as revenue. |
| LP36 [R] | Gate check-in does not re-spend | After LP20, staff scan the rider's Loam Pass QR at the gate (`GateCheckIn`) | Existing paid entry flips to `redeemed`; **no** additional credit spent; second scan reports "already checked in." |

---

## Un-redeem & refund

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| LP40 [NN] | Admin refund un-redeems | Admin (`SalesRefund`) refunds the LP20 `event_ticket` purchase | `Refund` calls LoamMx `Unredeem` by the recorded `idempotency_key`; credit restored on LoamMx; `loampass_redemption.status = refunded` (+ `refunded_at`); ticket cancelled + refund ledger row at $0. `refundCents` forced to 0 (no money moved). |
| LP41 [NN] | Refund is idempotent | Refund the same loampass purchase twice | Second time the redemption is already `refunded`, so `Unredeem` + `MarkRefunded` are skipped; no double credit-back. LoamMx `Unredeem` is itself idempotent on the key. |
| LP42 [NN] | Credit balance restored | Check LoamMx balance after LP40 | Balance is back to its pre-redeem value (net zero across LP20 + LP40). |
| LP43 [NN] | Refund only when paid | Attempt to refund a pending/cancelled loampass entry | "Only a paid purchase can be refunded." |
| LP44 | Refund when LoamMx unreachable | Break LoamMx, then admin-refund a loampass entry | DOCUMENT actual behavior: `RefundAsync` returns false on a swallowed error but `MarkRefunded` still runs, so the RidePass row reads `refunded` while the LoamMx credit may NOT be restored. Flag the drift (see risks). |
| LP45 [!!] | Rider self-cancel does NOT un-redeem | Tenant has `AllowSelfCancel = true`; rider self-cancels their loampass entry via `MeController.CancelMyTicket` | KNOWN GAP: the ticket is cancelled (spot freed) and the waitlist promoter fires, but `CancelMyTicket` only does a Stripe refund and never calls `_loampass.RefundAsync`. The LoamMx credit is **lost** (not returned). Confirm and file. |
| LP46 [R] | Refund permission gate | A user without `SalesRefund` calls `Refund` | 403 / rejected. |

---

## Edge

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| LP50 [NN] | Cross-step double-enter on a ladder | Rider redeems on step-1 of a ladder class, then redeems on step-2 of the SAME `ladder_group` | WATCH: redeem dedupe uses `HasActiveRaceEntry(tier.Id)` (single tier), NOT the ladder-group-spanning `FindRaceClassConflict`. Verify whether a rider can double-enter one class across steps and double-spend a credit. Document the result. |
| LP51 [NN] | Tier with no inventory cap | Redeem on a `race_entry` tier whose `inventory` is NULL on an event at `event.capacity` | WATCH: the redeem path only checks `tier.Inventory.HasValue`; it does not consult `event.capacity` / `GroupSoldCount` like the card buy flow. Confirm whether credit redeems can oversell event capacity. |
| LP52 [NN] | Status when LoamMx down | Linked rider opens `Status` while LoamMx is unreachable | `GetCreditsAsync` swallows the error and returns 0, so `creditsAvailable` shows 0 (not an error). Confirm the UI does not imply "no credits" misleadingly. |
| LP53 [NN] | Stale/over-reported credits | LoamMx reports >=1 credit at `Status`, but the credit is consumed elsewhere before redeem | Redeem fails the draw (no credit), pending row cancelled, clear message. No $0 paid entry created. |
| LP54 [NN] | Unconfigured integration on a flagged track | Track has a destination id but `IsConfigured = false` (missing key) | Redeem: `RedeemAsync` returns "integration is not configured", pending row cancelled. Linking: `LinkStart` returns the unavailable message. |
| LP55 [R] | QR parse for gate check-in | `GateCheckIn` with a full `{issuer}/QR/{passId}` URL and with a bare pass id | Both parse to the same pass id; unknown pass -> "wasn't recognized"; pass not linked here -> the connect-in-profile message. |
| LP56 | Wrong-tenant purchase id | Attempt to refund/redeem against a `purchaseId` from another tenant | Tenant-scoped lookups (`GetById`, `GetByPurchaseId`, `MarkRedeemed` WHERE tenant_id) reject / not-found; no cross-tenant mutation. |

---

## Known risks / watch-items
- **Multi-tenant isolation:** link reads/writes (`ListByUserId`, `GetUserIdByAccount`, `DeleteByAccount`) and `loampass_redemption` reads are tenant-scoped; `MarkRedeemed` carries the `tenant_id` predicate. The unique link index `(user_id, loampass_account_id)` omits tenant, but RidePass `user_id` is already per-tenant. Verify LP18 / LP56 hold.
- **Self-cancel does not un-redeem (LP45):** `MeController.CancelMyTicket` has no LoamPass branch, so a rider self-cancelling a `loampass_credits` entry frees the RidePass spot but never returns the LoamMx credit. Strongest correctness gap. Only the admin `Refund` path un-redeems.
- **Refund drift when LoamMx is down (LP44):** `Refund` marks the RidePass redemption `refunded` even if `Unredeem` failed (the result is ignored), so RidePass and LoamMx can disagree about whether the credit was returned. Consider gating `MarkRefunded` on a successful `Unredeem` or a retry/outbox.
- **Double-spend across ladder steps (LP50):** the redeem dedupe is per-tier (`HasActiveRaceEntry(tier.Id)`), not class-wide like registration's `FindRaceClassConflict`. A multi-step ladder class may allow a second entry (and a second credit spend) at a different step.
- **Capacity not fully enforced on redeem (LP51):** the redeem path checks only `tier.Inventory`; it skips the `event.capacity` / `GroupSoldCount` recheck the card buy flow uses, so a no-inventory tier could let credit redeems exceed event capacity.
- **Idempotency keying:** the LoamMx `idempotencyKey` is the RidePass `purchaseId`, and every redeem call mints a fresh purchase row. True same-purchase retries are safe; protection against client double-submits relies on the in-lock `HasActiveRaceEntry` dedupe (the advisory lock is released BEFORE the network redeem). Confirm LP24 + LP25 together.
- **$0 accounting:** the entry and ledger rows are all-zero cents; ledger insert dedupes on `23505`. Verify it never inflates revenue or platform-cut math, and that it surfaces correctly in `v_recent_sales` / reports (LP35).
- **Silent failures:** `GetCreditsAsync`, `GetPassOwnerAsync`, and the M2M `PostAsync` swallow exceptions and return 0/false/null, so an integration outage degrades quietly (credits read as 0, links/redeems just fail with generic messages). Good for safety, but monitor LoamMx availability out of band.
