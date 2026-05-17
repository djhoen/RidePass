# Section 9: Customer aggregation, Reports & Survey responses

## Inline fix applied during this review

**Critical — `MarkRedeemed` was tenant-unscoped on pass / ticket / extra repos (FIXED).** Same shape
as the cross-tenant write closed in Section 1 for season-pass reservations. `ReportsController.SetCheckIn`
and `RedemptionController` both called `MarkRedeemed(purchaseId, staffId, atUtc)` against repos whose
SQL was `UPDATE … WHERE id = @id` — any staff with `SalesRedeem` at tenant A could flip arbitrary
purchase ids across every tenant to `redeemed`. Fixed by adding `tenantId` to all three
`MarkRedeemed` signatures (`IPassPurchaseRepository`, `IEventTicketPurchaseRepository`,
`IEventExtraRepository`) and their impls (predicate now `WHERE id = @id AND tenant_id = @tenantId`)
plus all six call sites (`ReportsController` + `RedemptionController` single + bulk paths).

## Scope

Read end-to-end:

- `webapi/Controllers/CustomerController.cs` — list, detail, top-riders.
- `webapi/Controllers/ReportsController.cs` — `Admin/Summary`, `Admin/EventRiders/{eventId}`,
  `Admin/EventRiders/{purchaseId}/CheckIn`, `Admin/EventRiders/Ticket/{purchaseId}/RaceNumber`,
  `Admin/EventRiders/{eventId}/SendSms`, `Admin/EventRiders/{eventId}/Export/Trackside`,
  `Admin/DailyEvents`, `Admin/CheckInLookup`.
- `webapi/Controllers/SurveyController.cs` — admin endpoints were Section 8; this section reread
  `Public/{token}`, `Public/{token}/Submit`, `Admin/{id}/Results`, and the `ResolveAudience` shape.
- `Services/Repositories/CustomerRepository.cs` — the activity CTE, `ListForTenant`,
  `CountForTenant`, `GetDetail`, `GetTopRiders`.
- `Services/Repositories/ReportsRepository.cs` — `GetPassTotals`, `GetTicketTotals`,
  `GetUniqueRiders`, `GetDailyRevenue`, `GetTopPassProducts`, `GetTopEvents`, `GetPlatformTotals`,
  `GetPlatformDailyRevenue`, `GetTenantBreakdown`, `GetEventRiders`, `LookupCheckInByToken`,
  `GetEventsInRange`.
- `Services/Repositories/SurveyRepository.cs` — public-path methods (`GetByPublicToken`,
  `GetInviteByToken`, `MarkInviteOpened/Completed`, `CreateResponse`, `CreateAnswer`,
  the four `Audience*` resolvers).
- `Services/Repositories/Data/CustomerData/CustomerData.cs` (DTO shapes).
- `RidePass.Migrator/Scripts/Script0076_Surveys.sql`,
  `Script0077_SurveyOtherChoice.sql`,
  `Script0080_RecentSalesView.sql` (cross-reference for "should this read use v_recent_sales").
- `vueapp/src/views/Admin/Customers.vue`.
- `vueapp/src/views/Admin/CustomerDetail.vue` (pre-existing typing gaps noted in build skill — skipped per scope).
- `vueapp/src/views/Admin/Reports/SalesSummary.vue`,
  `vueapp/src/views/Admin/Reports/EventRiders.vue`,
  `vueapp/src/views/Admin/Reports/DailyEvents.vue`.
- `vueapp/src/views/Survey.vue`, `vueapp/src/components/SurveyForm.vue`.

Spot-checked:

- `Services/Repositories/PassPurchaseRepository.cs` (`MarkRedeemed`, `UndoRedeemed`,
  `SetRaceNumber` absence on pass — confirmed only event_ticket has race_number).
- `Services/Repositories/EventTicketPurchaseRepository.cs` (`MarkRedeemed`, `UndoRedeemed`,
  `SetRaceNumber`).
- `webapi/Controllers/RedemptionController.cs` — to confirm the OTHER callers of
  `MarkRedeemed` reach it through a tenant-scoped preview.
- `Services/Repositories/MembershipRepository.cs` (`GetActive` signature for the
  CheckInLookup membership-gate path).
- `webapi/AuthPolicies/TenantPermissions.cs` — to evaluate role gating on survey
  results vs accountant role.
- `RidePass.Migrator/Scripts/Script0071_SpectatorWaiverSignatures.sql` — confirmed that
  spectator signatures land in `rider_waiver_signature` (single table; user_id nullable).

Section 1–8 findings are not repeated. The pass / ticket / season-pass redemption surfaces
are otherwise covered by Section 4; this section calls out the additional path that goes
through `ReportsController.SetCheckIn` for the same MarkRedeemed methods.

## Architecture summary

**A "customer" of a tenant is derived, not stored.** `CustomerRepository.ActivityCte`
unions four sources — `pass_purchase`, `event_ticket_purchase`, `season_pass_purchase`,
and `rider_waiver_signature` — all scoped by `tenant_id`. A person becomes a customer
the moment any of those rows exists with a non-null `purchaser_user_id` (or `user_id`
for the waiver branch). The `GetDetail` gate uses the same UNION but with `LIMIT 1` —
return 404 if the user has no activity at this tenant, so a tenant admin can't enumerate
users they've never interacted with.

**Guest checkouts are silently excluded.** The CTE has `purchaser_user_id IS NOT NULL`
on all three purchase branches; only `rider_waiver_signature` contributes a row with a
real user_id (the spectator-guest signatures from Script0071 have `user_id` NULL, and
the JOIN to `users` then drops them anyway). The user has already flagged the confusion
this causes; documented as a design question below.

**Reports has three flavors of output.**
1. `Admin/Summary` — tenant-scoped period totals (revenue, refunds, disputes, daily
   chart, top products, top events) computed from raw `pass_purchase`/`event_ticket_purchase`
   tables. Does NOT consult v_recent_sales and does NOT include event_extra, season_pass,
   membership, gift_card, or rental revenue.
2. `Admin/EventRiders/{eventId}` — UNION across the three sources of registrants
   (pass/event_ticket/season_pass reservation) joined to the user table for phone +
   race_number + hometown. Drives the gate roll-call UI, the Trackside CSV export,
   the SMS blast, and individual per-row check-in toggles.
3. `Admin/CheckInLookup` — scan a redemption-token QR, resolve to a rider, return
   today + future registrations across all three sources + waiver/membership gating flags.

**SetCheckIn fans into three repository paths.** `case "season_pass"` was patched in
Section 1 to take a tenant id and join through `season_pass_purchase`. The `case "pass"`
and `case "event_ticket"` branches were left as `MarkRedeemed(purchaseId, staffId, atUtc)`
and the corresponding repository SQL is `UPDATE … WHERE id = @id` with no tenant predicate.
See Critical #1 below.

**Survey public submission.** Two token kinds: per-recipient invite tokens
(`survey_invite.token`) and per-survey public tokens (`survey.public_token`). The controller
tries invite first; if found, loads the survey via `GetById(invite.SurveyId, resolvedTenantId)`
— so a leaked invite token from tenant A cannot be replayed against tenant B's
subdomain. The public-token path is tenant-scoped in the SQL directly
(`WHERE public_token = … AND tenant_id = …`). Both gate on `status = 'published'` and
the `closes_at_utc` floor. The submit endpoint records `ip_address` and the resolved
respondent email/name; the public results aggregation never re-emits IP or names per
response (only aggregated counts and free-text strings).

**Survey results aggregation.** Server-side projection of choices → counts and
free_form → list of free-text strings, with the "Other — please explain" choice
contributing both a count and a `freeTextAnswers[]` list of explanations. Percentages
are computed against `totalPicks` (sum across all choices), not against respondent count.
For single_choice that's identical because there is exactly one pick per respondent;
for multiple_choice it's misleading (see High #2).

## Findings

| Severity | Location | Description | Suggested fix |
|---|---|---|---|
| **Critical** | `webapi/Controllers/ReportsController.cs:170-180` (`SetCheckIn`, `case "pass"` and `case "event_ticket"`) + `Services/Repositories/PassPurchaseRepository.cs:131-138` (`MarkRedeemed`) + `Services/Repositories/EventTicketPurchaseRepository.cs:98-105` (`MarkRedeemed`) | The action takes `purchaseId` straight from the URL and calls `_passes.MarkRedeemed(purchaseId, staffId, DateTime.UtcNow)` / `_tickets.MarkRedeemed(purchaseId, staffId, DateTime.UtcNow)`. Both repository methods run `UPDATE pass_purchase SET status='redeemed', redeemed_at_utc=@atUtc, redeemed_by_user_id=@redeemedByUserId WHERE id = @id` — **no `tenant_id` predicate**. Any staff member with `SalesRedeem` can flip an arbitrary purchase id at any tenant to `redeemed` (and stamp THEIR staff id as the redeemer). This is the same class of bug Section 1 inline-fixed for the `season_pass` branch of this same switch — `case "season_pass"` was patched, the other two were not. Section 4 covers the redemption-token path that goes through `RedemptionController` (where a `LookupAsync` preview resolves the row by tenant-scoped token first, so MarkRedeemed inherits the scope check). The Reports check-in path skips that preview because it already has the purchase id. | Add `tenantId` to `IPassPurchaseRepository.MarkRedeemed` and `IEventTicketPurchaseRepository.MarkRedeemed` and append `AND tenant_id = @tenantId` to the SQL. Audit every caller: `ReportsController.SetCheckIn` passes `_tenantContext.TenantId`; `RedemptionController` (single + bulk paths) and any background workers must pass the resolved tenant id explicitly. The UndoRedeemed methods already do this correctly — mirror them. |
| **High** | `webapi/Controllers/ReportsController.cs:50-99` (`GetTenantSummary`) and `Services/Repositories/ReportsRepository.cs:13-147` | The tenant summary report is hardwired to `pass_purchase` + `event_ticket_purchase` ONLY. `event_extra_purchase`, `season_pass_purchase`, `membership_purchase`, `gift_card`, and `rental_purchase` are all silently excluded from `TotalRevenueCents`, `RefundedCount`, `RefundedAmountCents`, `UniqueRiders`, `DailyRevenue`, `TopPassProducts`/`TopEvents`. A tenant that sells $5k of extras + $3k of memberships + $2k of rentals in the period will see "Total revenue: $0" if they didn't sell any passes or tickets. The admin Dashboard and the Purchases list both already read from `v_recent_sales` (Section 1 noted it). The summary report has not been migrated. This is exactly the silent-disappear failure mode the v_recent_sales skill exists to prevent. | Rebuild `GetPassTotals` / `GetTicketTotals` / `GetDailyRevenue` (and the unique-buyers + top-products aggregations where it makes sense) on top of `v_recent_sales` — group by `kind` rather than per-table SQL strings. The per-product chart can stay per-kind since item_name shapes differ. Match `v_recent_sales`'s `status IN ('paid','redeemed')` semantics. |
| **High** | `webapi/Controllers/SurveyController.cs:340-358` (`Results`) + `Services/Repositories/SurveyRepository.cs:305-313` (`ListAnswersForSurvey`) | Multiple-choice percentages are computed as `100 * choice_count / totalPicks` where `totalPicks` is the sum across all picks (one row per (response, choice)). For multiple_choice that means the percentages don't represent "share of respondents who picked this option" but rather "share of all selections" — and they sum to 100% across choices regardless of how many respondents picked multiple boxes. With 100 respondents who each pick 3 of 5 options, every choice shows 33% instead of "this percentage of respondents picked it." UX-wise this is the wrong number and admins will trust it. Single-choice happens to be correct because there's at most one pick per respondent. | Use `answeredCount` (already computed: distinct response ids that answered the question) as the denominator for multiple_choice. Or surface both numbers as "X of Y respondents (Z% of selections)". |
| **High** | `webapi/Controllers/SurveyController.cs:451-560` (`SubmitPublic`) | No rate limit, no per-IP cap, no CAPTCHA. The `[HttpPost("Public/{token:guid}/Submit")]` endpoint is anonymous. Token entropy is fine (UUID v4 from `uuid_generate_v4()`), but once a survey's public_token is shared the endpoint will accept unlimited submissions from a single IP. A drive-by spammer can flood `survey_response` + `survey_answer` for a published survey and corrupt the results. Email-only invites are slightly protected because submission re-stamps `completed_at_utc` (still doesn't *reject* a second submission — the schema/SQL allow multiple responses against the same invite_id). | Add a per-IP rate limit on `SubmitPublic` (e.g., 5/min/IP via the existing tenant-scoped rate limiter if one exists, or `Microsoft.AspNetCore.RateLimiting` with a fixed window). For invite-bound submissions, either reject when `survey_invite.completed_at_utc IS NOT NULL` (with a "you've already submitted" message) or de-duplicate downstream. Document the chosen semantics on the schema. |
| **Medium** | `Services/Repositories/CustomerRepository.cs:28-62` (`ActivityCte`) | The aggregation that defines "is this user a customer of this tenant" predates the extras / memberships / rentals / gift-card tables. Missing branches: `event_extra_purchase`, `membership_purchase`, `gift_card` (buyer or recipient), `rental_purchase`. A user who only bought a $40 parking extra and a season membership shows up as a non-customer. The user explicitly flagged guest checkouts as a known limitation (`purchaser_user_id IS NOT NULL`); the missing-kind problem is the same shape but worse because even authenticated buyers in these kinds disappear. | Extend the CTE with a UNION ALL branch per missing kind, mapping each table's columns into the `(user_id, activity_at, amount_cents, is_paid)` shape used by the rest of the query. Membership uses `user_id` (not purchaser_user_id); gift_card uses `buyer_user_id`. Consider whether the CTE should switch to reading from `v_recent_sales` to inherit the same single-source-of-truth pattern as Dashboard/Purchases. Same fix needed for the `GetDetail` gate. |
| **Medium** | `vueapp/src/views/Admin/CustomerDetail.vue:84-170` | The detail page is still per-kind (Passes / Event Tickets / Season Passes tabs). Extras, memberships, gift cards, and rentals don't appear. If a user buys a $1000 season pass + $200 of extras + a membership, the tabs show $1000. The "Total at this track" computed in `totalSpent` only sums the three included kinds — so the displayed running total is also wrong. Same root cause as Medium above. | After the repository CTE is widened, surface the additional kinds either as tabs or as a unified "All Activity" tab backed by `v_recent_sales` filtered by `purchaser_user_id` + `tenant_id`. |
| **Medium** | `Services/Repositories/CustomerRepository.cs:64-103` | `ListForTenant` and `CountForTenant` evaluate the activity CTE twice per page request, scanning four tables each time. With no `(tenant_id, purchaser_user_id)` index on `pass_purchase` / `event_ticket_purchase` / `season_pass_purchase` (verified via Script grep — only `(tenant_id, status)` exists), every search is a full tenant scan of each table, deduplicated in memory. Search uses `ILIKE '%foo%'` which can't use any standard btree index. For a tenant with 100k purchases this will get slow. | Two complementary fixes: (a) add a composite index `(tenant_id, purchaser_user_id)` on each of the three purchase tables so the CTE can scan-by-tenant; (b) use `pg_trgm` GIN on `users.first_name`, `users.last_name`, `users.email` to make the leading-wildcard ILIKE searchable. Alternative: materialize a per-tenant `customer_summary` table refreshed by a worker (more work but the right shape long-term). |
| **Medium** | `webapi/Controllers/CustomerController.cs:48-66` (`Detail`) and the DTO `CustomerSummary.Birthdate` (`Services/Repositories/Data/CustomerData/CustomerData.cs:14`) | The customer detail endpoint returns the full birthdate (year + month + day) to anyone with `CustomersView` — which includes `tenant_cashier` and `tenant_accountant` roles via the role-set in `TenantPermissions.cs`. Section scope flagged this as a question: most UI surfaces only need "is this rider a minor?" (boolean) or month/day for "happy birthday" promos. Returning the full date is PII broader than needed. | Either gate the year on a separate permission, or add an `IsMinor` boolean to the wire DTO and only ship the year to a narrower permission (e.g., `UsersManage`). At minimum, ensure birthdate isn't included in the *list* DTO (`CustomerSummary`) — it currently is, even though Customers.vue doesn't render it. |
| **Medium** | `webapi/Controllers/SurveyController.cs:304-381` (`Results`) | The results endpoint is gated on `CampaignsManage`. `tenant_accountant` has `ReportsView` + `CustomersView` but NOT `CampaignsManage`, so they can't view survey results — yet survey results are reporting data. `tenant_manager` has both so they're fine; the affected role is the accountant who might reasonably want to see the response counts on a customer-satisfaction survey. The reverse is also true: a manager whose only campaign duty was sending the invite blast inherits the right to view all results. | Decide intent: if results are "reports", add `[Authorize(Policy = ReportsView)]` as the gate (and CampaignsManage stays on create/update/send). If they're "campaign artifacts", current shape is correct — document why. |
| **Medium** | `webapi/Controllers/SurveyController.cs:451-561` (`SubmitPublic`) + `Services/Repositories/SurveyRepository.cs:264-268` (`MarkInviteCompleted`) | Submit doesn't reject a second submission against the same invite token. `MarkInviteCompleted` only updates `completed_at_utc IF currently NULL`, so the stamp doesn't move — but a fresh `survey_response` + answer rows are inserted on every submit. Results then over-count: one invite recipient can submit five times and contributes five rows to choice counts. The frontend says *"you can submit again, but only the latest response counts"* (SurveyForm.vue line 13) — the backend doesn't enforce that. | Either (a) explicitly reject re-submission for invite tokens (return 400 if `invite.completed_at_utc IS NOT NULL`), or (b) implement "latest wins" by deleting prior `survey_response` rows for the invite before inserting the new one. Option (b) matches the UI copy. |
| **Medium** | `Services/Repositories/ReportsRepository.cs:516-574` (`GetEventsInRange` — `pass_agg`/`tk_agg`/`spr_agg` subqueries) | The three aggregation subqueries don't constrain by event date — `pass_agg` reads every pass_purchase for the tenant and aggregates by event_id, then LEFT JOINs to events in the requested window. Functionally correct (only events in range are surfaced), but each query scans every paid purchase in the tenant's history regardless of how narrow the window is. For an established tenant the cost grows linearly with all-time sales for a single-day report. | Push the event date filter into the subqueries by joining each agg to `event` and adding the same `e.starts_at >= @fromUtc AND e.starts_at < @toUtc` predicate, OR materialize the registered/checked-in counts on the event row via a trigger and read them directly. Existing approach works for small tenants. |
| **Medium** | `Services/Repositories/ReportsRepository.cs:43-58` (`GetUniqueRiders`) | "Unique riders" is computed as `COUNT(DISTINCT lower(purchaser_email))` across pass_purchase + event_ticket_purchase. It will count the same registered rider twice if they purchased once as a guest (some random email) and once with their account (their real email). It also misses event_extra/season_pass/membership purchasers — same root cause as the High above. | Switch to `COUNT(DISTINCT COALESCE(purchaser_user_id::text, lower(purchaser_email)))` over `v_recent_sales` so registered riders dedupe on user_id and guests still dedupe on email. |
| **Medium** | `Services/Repositories/ReportsRepository.cs:355-510` (`LookupCheckInByToken` — three sequential SELECTs by token) | The endpoint runs up to three round trips to identify the rider (pass → ticket → season_pass), then a fourth for the user's phone, then a fifth UNION for their registrations, then in the controller it calls `_events.GetById` *per row* in `TodayRegistrations` to check `RequiresRiderWaiver`. With several registrations on the same day this is N+1 (one event fetch per registration). This is the path scanned at the gate and must be fast. | Identify rider in a single statement: `UNION ALL ... ORDER BY priority LIMIT 1` against the three tables. Replace the per-row event lookup in `ReportsController.CheckInLookup` with a single batched `GetByIds`. Cache the active waiver lookup per request (already 1 round trip — fine). |
| **Low** | `webapi/Controllers/SurveyController.cs:566-602` (`InvitePreview` / `AudiencePreview`) | Audience preview returns up to 10 emails in `Sample`. The current Section 8 review already covered authorization but flagging here for cross-cutting consistency: a tenant_manager with `CampaignsManage` can repeatedly call `AudiencePreview` with `type = "custom"` + various email lists to confirm whether an email is in any of the tenant's customer/subscriber buckets. The endpoint will echo back the email iff it passes EmailHelper.IsValid (it always does for well-formed inputs). Useful UX, but worth noting that it doesn't enrich the data the caller doesn't already have. | No code change. Document that the sample is for UX confirmation; the caller already supplied the emails in the "custom" case. For the other audience kinds (event/timeframe/all_customers/subscribers), the sample IS a small data leak vs the count-only alternative — consider gating the sample on a separate permission if the user is concerned. |
| **Low** | `webapi/Controllers/ReportsController.cs:243-277` (`ExportTrackside`) | CSV export is properly RFC-4180 escaped via `CsvEscape` (quotes commas / quotes / CRLF). One small issue: Excel's "CSV injection" attack — a cell starting with `=`, `+`, `-`, or `@` is interpreted as a formula. If a rider's hometown or item name starts with one of those characters (uncommon for names but the Class column is admin-controlled and could be e.g. `=SUM(...)`), Excel will execute it on open. | Optional: prepend a single quote `'` to any cell value that starts with `=`, `+`, `-`, `@` (Google Sheets has the same behavior). Low risk because the inputs are admin-defined, but the export goes to third-party software (Trackside / Excel). |
| **Low** | `vueapp/src/views/Admin/Reports/SalesSummary.vue:191-193` (`load`) | Range boundary computed as `dayjs.tz(rangeFrom + 'T00:00', tz()).utc().toISOString()`. The `T00:00` literal is correctly interpreted in the tenant's tz and converted to UTC — good. The To boundary, however, uses the same `T00:00` for the end date, meaning a user who picks "From: May 1, To: May 1" gets a zero-width range. The preset handlers consistently add 1 day (`rangeTo = today.add(1, 'day')`), but the manual date pickers don't, and the load() will then short-circuit at the server's `toUtc <= fromUtc` check. | Either make the UI add 1 day on submit, or document that `rangeTo` is exclusive. Current code is correct under the convention but easy to misuse. |
| **Low** | `webapi/Controllers/SurveyController.cs:451-561` (`SubmitPublic`) | `IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()` stamps the raw IP onto every response row. Anonymous-friendly surveys then have a per-response IP that, combined with web-server logs, can re-identify a "anonymous" respondent. The aggregation endpoint never re-emits IP, so the leakage is confined to the DB, but a future "export raw responses" endpoint would expose it. | Decide intent: if surveys are truly anonymous, drop the column for surveys without `require_email`, or hash with a per-tenant salt so it's still useful for fraud detection without being a re-identifier. Document the policy in `Script0076_Surveys.sql`. |
| **Low** | `Services/Repositories/SurveyRepository.cs:178-188` (`ReplaceChoices`) | Per-row INSERT loop inside `ReplaceChoices`. With a 20-option multi-choice question this is 21 round trips (one DELETE + 20 INSERTs). Not a correctness issue, just slow under any scenario with many choices. | Use `UNNEST` to insert all rows in a single statement, mirroring the pattern used by `UpdateQuestionSortOrders` / `UpdateChoiceSortOrders`. |
| **Low** | `webapi/Controllers/SurveyController.cs:45-74` (`ListAdmin`) | The admin survey list does N+1 round trips per survey to compute `QuestionCount` and `ResponseCount` (own comment acknowledges this). Will get slow at scale. | Replace with one SQL query that joins survey + LATERAL(SELECT COUNT) twice, or with pre-aggregated subqueries. Pattern flagged by the existing comment as a known shortcut. |

## Patterns worth replicating

- **`SurveyRepository.GetByPublicToken` doubles down on tenant scope.** The SQL has
  both `public_token = @publicToken` AND `tenant_id = @tenantId`. The comment explicitly
  calls out "Defense in depth — public_tokens are random GUIDs and not guessable, but
  isolation by tenant is non-negotiable." This is the right shape for any token-resolving
  endpoint that lives on a tenant subdomain.
- **`SurveyController.GetSurveyByIdAnyTenant`** re-fetches through the tenant-scoped
  GetById to confirm the invite's survey actually belongs to the resolved tenant. Same
  defense-in-depth idea applied to the invite-token path.
- **`ReportsController.SetCheckIn` and `SetRaceNumber` use `SalesRedeem`** rather than
  `ReportsView` for write actions on the read-only Reports surface. Right call —
  `ReportsView` accounts shouldn't be able to mutate. (The Critical above is about a
  separate bug — the underlying SQL doesn't enforce tenant scope — but the auth-policy
  choice is correct.)
- **`MarkInviteOpened` is idempotent** — the SQL has `AND opened_at_utc IS NULL` so a
  recipient who reloads the survey page doesn't reset their open timestamp. Same shape
  on `MarkInviteCompleted` — also `IS NULL`. Good pattern; document that the same idea
  needs to apply to `MarkInviteSent` if "resend" is ever added.
- **`CustomerController.Detail` returns 404 (not 403) when the user has no activity
  at this tenant.** The repository's `gateSql` (lines 110-119) is exactly the right
  shape — confirm activity before exposing any user fields. The comment explicitly
  explains the choice ("we deliberately return 404 (not 403) so we don't confirm the
  user exists outside this tenant's scope"). This is the canonical pattern for any
  endpoint that takes a globally-unique GUID as input.
- **`SubmitPublic` validates required questions server-side** rather than trusting
  the SurveyForm pre-flight (line 487-497). The comment acknowledges UI also enforces
  but the server is the source of truth. Right.
- **`ReplaceChoices` deletes-then-inserts** rather than diff-and-merge — simple, correct,
  no orphaned choices, no cascading issues. The schema `ON DELETE CASCADE` from
  `survey_answer.choice_id` is what makes this safe (and would be the gotcha if surveys
  ever had locked-after-publish semantics).

## Open questions

1. **Customer list excludes guests by design.** The user has already flagged this. The
   product question is whether the customer list is "people I can re-market to" (then
   the user_id requirement is too strict — we should also include guest purchasers by
   purchaser_email, deduped), or whether it's "registered users with activity here"
   (then it's correct, but the UI should say so). Until the product question is answered,
   the missing-kind bug (Medium #1) is the immediate concern.
2. **Top Riders denominator.** `GetTopRiders` requires a `rider_waiver_signature` row
   for the user. Spectator-only customers (who signed a spectator waiver via the
   spectator buy path, which now lands in the same `rider_waiver_signature` table per
   Script0071) will appear as "top riders" even though they never rode. The semantic
   of "rider" needs a way to distinguish — possibly by tier kind on the activity that
   counts.
3. **`v_recent_sales` adoption.** The view is now the source for Dashboard and Purchases.
   Reports (Section 9), Customer aggregation (Section 9), and any future export tooling
   should migrate to it. Worth a single follow-up task: "audit every cross-kind UNION ALL
   and replace with `v_recent_sales`". Section 9 found three: `GetTenantSummary`,
   `GetUniqueRiders`, `CustomerRepository.ActivityCte`.
4. **Survey anonymity policy.** The schema captures `ip_address`. The UI presents
   surveys as anonymous-friendly. There's no documented policy on when IP is retained,
   exposed, or purged. Worth a decision and a comment in Script0076.
5. **`Admin/EventRiders/SendSms` and admin-author content.** Section 1 noted the SMS
   sender is Twilio; the action takes the body verbatim from the admin. No content
   templating, no opt-out clause appended ("Reply STOP"). US carriers may filter
   bulk SMS without an opt-out — operational consideration, not a code bug.
6. **CheckInLookup membership-required logic.** The flag `RequiresMembership` is set
   whenever the tenant requires membership for *any* guarded purchase kind, even if the
   rider's check-in is for an event that doesn't require it. The UI then "shows a
   warning when required-and-not-active" — but the warning may be a false positive for
   a rider doing a spectator check-in at a track that only requires membership for riders.
   Section 7 territory; flagging for cross-reference.
7. **CustomerDetail birthdate display** shows the full birthdate string client-side
   (`detail.user.birthdate.substring(0, 10)`). If the PII concern in Medium #6 is
   addressed by stripping the year server-side, the UI should be reviewed to make sure
   it doesn't break.

## Coverage notes

- Every endpoint on the three in-scope controllers was read end-to-end and matched
  against the corresponding repository method's SQL.
- Repository methods were read in full for the in-scope repositories. The redemption /
  ticket / pass repositories were spot-read at the `MarkRedeemed` / `UndoRedeemed` /
  `SetRaceNumber` methods to confirm the Critical and to verify SetRaceNumber's
  tenant scope.
- Frontend Survey.vue + SurveyForm.vue read in full; the pre-flight validation matches
  the server-side validation in `SubmitPublic`.
- CustomerDetail.vue read end-to-end; the pre-existing typing gaps the scope refers to
  (the `as any` casts on `r.data` to reach `.data`) are present throughout the admin
  views and are noise from the api-response wrapper shape — not Section 9 findings.
- I did NOT verify that `v_recent_sales` includes the membership / gift_card / rental
  branches present in Script0080 — I trust the script's content as shown. The Medium
  fixes that migrate to the view depend on the view being complete; that's the
  whole point of the v_recent_sales radar skill.
- I did NOT exercise the Reports endpoints against a live DB; the Critical and High
  findings are read-from-source, not reproduced.
