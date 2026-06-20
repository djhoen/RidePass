# QA Test Plan: Reports & Dashboard

> Scope: tenant sales/rider reports (summary, daily events, event riders, check-in lookup, Trackside export), the admin dashboard snapshot, and the unified `v_recent_sales` read model that surfaces every sale kind. Filters, date-range scoping, data correctness, and tenant isolation. Last updated: 2026-06-20.

## Surface map
- **Reports (`ReportsController`):** `GET Admin/Summary` (`ReportsView`), `GET Admin/DailyEvents` (`ReportsView`), `GET Admin/EventRiders/{eventId}` (`ReportsView`), `GET Admin/EventRiders/{eventId}/Export/Trackside` (`ReportsView`), `GET Admin/CheckInLookup` (`SalesRedeem`). Per-row actions: `PUT .../CheckIn`, `PUT .../Ticket/{id}/RaceNumber`, `POST .../SendMessage`, `GET .../ScheduledMessages`, `POST Admin/ScheduledMessages/{id}/Cancel` (all `SalesRedeem`).
- **Dashboard (`DashboardController`):** `GET Snapshot` (`[Authorize]`, permission-gated blocks), `GET/PUT Config` (per-user JSON).
- **Repository:** `Services/Repositories/ReportsRepository.cs` (tenant + platform totals, daily revenue, top events, event riders, check-in lookup, daily events) and `RecentSalesRepository.cs` (reads `v_recent_sales`).
- **Unified view:** `Script0080_RecentSalesView.sql` to `v_recent_sales` (later extended; concessions branch in `Script0105`).
- **Frontend:** `src/views/Admin/Dashboard.vue`, `src/views/Admin/Purchases.vue`, `src/services/DashboardService.ts`, `PassService.ts`.

## Concepts under test
- **Summary** (`GetTenantSummary`) is computed ONLY from `event_ticket_purchase`: revenue + sold count from status (`paid`,`redeemed`), refunded/cancelled counts, refunded cents, unique riders (distinct `lower(purchaser_email)`), dispute count, daily revenue bucketed in the tenant timezone, and top events. `PassesSold` is hard-coded 0 and `TopPassProducts` empty (day passes retired).
- **Date ranges are half-open** `[fromUtc, toUtc)`; `toUtc <= fromUtc` is rejected. Daily buckets use `(created_at AT TIME ZONE @timezone)::date`; the dashboard computes local-day / month boundaries from the tenant timezone.
- **`v_recent_sales`** is the single unified read model. It UNION ALLs the per-kind tables and normalizes them to one shape: `pass` (day pass), `event_ticket`, `event_extra` (gate fees / parking / merch), `season_pass`, `membership` (joins `users` for buyer), `gift_card` (uses `initial_amount_cents` + `buyer_*`, synthesized label), `rental` (uses `rental_pi_id`). `RecentSalesRepository.List` filters by `tenant_id`, optional date range, optional status, ordered newest-first.
- **Dashboard snapshot** blocks are gated by effective permissions: `ReportsView` (today/month revenue, unique riders, last-7-days spark), `SalesView` (recent purchases from `v_recent_sales`, last 30 days), `DisputesView` (open dispute count), `SalesCancel` (pending refunds = cancelled tickets).
- **Event Riders** rolls up registrants across `event_ticket_purchase` (via tier to event) and `season_pass_reservation`, excluding cancelled, with check-in status. Trackside CSV exports only `race_entry` ticket rows.

## Preconditions / test data
- Tenant T1 with, inside one date window, at least one paid sale of EACH kind: day pass, event ticket (race entry), gate fee (event extra), season pass, membership, gift card, and rental. Plus a refunded ticket, a cancelled ticket, and a dispute.
- A second tenant T2 with its own sales for isolation checks.
- A multi-day event so day-boundary/timezone bucketing can be exercised; tenant timezone set to a non-UTC zone (e.g. America/Denver).
- A staff user with full permissions and a second staff user limited to `SalesView` only (no `ReportsView`).

---

## Admin (reports)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RP1 [NN] | Summary basic accuracy | `GET Admin/Summary?fromUtc&toUtc` over the window | `TotalRevenueCents` / `TicketsSold` match the paid+redeemed event tickets in range; `RefundedCount`/`RefundedAmountCents`/`CancelledCount`/`DisputedCount` match; `UniqueRiders` = distinct lowercased purchaser emails. |
| RP2 [NN] | Summary excludes non-ticket revenue | Include season pass / membership / gift card / rental / gate-fee sales in the window | Document the gap: Summary revenue counts ONLY event tickets, so these are NOT reflected. Confirm this is intended vs. a reporting bug. |
| RP3 [NN] | Half-open range boundary | Place one sale exactly at `fromUtc` and one exactly at `toUtc` | The `fromUtc` sale is included, the `toUtc` sale excluded (`>= from AND < to`). |
| RP4 [NN] | Invalid range | `toUtc <= fromUtc` | 400 ("toUtc must be after fromUtc."). |
| RP5 [NN] | Timezone day bucketing | With a non-UTC tenant tz, a sale just after local midnight | Daily revenue assigns it to the correct local date (not the UTC date); the spark/series buckets line up with the tenant's calendar day. |
| RP6 [NN] | Top events | Multiple events with ticket sales | Ordered by revenue desc, limited; counts + revenue per event correct (paid/redeemed only). |
| RP7 [NN] | Daily events report | `GET Admin/DailyEvents` for a day window | One row per event in range with registered / checked-in / revenue aggregated across tickets + season-pass reservations; revenue from tickets only. |
| RP8 [NN] | Event riders roll-call | `GET Admin/EventRiders/{eventId}` | Lists ticket + season-pass-reservation registrants, cancelled excluded; `TotalRegistrants`/`TotalCheckedIn` sums correct; ticket rows show check-in via status `redeemed`. |
| RP9 [NN] | Check-in toggle | `PUT EventRiders/{purchaseId}/CheckIn` for a ticket and a season-pass row | Ticket to `MarkRedeemed`/`UndoRedeemed`; season pass to reservation status `checked_in`/`reserved`; report reflects it. |
| RP10 [NN] | Trackside export | `GET .../Export/Trackside` | CSV contains ONLY `event_ticket` rows with `tier.kind='race_entry'` (no spectators/passes); columns Number,FirstName,LastName,Class,Hometown,Email,Phone; RFC-4180 quoting on commas/quotes. |
| RP11 [NN] | Check-in lookup by token | `GET Admin/CheckInLookup?token&fromUtc&toUtc` | Resolves a ticket or season-pass redemption token to the rider; returns today vs future registrations split at the local-day boundary; waiver + membership gating flags set correctly. |
| RP12 [R] | Reports permission gate | Summary / DailyEvents / EventRiders without `ReportsView`; CheckIn/SendMessage without `SalesRedeem` | 403. |

---

## Admin (dashboard)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RP13 [NN] | Snapshot requires tenant | `GET Dashboard/Snapshot` off a tenant subdomain | 400 ("only available on a tenant subdomain"). |
| RP14 [NN] | Recent sales shows every kind | Seed one paid sale of each kind in the last 30 days; load the snapshot as a `SalesView` user | `RecentPurchases` includes day pass, event ticket, gate fee (event_extra), season pass, membership, gift card, and rental, not just day passes. Each shows the correct `Kind`, item name, amount, and status. |
| RP15 [NN] | Recent sales date scope | Place a sale 31 days ago and one today | The 31-day-old sale is excluded (window is `now-30d .. now+1d`); today's sale appears. |
| RP16 [NN] | Gift card label + amount | Include a gift card sale | Item name synthesized ("Gift Card $N") from `initial_amount_cents`; amount = initial (not the drifting balance). |
| RP17 [NN] | Membership / rental mapping | Include a membership and a rental | Membership buyer name/email come from the joined `users` row (uses `user_id`, `name_at_purchase`); rental uses `rental_pi_id` as the PI and the product name. |
| RP18 [NN] | Permission-gated blocks | Load snapshot as a `SalesView`-only user (no `ReportsView`/`DisputesView`/`SalesCancel`) | Revenue/unique-rider/spark blocks absent; recent purchases present; dispute + pending-refund counts absent. Super-admin sees all blocks. |
| RP19 [NN] | Pending refunds count | Cancel (not yet refund) some tickets | `PendingRefundsCount` reflects cancelled tickets for THIS tenant only (the cross-tenant list is filtered in memory by `tenant_id`). |
| RP20 [R] | Dashboard config round-trip | `PUT Dashboard/Config` then `GET` | Per-user JSON stored verbatim and returned; isolated per user. |

---

## Edge / cross-tenant

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| RP21 [NN] | Summary isolation | Run Summary on T1 while T2 has sales in the same window | T1 totals exclude all T2 sales (every query scoped by `tenant_id`). |
| RP22 [NN] | Recent sales isolation | Snapshot/Purchases list on T1 vs T2 | Each tenant sees only its own rows; `v_recent_sales` queried with `tenant_id = @tenantId` pushed into every UNION branch. |
| RP23 [NN] | Event riders / check-in isolation | Request EventRiders or CheckInLookup for an event/token belonging to T2 while on T1 | Event not found / no registration (event + purchase queries scoped by `tenant_id`); no T2 data returned. |
| RP24 [NN] | Trackside isolation | Export Trackside for a T2 event while on T1 | 404 ("Event not found.") before any rows are read. |
| RP25 [NN] | Status filter accuracy | List recent sales filtered by `status=refunded` vs `paid` | Only matching rows returned; counts reconcile with the per-kind tables. |
| RP26 [NN] | New sale kind regression | Add a future purchase-shaped table without a `v_recent_sales` branch | The new kind silently disappears from the dashboard + Purchases list. Treat as the canonical reason to update the view (see `recent-sales-view` skill). |

---

## Known risks / watch-items
- **Summary under-reports total revenue** (RP2): `GetTenantSummary` and the dashboard revenue blocks count ONLY `event_ticket_purchase`. Season passes, memberships, gift cards, rentals, and gate fees (event extras) contribute zero to "Total Revenue," even though `v_recent_sales` shows them. This is the highest-impact data-correctness concern: confirm whether Summary should sum across `v_recent_sales`.
- **`v_recent_sales` drift** (RP26): a new purchase table that omits its UNION ALL branch vanishes from the dashboard and Purchases list with no error. The `recent-sales-view` skill exists precisely to keep the view in lockstep.
- **Gift card amount semantics** (RP16): the view reports `initial_amount_cents`; the spent-down `balance_cents` is not surfaced here, so "recent sales" reflects the sale, not remaining value. Verify reports that need balance go elsewhere.
- **Cross-tenant read into memory** (RP19): `PendingRefundsCount` uses `ListByStatusAcrossTenants("cancelled")` then filters by tenant in C#. Correct result, but it pulls other tenants' cancelled rows into process memory. Prefer a tenant-scoped query.
- **Unique riders is ticket-only** (RP1): `GetUniqueRiders` counts distinct emails from event tickets only; riders who only bought a season pass/membership are not counted.
- **Timezone resolution fallback** (RP5): an unrecognized tenant IANA timezone silently falls back to UTC (`ResolveTz`), which would mis-bucket daily revenue. Verify tenant timezones are valid IANA ids.
