# Highland demo: QuickBooks integration + financial reporting plan

**Date:** 2026-08-17
**Repo:** RidePass (branch `stage`)
**Scope:** what to show Highland Mountain Bike Park, what has to be built first, and how the demo runs.

---

## Status (2026-08-17, end of day)

| Item | State |
| --- | --- |
| 3.1 Intuit app + stage config | DONE. Rob's workspace "Ridepass LLC" + app "Ridepass"; stage redirect URI registered; sandbox keys in `/etc/ridepass/staging.env`; Highland tenant CONNECTED to sandbox realm 9341457734615072 ("Highland Mountain Bike Park (RidePass demo)", 16 park accounts), all 17 required slots mapped (`mappingComplete: true`). **2026-08-18: stage deploy done (0272 + 0273 applied), sync ENABLED, `sync_start_date` set to 2026-07-04 in the DB, Sync now posted 45/45 days success (RP-20260704 .. RP-20260817, QBO JE ids 145 to 189, $1.04M debits, zero errors); the hourly sweep now posts each new day.** End of Day report verified on stage against posted data (8/14: $25.4k, "Posted to QuickBooks RP-20260814"). |
| Deploy | Committed and deployed to stage 2026-08-18 12:05Z (GitHub run 32134797713). Remaining uncommitted: the small gift-card tender count fix in EndOfDay (in progress). |
| 3.2 Gift card liability | DONE in working tree. `Script0273` synthesizes `gift_card_sold` rows in `v_accounting_entries` (depends on Script0272 `imported_from`); `JournalEntryBuilder.AccrueGiftCardSale`; 31/31 builder tests. |
| 3.3 Bike shop + other kinds | DONE. `revenue_bike_shop` (shop_sale + shop_wo_deposit), `revenue_bike_shop_rental`, `shop_rental_deposit` -> forfeited; tax breakout for shop rows; `credit_tender` left as is (see 3.13). |
| 3.4 End of Day report | DONE. `GET api/Reports/Admin/EndOfDay(/Csv)?date=`, `views/Admin/Reports/EndOfDay.vue`, tile + route, print stylesheet, QuickBooks status card. Verified locally by execution; gift-card / deposit / dispute branches verified by reading only (no local data). |
| 3.5 Tax report UI | DONE. `TaxReport.vue` (admission tax + new `GET api/Reports/Admin/SalesTax` by category and by day), CSV. Note Highland has NO admission tax (New Hampshire), only 8.5% Meals & Rentals tax on F&B; the report shows that honestly. |
| 3.6 CSV export | DONE. `helpers/csv.ts` + `webapi/Helpers/CsvWriter.cs`; Export CSV on Sales Summary, F&B Profit, F&B Staff, Void/Comp, Daily Events. PassesSold / TopPassProducts wired to season passes. |
| 3.7 Ledger seed | DONE and RUN ON STAGE. `$hl_ledger$` fragment: 74,649 sales + 16 refunds over 326 business dates, every day of the last 46 populated, 15 paid gift cards + 10 redemptions, cash tender with cashiers, Highland absorbs the 3% cut. Rerun-verified. Side effects: the seed's `$hl_tix$` / `$hl_shop$` wipes were widened to tenant scope to make it rerunnable, which removed the hand-made QA rows on the Highland tenant (2 season passes and 2 rentals under djhoen@gmail.com, 1 package purchase, 11 ledger rows, 1 payout); Highland config changed: F&B tax 0 -> 850 bps, tips enabled; `service_charge_cents` backfilled on seeded purchases. Rehearsal (sync + sandbox P&L) blocked on 3.1. |
| Deploy | All of the above is UNCOMMITTED in the RidePass working tree on branch `stage` (alongside the in-flight gift-card import work). Needs the user's commit + stage deploy, then the migrator applies 0272 + 0273 on stage. |

## 1. Where we are today

### 1.1 The QuickBooks Online integration is complete and production-shaped

The whole path from a sale to a posted journal entry exists and is wired end to end. It was built to production standards, not as a spike.

| Component | File | What it does |
| --- | --- | --- |
| OAuth + token lifecycle | `Services/QuickBooks/QuickBooksTokenService.cs` | Builds the Intuit authorization URL, exchanges the code, refreshes and revokes. Refresh and access tokens are stored AES-encrypted via `EncryptionHelper`. |
| Config | `Services/QuickBooks/QuickBooksOptions.cs` | Reads `QuickBooks:ClientId` / `ClientSecret` / `RedirectUri` / `Environment`. `IsConfigured` requires all three of id, secret, redirect (line 26-27). `IsProduction` switches `ApiBaseUrl` between `sandbox-quickbooks.api.intuit.com` and `quickbooks.api.intuit.com` (lines 29, 39-41). OAuth hosts are shared between sandbox and production. Scope is accounting only. |
| API client | `Services/QuickBooks/QuickBooksApiClient.cs` | Posts `journalentry`, lists the chart of accounts, reads the company name. |
| Journal building | `Services/Accounting/JournalEntryBuilder.cs` | Signed-accumulator double entry. Refunds, partial gift-card tenders and platform charges all fall out of one formula. `Build()` throws `JournalImbalanceException` rather than return an unbalanced draft (lines 103-108). |
| Semantic account slots | `Services/Accounting/QboAccountKeys.cs` | 17 keys (revenue, liability, asset, expense). No QBO account id is ever hardcoded. |
| Per-tenant mapping | `qbo_account_mapping` + `webapi/Controllers/QuickBooksController.cs:378-423` | `RequiredKeys()` gates the slots by the tenant's own feature toggles and Stripe charge mode, so a tenant is never blocked on a slot they cannot use. |
| Read model | `RidePass.Migrator/Scripts/Script0175_AccountingEntriesView.sql`, superseded by `Script0183_AccountingEntriesDepositHold.sql` | `v_accounting_entries`: tenant-local business date, tax/tip/gift-card broken out of gross by proration. |
| Sweep | `TaskRunner/Program.cs:241-268` (`QuickBooksSyncLoop`), `Services/QuickBooks/QuickBooksSyncService.cs` | Hourly tick (tenants span timezones). Posts one JE per tenant-local completed business day, DocNumber `RP-yyyyMMdd`. The loop logs "not configured" and disables itself when the keys are unset (`Program.cs:247`). |
| Double-post guard | `qbo_sync_log` unique index on (tenant_id, business_date), claimed by `TryClaimBusinessDate` before any QBO call (`QuickBooksSyncService.cs:136`) | A day can only be claimed once. |
| Admin UI | `vueapp/src/views/Admin/Settings/QuickBooks.vue`, route `vueapp/src/router/router.ts:262` | Connection / Chart of accounts / Sync history with per-day Retry. |
| Tests | `UnitTests/JournalEntryBuilderTests.cs` | 22 NUnit `[Test]` cases covering balance, tax/tip never revenue, gift cards, direct vs platform charge, deposits. |

**And it is dark.** `QuickBooks__*` is unset in production and, verified today, unset on stage (`/etc/ridepass/staging.env` has no QuickBooks key at all). `Encryption__KeyBase64` and `Encryption__IvBase64` are both set on stage, so the token encryption the flow depends on is ready. No tenant anywhere is connected: `tenant_quickbooks_connection` is empty on stage, `qbo_account_mapping` has 0 rows, `qbo_sync_log` has 0 rows.

### 1.2 Reporting inventory

| Report | Where | Endpoint | Export |
| --- | --- | --- | --- |
| Sales Summary | `views/Admin/Reports/SalesSummary.vue` (tile `sales-summary`) | `GET Reports/Admin/Summary` (`ReportsController.cs:64`) | none |
| Waivers | `views/Admin/Reports/WaiverSignatures.vue` | `GET Reports/Admin/Events/{id}/WaiverSignatures` (`:469`) | none |
| Daily Events | `views/Admin/Reports/DailyEvents.vue` | `GET Reports/Admin/DailyEvents` (`:836`) | none |
| F&B Profit | `views/Admin/Reports/ConcessionProfitability.vue` | `GET Reports/Admin/ConcessionProfitability` (`:156`) | none |
| F&B Staff | `views/Admin/Reports/ConcessionStaff.vue` | `GET Reports/Admin/ConcessionEmployees` (`:229`) | none |
| Void / Comp | `views/Admin/Reports/ConcessionComps.vue` | concession comp endpoints | none |
| Bike Shop (Valuation / Sales / Labor Time / Dead Stock) | `components/bikeshop/ReportsTab.vue` | bike shop report endpoints | CSV (client side) |
| Event Riders / Trackside | `views/Admin/Reports/EventRiders.vue` | `GET Reports/Admin/EventRiders/{id}/Export/Trackside` (`:769`, `File(..., "text/csv", ...)` at `:815`) | CSV |
| Rider Report | `ReportsController.cs:266` / `:316` | `GET Reports/Admin/Riders`, `Admin/RiderDetail` | none |
| Payouts | `views/Admin/Payouts.vue` | `TenantPayoutController.cs:44` | CSV per payout |
| Dashboard | `views/Admin/Dashboard.vue` | dashboard endpoints | none |
| Purchases | `v_recent_sales` | purchases list | none |
| Staff Activity | staff activity view | staff endpoints | none |
| Super-admin only | Analytics, Stripe Reconciliation, Payouts, Refunds, Disputes (`SuperAdminController.cs`, CSV at `:1113`) | not tenant-visible | CSV on payouts |

### 1.3 Reporting gaps

- **No end-of-day / Z report.** Nothing closes a business date for a tenant.
- **Admission tax has an endpoint and no UI.** `GET Reports/Admin/AdmissionTax` (`ReportsController.cs:122`) is fully implemented and never called from the Vue app.
- **Cash has a controller and no UI.** `CashController` exposes `POST Session/Open` (`:46`), `GET Session/Current` (`:74`), `POST TurnIn` (`:88`), `GET TurnIn/Pending` (`:119`), `GET TurnIn/ByEvent/{eventId}` (`:128`), `POST TurnIn/{id}/Confirm` (`:137`), `GET Reconciliation/{eventId}` (`:161`), `GET Reconciliation` (`:178`). No screen calls any of it.
- No gift-card liability report.
- No season-pass deferred-revenue report.
- No tenant-facing Stripe payout reconciliation (super-admin only).
- CSV exists only on Trackside, payouts and bike shop. No PDF or print view anywhere.
- `GetTenantSummary` hardcodes `PassesSold = 0` (`ReportsController.cs:90`) and returns `TopPassProducts = new List<TopProductDto>()` (`ReportsController.cs:104`). Both render as empty or zero tiles on the Sales Summary today.
- `docs/qa/reports-dashboard.md` is stale.

### 1.4 QuickBooks-specific gaps

1. **Gift card liability is only ever debited, never credited.** `JournalEntryBuilder.AccrueSale` debits `liability_gift_card` by `GiftCardAppliedCents` on redemption (`JournalEntryBuilder.cs:136`), which is correct. But the gift card SALE writes no ledger row at all: `StripePurchaseFinalizer.cs:181-201` activates the card, fires the delivery email, and `return`s without touching `_ledger`. `GiftCardRepository.Create` (`:29-44`) only inserts into `gift_card`. Net effect in QBO: the liability account goes negative as cards are redeemed, because nothing ever created the liability.
2. **Six ledger source kinds fall through to `revenue_other` with no tax breakout.** The `CASE l.source_kind` in `Script0183_AccountingEntriesDepositHold.sql:39-47` covers only `event_ticket`, `extras`, `season_pass`, `membership`, `rental`, `concession`. Anything else gets `source_amount_cents = NULL`, which forces `tax_cents` and `tip_cents` to 0, so tax rides into revenue. `QboAccountKeys.RevenueForSourceKind` (`:77-90`) likewise falls through to `RevenueOther`. Affected kinds actually written today: `shop_sale`, `shop_rental`, `shop_rental_deposit`, `shop_wo_deposit`, `credit_tender`. (See section 3.3 and the Part A note on `marketing_automation`.)
3. **`ConnectedByUserId` is always null.** `QuickBooksController.cs:191` sets it explicitly, because the OAuth callback carries no RidePass JWT. The audit trail is `connected_at_utc` only.
4. **Re-posting a corrected day requires a manual JE delete in QBO.** `Resync` refuses a day already marked success and says so (`QuickBooksController.cs:356-360`).

---

## 2. Demo recommendation (DECIDED)

### 2.1 Connect the `highland` stage tenant to an Intuit sandbox company

Create an Intuit Developer app under Rob's existing Intuit login (the loampass.com QuickBooks account), and connect `highland.stage.ridepass.io` to that app's free sandbox company.

Why:

- Every Intuit developer account gets a free sandbox QBO company with the full QuickBooks UI. Chart of Accounts, Journal Entries and the P&L report all behave identically to production. The only visible difference is a "Sandbox" banner.
- It costs nothing.
- It keeps 30 to 45 days of fake Highland journal entries (one per business day, seeded per section 3.7) out of LoamPass's real books, and there is nothing to clean up afterwards.

Rename the sandbox company to **"Highland Mountain Bike Park (RidePass demo)"** in sandbox Company Settings, and pre-create a park-shaped chart of accounts so the mapping screen looks like a real park's books:

| RidePass key | Sandbox account to create |
| --- | --- |
| `revenue_event_ticket` | Lift Ticket Revenue |
| `revenue_season_pass` | Season Pass Revenue |
| `revenue_event_extra` | Camps & Clinics Revenue (see the open question in section 5 about a dedicated key) |
| `revenue_rental` | Rental Revenue |
| `revenue_concession` | Food & Beverage Revenue |
| `revenue_bike_shop` (new, section 3.3) | Bike Shop Revenue |
| `revenue_bike_shop_service` (new, section 3.3) | Bike Shop Service Revenue |
| `liability_gift_card` | Gift Card Liability |
| `liability_sales_tax` | Sales Tax Payable |
| `liability_tips` | Tips Payable |
| `liability_rental_deposit` | Rental Deposits Held |
| `asset_stripe_clearing` | Stripe Clearing |
| `asset_undeposited_cash` | Undeposited Funds (Cash) |
| `expense_stripe_fees` | Stripe Fees |
| `expense_ridepass_fees` | RidePass Fees |
| `asset_ridepass_receivable` | RidePass Receivable |

### 2.2 Explicitly NOT recommended: the real loampass.com production QBO company

Reasons:

- It pollutes real books with demo journal entries that must be hand-deleted, one per business day.
- The hourly sweep keeps posting until someone disables sync, so the pollution grows.
- Intuit production keys are not issued until the app's production settings are complete (EULA URL, privacy policy URL, host domain, and so on), which is a separate approval step, not a checkbox.

Offer it only as a fallback if Highland insists on seeing a non-sandbox company. If that happens: set `sync_start_date` to a 3-day window, disable sync immediately after the call, and delete every `RP-yyyyMMdd` journal entry that was posted (the DocNumber makes them trivially findable).

A fresh QBO trial company is technically possible but becomes a paid subscription after 30 days. Per the standing cost rule, do not sign up for anything billable without the user's explicit approval first, with the price stated.

### 2.3 Prepare both demo modes

- **(a) Pre-connected (recommended default).** The page opens already connected, mapping complete, 30 or more days of green sync history behind it. Nothing can fail live.
- **(b) Live connect.** Click Connect, consent in the Intuit sandbox, land back on our page. A 60-second "look how easy this is" moment. Run this ONLY if the OAuth round trip was rehearsed on stage that same morning.

### 2.4 Demo storyline (5 to 7 minutes)

Slot this immediately after the end-of-day report in the day-in-the-life arc, so the story runs "you closed the day, here is where that day goes."

1. **Settings > QuickBooks.** Connected company name, sync enabled, last synced date.
2. **Chart of accounts panel.** Each RidePass revenue, liability, asset and expense slot mapped to Highland's own QBO accounts. Point out that the mapping is per tenant, gated by their feature toggles, and that no account is hardcoded anywhere in RidePass.
3. **Sync history.** One balanced journal entry per business day, green rows, entry counts and debit totals. Show the Retry control on a failed day.
4. **Switch to the QBO tab.** Open Journal Entry `RP-yyyyMMdd` and walk the lines: Stripe clearing debit, revenue credits by category, sales tax and tips as liabilities, gift card liability draw-down, Stripe fee expense.
5. **QBO Reports > Profit & Loss, last month.** Revenue by category flowing straight from RidePass. Nobody keyed anything.
6. **Close.** "Your accountant logs into QuickBooks and the books are already done. RidePass is the sub-ledger."

### 2.5 Talking points and objection prep

- *What if a day is wrong?* Delete the journal entry in QBO, click Retry in the sync history. The DocNumber is deterministic (`RP-` plus the date), so a duplicate would be obvious on sight and the unique index on `qbo_sync_log` prevents one anyway.
- *Refunds and chargebacks?* Netted into the day they occur. A refund is a sale with a negative gross and flips the same accounts. Dispute fees are expensed.
- *Cash?* Cash tender debits Undeposited Funds, so a cash sale never looks like a card sale.
- *Which Stripe mode?* Both. Direct-charge and platform-charge are read per entry from the payment method snapshotted at charge time, not from the tenant's current setting, so flipping the mode never rewrites history.
- *What is NOT synced?* Customer-level invoices and sales receipts, inventory quantities, payroll. This is a daily summary post, deliberately.
- *Classes and Locations?* Not used today. On the roadmap if they want department reporting through QBO Classes rather than separate revenue accounts.

---

## 3. Pre-demo engineering work

Owner for every item: engineering. Estimates in half-days.

### P0, must land before the demo

#### 3.1 Intuit app + stage config (1 half-day, plus Rob's time)

- Create the Intuit Developer app under Rob's Intuit login (preferred: the company keeps ownership) or ours.
- Register the redirect URI **exactly** as `https://stage.ridepass.io/api/QuickBooks/Callback`. Verified: `QuickBooksController.Callback` is `[HttpGet("Callback")]` on `[Route("api/[controller]")]`, it is `[AllowAnonymous]` because the browser arrives from intuit.com with no JWT, and stage nginx serves `stage.ridepass.io` and `*.stage.ridepass.io` with `location /api` proxied to the webapi on `127.0.0.1:7293`. Intuit allows no wildcards, so the apex is the only workable target.
- Set on `/etc/ridepass/staging.env`: `QuickBooks__ClientId`, `QuickBooks__ClientSecret`, `QuickBooks__RedirectUri=https://stage.ridepass.io/api/QuickBooks/Callback`, `QuickBooks__Environment=sandbox`.
- `Encryption__KeyBase64` and `Encryption__IvBase64` are already set on stage. Confirmed, no action.
- Restart both services on the stage droplet. They run under pm2, not systemd: `pm2 restart stage-webapi stage-taskrunner`.
- Verify the settings page no longer shows "QuickBooks isn't set up on this RidePass deployment yet" (`QuickBooksController.cs:104-105`) and that the TaskRunner log stops printing "QuickBooks sync: not configured" (`TaskRunner/Program.cs:247`).
- After connecting, the browser lands on `https://highland.stage.ridepass.io/Admin/Settings/QuickBooks?qboConnected=1`. Verified: `TenantOrigin()` builds `https://{subdomain}.{App:RootDomain}` and stage has `App__RootDomain=stage.ridepass.io`.

#### 3.2 Fix gap 1: gift card sales must credit `liability_gift_card` (2 half-days)

This one touches money logic. **Fable writes the spec, Opus implements, Fable reviews.**

- Add a `gift_card` (or `gift_card_sale`) `source_kind` ledger row when a gift card purchase completes, for both Stripe and cash tenders. The write belongs alongside the activation in `StripePurchaseFinalizer.cs:181-201`, guarded by the same `justActivated` idempotency the delivery email uses.
- Include the new kind in `v_accounting_entries` with `gross = card value` and no tax.
- Teach `JournalEntryBuilder` to credit `liability_gift_card` and debit the tender account on sale. Redemption already debits, so the two sides finally close.
- Extend the `source_kind` CHECK constraint on `tenant_ledger_entry` (currently expanded piecemeal, most recently in the bike shop and credit tender migrations).
- New unit tests mirroring the existing `JournalEntryBuilderTests` style: sale creates the liability, redemption draws it down, sale plus full redemption nets the liability to zero.
- Backfill migration for existing `gift_card` rows so historical and seeded gift card sales appear in the ledger. Highland currently has zero gift cards on stage, so the backfill is for correctness, not for the demo.

#### 3.3 Fix gap 2: bike shop and other source kinds (2 half-days)

- Add revenue keys `revenue_bike_shop` (retail) and `revenue_bike_shop_service` (labor and work orders). Decide from the source rows whether the ledger can distinguish them: `shop_sale` is written from `ShopStoreController.cs:516`, `BikeShopRegisterController.cs:575/590/867/1068` and `BikeShopWorkOrderController.cs:1200`, so a work-order-originated sale and a counter sale share one kind. If they cannot be split cleanly at the ledger, ship a single `revenue_bike_shop` and note the follow-up.
- Extend `QboAccountKeys.RevenueForSourceKind` for `shop_sale`, `shop_rental`, `shop_rental_deposit`, `shop_wo_deposit`.
- Extend the `CASE` in the view so those kinds carry `source_amount_cents` and get their tax broken out. Today they all return NULL, which silently books their tax as revenue.
- Handle `credit_tender`: this is a customer-credit draw-down, not revenue. It should debit a new `liability_customer_credit` key rather than contra-ing `revenue_other`. The row is written by `StripePurchaseFinalizer.BookCreditTenderEntry` (`:493-505`) with `entry_kind = 'sale'`, `payment_method = 'credit'` and a negative gross.
- `marketing_automation` needs no work. See the Part A note in section 6.
- Add the new keys to `RequiredKeys()` in `QuickBooksController.cs`, gated by the bike shop feature toggle (Highland has `bike_shop_enabled = true` on stage), and to the mapping dropdown in `QuickBooks.vue`.
- Unit tests for every new mapping.
- One new migration, next number is `Script0273_*`. Rerunnable and backwards compatible per the `ridepass-migration` skill. `CREATE OR REPLACE VIEW v_accounting_entries` supersedes `Script0183`.

#### 3.4 End-of-day (Z) report, tenant admin (4 half-days)

The report the demo arc ends on, and it does not exist today.

One tenant-local business date. Contents:

- Gross by revenue category, using the same buckets as the QBO account keys so the report reconciles to the journal entry line for line.
- Tax, tips, discounts and comps, refunds, disputes.
- Tender breakdown: card, cash, gift card, customer credit.
- Stripe fees, net, transaction counts.
- Per-staff and per-register / cash-session sub-table when cash sessions exist.
- A "Posted to QuickBooks: `RP-yyyyMMdd` (success / failed / pending)" line linking through to the sync log.

Sourced from `v_accounting_entries` plus the cash tables. Print stylesheet and CSV export. Route `/Admin/Reports/EndOfDay`, `ReportsView` permission.

#### 3.5 Tax report UI (2 half-days)

- Wire the existing `GET Reports/Admin/AdmissionTax` (`ReportsController.cs:122`) into `ReportsService.ts` and a new `views/Admin/Reports/TaxReport.vue` tile: period picker, per-day or per-event breakdown, totals, CSV.
- Extend it to include sales tax collected by category from `v_accounting_entries.tax_cents`, so the tenant gets one "Tax" report rather than an admission-only one. The existing endpoint deliberately excludes concession sales tax; the new view should show both.

#### 3.6 CSV export on Sales Summary (1 half-day)

Add a shared client-side CSV helper and wire it into Sales Summary first, then F&B Profit, F&B Staff, Void/Comp, Daily Events and Rider Report. The bike shop `ReportsTab.vue` already does this ad hoc and is the pattern to generalize.

#### 3.7 Highland accounting ledger seed (3 half-days) - THE DEMO BLOCKER

`seed-highland.sql` writes purchases, passes, F&B orders, rentals and shop sales, but **zero `tenant_ledger_entry` rows**. `v_accounting_entries` (and therefore the QBO sync, the End of Day report and the tax report) reads only the ledger. Highland today has 11 ledger rows on 2 business dates (2026-07-24, 2026-08-03), about $501 gross, all from manual QA clicks. Without this item there is nothing to post and nothing to show.

Build a new idempotent fragment `$hl_ledger$` at the end of `seed-highland.sql`:

- Wipe: delete every `tenant_ledger_entry` for the highland tenant (demo tenant, all rows are seed-owned; the 11 QA rows go too).
- Generate one `sale` ledger row per seeded purchase across the whole seeded year, derived from the purchase tables the seed already fills, using the same `source_kind` values the live code writes: `event_ticket` (event_ticket_purchase), `extras` (event_extra_purchase), `season_pass` (season_pass_purchase), `membership`, `concession` (concession_sale, with tax and tips), `shop_sale` (bike shop retail + work orders), `shop_rental` (bike shop rentals). Skip `rental` (Highland has `rentals_enabled = false`, the bike shop rental subsystem is what it uses).
- Money columns must match what `StripePurchaseFinalizer` / the controllers write for a `platform` charge-mode tenant: `gross_cents` = amount paid, `stripe_fee_cents` = 2.9% + 30c, `ridepass_cut_cents` from the tenant's configured cut, `net_to_tenant_cents` = gross - fee - cut, `tax_cents` / `tip_cents` from the purchase rows (concession tax + tips, admission tax on tickets), `payment_method` mostly `card` with a realistic share of `cash` on F&B and walk-up tickets so Undeposited Funds gets lines, `occurred_at_utc` = the purchase's paid-at so `business_date` buckets in America/New_York.
- Add refund rows (`entry_kind = 'refund'`, negative gross) for the seeded refunded purchases so the "refunds net into the day" talking point is real.
- After 3.2 lands: seed ~15 gift card sales and ~10 partial redemptions in the last 45 days so Gift Card Liability shows a credit and a draw-down.
- Verify: `select business_date, count(*), sum(gross_cents) from v_accounting_entries where tenant_id = highland group by 1 order by 1 desc limit 45` shows daily activity through yesterday, and every day's `JournalEntryBuilder` result balances (run the sync against the sandbox and check `qbo_sync_log` has zero `failed`).
- Rerun-safe: run twice, row counts identical.

Then the rehearsal (PROTOCOL, because the year fragment is anchored to now() and every rerun regenerates history, while qbo_sync_log marks posted days success and never re-posts them):

1. T-2 days before the demo: change `v_end := v_today_ny - 3` to `- 1` in `$hl_sales_year$` (check the base fragments' fresh demo orders and `$hl_upcoming$` events do not collide with year-fragment events on the same day), re-run the seed on stage so "yesterday" is a full day (today the last 3 days post ~35 entries vs 250 to 590).
2. Add a FRESH sandbox company (developer.intuit.com/sandbox-companies > Add), rename it, recreate the 16 accounts (the Opus browser agent did this in ~20 min), disconnect and reconnect Highland to the new realm (`DELETE api/QuickBooks/Connect`, then Connect), `delete from qbo_sync_log where tenant_id = highland`, re-map (new account ids), set `sync_start_date` = today-45 in the DB, enable sync, POST Sync, confirm all success. Do NOT re-post into the old sandbox: the old JEs would disagree with the regenerated ledger.
3. In the sandbox save a customized P&L filtered to the 16 RidePass accounts ("Highland P&L (RidePass)") so Craig's Landscaping sample data stays out of the slide; screenshot JEs + P&L into HighlandDemoSite/demo-screenshots.
4. Pause sync during the call (or accept that the sweep may post yesterday mid-demo, which is actually a nice moment).

Original rehearsal steps:

- Set `sync_start_date` 30 to 45 days back, run "Sync now", confirm every day is green. There is no max-days cap: `SyncTenantAsync` walks every business date with activity from the cursor to the last complete local day and posts each one (`QuickBooksSyncService.cs:109-121`), so a single "Sync now" catches up the whole window.
- Open the resulting journal entries in the sandbox, run the sandbox P&L, screenshot everything into `HighlandDemoSite/demo-screenshots`.
- Rehearse both the pre-connected and the live-connect modes.
- Fix `PassesSold = 0` (`ReportsController.cs:90`) and the empty `TopPassProducts` (`:104`): either wire them to `season_pass_purchase` or drop the tiles. An obviously-zero tile during a demo is worse than no tile.

### P1, strong to have (before the demo if time allows, otherwise the week after)

#### 3.8 Gift card liability report (1 half-day)

Outstanding balance, sold / redeemed / expired by period. Depends on the ledger rows from 3.2.

#### 3.9 Cash drawer UI (4 half-days)

A screen on top of the existing `CashController`: open session, turn-in, manager confirm, per-event and per-shift reconciliation. Every endpoint already exists (`CashController.cs:46-178`); this is pure frontend plus service wiring. The End of Day report from 3.4 then rolls these up.

#### 3.10 Season pass report (3 half-days)

Passes sold by product and month, dollars collected, redemptions and visits, cost per visit. Plus a simple deferred-revenue schedule (straight-line over the operating season, or per visit) as a CSV an accountant can journal from. Call out plainly that the QBO sync books pass revenue at the point of sale today, so the deferral is currently a manual adjusting entry on their side.

#### 3.11 Tenant-facing Stripe payout reconciliation (3 half-days)

Per payout: the component charges, refunds and fees, and the matching `v_accounting_entries` days. The question "does the bank deposit match RidePass" becomes answerable by the tenant instead of only by super-admin.

#### 3.12 Refunds & disputes report, discounts & coupons report (2 half-days)

Tenant-facing versions of data super-admin already has views over.

#### 3.13 Ledger writer fixes surfaced by the 3.2/3.3 work (2 half-days) - P1, not a demo blocker

Found 2026-08-17 while implementing 3.2/3.3 (Script0273). None of these affect the Highland seed, all affect real tenants once QuickBooks is on:

- `credit_tender` rows written with `net_to_tenant_cents = 0` (`StripePurchaseFinalizer.BookCreditTenderEntry`, `reduceNet: false`, used for BOTH direct-charge card checkouts at `StripePurchaseFinalizer.cs:376` AND the counter cash / fully-credit-covered path at `CounterController.cs:1243`) produce a one-line journal (`DR revenue_other`) and make the whole business day throw `JournalImbalanceException`, so that day never posts. The row carries nothing that says whether the credit offsets Stripe clearing or the cash drawer. Fix the writers to record the counterpart consistently, then add `liability_customer_credit` in the builder (draw-down like a gift card) and a synthesized funding row for credit grants.
- Concession denominator: `StripePurchaseFinalizer.cs:769` books `gross = total - credit_applied` while `v_accounting_entries` prorates tax against `cs.total_cents`, so a store-credit-funded F&B sale understates its tax. Same class of issue 3.3 fixed for `shop_sale`.
- `BikeShopRegisterController.WriteCashLedger` (`:1053`) folds the gift-card float into `net` only in platform mode; a direct-charge tenant's cash + gift-card bike-shop sale now fails the balance check (correctly, rather than posting a wrong till). No tenant is direct-charge today. Record the float identically in both modes.
- Admin void of a paid, live gift card (`GiftCardRepository.VoidActive`) drops the card out of the `gift_card_sold` synthesis instead of booking breakage. Conservative; add a breakage entry later.

#### 3.14 Department revenue split + Revenue by Department report (BUILT 2026-08-20, uncommitted)

Driven by Dave/Highland's 4-business-unit request. Script0274 adds `tenant_event_type.revenue_key` (lesson/camp/clinic backfilled to `revenue_training` for all tenants) and re-creates `v_accounting_entries` with `revenue_key_override` (tickets via tier -> event -> type; extras via their event; NULL elsewhere). New `revenue_training` slot ("Training Center revenue"); `JournalEntryBuilder` and the EOD/Sales-Tax bucketing route by `QboAccountKeys.EffectiveRevenueKey`. `RequiredKeys()` requires the slot only when a tenant maps an event type to it. New report: Reports > Revenue by Department (`GET Reports/Admin/RevenueByDepartment`), tiles + category drill-down + CSV, generic labels via `QboDepartments` (Tickets & Passes / Training Center / Food & Beverage / Bike Shop / Other). 224/224 tests.

DEPLOY NOTE: immediately after 0274 reaches stage, map "Training Center revenue" on the QuickBooks settings screen (Highland goes 17 -> 18 required slots); until mapped, the next sync day fails loudly with "No QuickBooks account is mapped for Training Center revenue". Already-posted days unaffected. Rehearsal re-post will show Training Center split retroactively.

Follow-ups (P1): default `revenue_key` in seed_default_event_types for NEW tenants; event-type admin UI to set the key on custom types; ask Dave whether Find Your Ride's bike-rental add-on belongs to Bike Shop or Training Center.

### P2, roadmap (mention in the demo, do not build now)

- PDF and print packs, plus scheduled email of the End of Day report.
- Accounting export package: CSV of `v_accounting_entries` per period, for accountants who are not on QBO (Xero and similar).
- QBO Class / Location / Department support.
- Per-transaction SalesReceipt mode instead of a daily summary journal.
- `ConnectedByUserId` audit trail.
- Month-over-month and year-over-year comparison in Sales Summary.
- Refresh `docs/qa/reports-dashboard.md`.

### Estimate roll-up

| Tier | Items | Half-days |
| --- | --- | --- |
| P0 | 3.1 - 3.7 (3.7 = ledger seed, 3 half-days) | 15 |
| P1 | 3.8 - 3.13 | 15 |
| **P0 + P1** | | **30** |

---

## 3.9 Live-sync demo setup (added 2026-08-20, DONE and verified on stage)

The user wants to show a sync happening live. That needs PENDING days: ledger activity with no
success row in `qbo_sync_log`. The seed's history ended 2026-08-17 (all 45 days posted as JEs
145-189), and the hourly sweep advances the cursor past empty days, so out of the box there was
nothing left to sync. Setup now in place:

- **`seed-highland-topup.sql`** (repo root, uncommitted, alongside the main seed): fills every
  un-posted business day INCLUDING today-so-far by cloning a same-weekday donor day (7/14/21/28
  days back, first with >= 50 sale rows) - source purchase rows + their `sale` ledger rows, fresh
  ids/tokens/PaymentIntent ids, ~10% of tickets and F&B orders randomly dropped so totals differ
  week over week, plus one gift card sold per day (its sale row is synthesized by the view's Part
  3). Donor refunds and refunded sources are excluded. Each day fills incrementally by local
  time-of-day (up to now for today, the full day for past days), so the End of Day report always
  shows a live in-progress "today" and a same-day rerun just extends it; days with a SUCCESS row
  in `qbo_sync_log` are never touched. It also rewinds `last_synced_date` to the last success day
  so the filled days are visible to the next sync. Run command is in the file header.
- Ran 2026-08-20: filled 8/18 (202 entries, $9,457.37 gross), 8/19 (306 entries, $12,573.58), and
  today 8/20 through 14:41 local (261 entries, $12,286 gross; End of Day API verified showing it).
- **`sync_enabled` set to FALSE** on the Highland connection so the hourly sweep cannot post the
  pending days before the call. The connection stays `active`; the admin page toggle re-enables it.
- Smoke-tested end to end: posted 8/18 live via `POST api/QuickBooks/Resync` -> success, JE 190,
  `RP-20260818`, 202 entries, $9,457.37 total debits, balanced, including the gift-card liability
  line. So the cloned data provably builds and posts a valid journal entry.

**Demo flow for the live sync moment** (slots into storyline step 3): 8/19 (and any newer topped-up
days) sit un-posted. On the Settings > QuickBooks page, flip Sync ON, click Sync now: each pending
day posts in ~0.7s (measured: the 45-day backfill took ~30s, one QBO API call per day), the sync
log rows turn green, then switch to the QBO tab and open the new `RP-yyyyMMdd` entry.

**Morning of the demo**: re-run `seed-highland-topup.sql` (fills through the new yesterday, giving
1+ fresh pending days), confirm sync is still OFF, rehearse the flip-on + Sync now once in a
throwaway sense only if a spare pending day exists - a posted day cannot be un-posted without
deleting the JE in QBO and the log row. **After the call**: flip sync back ON so the sweep resumes.

---

## 4. Demo-day checklist

- [ ] All P0 code deployed to stage and smoke-tested end to end.
- [ ] `QuickBooks__ClientId` / `ClientSecret` / `RedirectUri` / `Environment=sandbox` set in `/etc/ridepass/staging.env`, and `pm2 restart stage-webapi stage-taskrunner` done.
- [ ] `highland` connected, mapping complete (zero unmapped keys on the Status response), sync log green for the last 30 days.
- [ ] Sandbox company renamed to "Highland Mountain Bike Park (RidePass demo)" and its P&L opened and verified.
- [ ] Browser tabs pre-opened: `highland.stage.ridepass.io` logged in as `demo.admin@highland.test`, the QBO sandbox logged in, `HighlandDemoSite` on `localhost:8080` pre-loaded.
- [ ] No impersonation banner active in the RidePass tab.
- [ ] TaskRunner running so nothing surprising posts mid-call, or sync paused for the duration of the call and resumed after. Decide which, do not leave it to chance.
- [ ] Screenshots in `HighlandDemoSite/demo-screenshots` as a fallback if the Intuit sandbox is down.
- [ ] One-line answer rehearsed for "is this the real QuickBooks": yes, it is a QuickBooks Online sandbox company, same product, same API, and connecting a production company is the same button with production keys.

---

## 5. Open questions for the user and Rob

1. **Demo date.** Drives the seed refresh window and which P1 items make the cut.
2. **Who creates the Intuit developer app?** Rob under the loampass.com Intuit login is recommended, so the company owns it. Either way we need sandbox client id and secret now, and production keys later.
3. **Does Highland want department reporting through QBO Classes** rather than separate revenue accounts? This is a roadmap decision, not a demo blocker, but the answer changes how much of the chart of accounts we pre-build.
4. **Camps and clinics:** their own revenue line (a new `revenue_camps` key) or folded under event tickets and extras?
5. **Cash handling at Highland:** do they need the cash drawer UI for the demo? If yes, 3.9 is promoted to P0 and adds 4 half-days.

---

## 6. Part A verification notes that qualify the plan above

Recorded here so nobody re-derives them.

- The live definition of `v_accounting_entries` is `Script0183_AccountingEntriesDepositHold.sql`, not `Script0175`. `Script0175` created it; `Script0183` replaced it to add the rental deposit hold lifecycle. Any edit to the `CASE` must go in a new script, currently `Script0273_*`.
- `marketing_automation` does not actually leak into `revenue_other`. It is written with `entry_kind = 'email_charge'` (`webapi/Workers/MarketingAutomationSweep.cs:211-215`), and `JournalEntryBuilder.IsPlatformCharge` (`:62-63`) short-circuits on that entry kind and books it to `expense_ridepass_fees` before `RevenueForSourceKind` is ever consulted. No work needed.
- `credit_tender` does leak. It is written with `entry_kind = 'sale'` and a negative gross, so it takes the normal revenue path and debits `revenue_other`.
- There is no max-days cap in `SyncTenantAsync`. "Sync now" will catch up an arbitrarily long backlog in one call.
- `Encryption__KeyBase64` and `Encryption__IvBase64` are set on stage. `App__RootDomain=stage.ridepass.io`.
- Stage runs under pm2 (`stage-webapi`, `stage-taskrunner`, `stage-vueapp`), not systemd.
- `JournalEntryBuilderTests.cs` has 22 `[Test]` cases, not 21.
