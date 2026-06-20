# QA Results: Super Admin & Platform

Verified against current code by tracing each Expected result. No product code modified.
Counts: 42 PASS, 0 FAIL, 7 NEEDS-LIVE, 0 N/A (49 cases).

## Super-admin (provisioning + users)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SA1.1 | PASS | Bootstrap returns 400 "A super admin already exists" when `AnySuperAdminExists()`; no user created (SuperAdminController.cs:100-103). |
| SA1.2 | PASS | CreateTenant inserts then DB triggers seed type-appropriate defaults; MX else-branch seeds 6 event types and extras Gate Fee/Camping/Parking/Pit Vehicle (Script0125_TenantTypeDefaults.sql:28-35, 58-62); waiver + pass-product seed functions exist (Script0078/0118). Branding auto-seeds (Script0002). Audit `tenant.create` written (SuperAdminController.cs:246-247). |
| SA1.3 | PASS | bike_park seeds access day "Trail Day" + race/practice + "Clinic", extras Day Pass/Parking/Camping, and membership renamed "Park Membership" (Script0125_TenantTypeDefaults.sql:18-26, 47-56, 71-79). |
| SA1.4 | PASS | `VenueCategory = request.TenantType == "mountain_bike" ? request.VenueCategory : null` (SuperAdminController.cs:222); CHECK constraint also bounds it (Script0125:9-10). |
| SA1.5 | PASS | GetBySubdomain pre-check -> 400 "already taken", no insert (SuperAdminController.cs:205-209). |
| SA1.6 | PASS | FindSystemTimeZoneById try/catch -> 400 "Unknown IANA timezone" (SuperAdminController.cs:198-202). |
| SA1.7 | PASS | tenant_admin created with generated temp password returned once (SuperAdminController.cs:266-281); welcome email with login deep link + reset link sent when emailer configured (284-299); AdminEmail without first/last -> 400 (261-264). |
| SA1.8 | PASS | Creates global super_admin (TenantId null); duplicate global email -> 400 (SuperAdminController.cs:320-336). |
| SA1.9 | PASS | tenant user -> super_admin blocked (416-419); global super_admin -> tenant role blocked (420-423); unknown role/status -> 400 (406-414); same-scope email collision -> 400 (427-435). |
| SA1.10 | PASS | Impersonate issues 1-hour token with `impersonatedBy = currentSuperAdminId` (SuperAdminController.cs:516-524); returns target tenant scope. |
| SA1.11 | PASS | target.Role == "super_admin" -> 400 "Cannot impersonate another super admin" (SuperAdminController.cs:511-514). |
| SA1.12 | PASS | UpdateMiscSettings normalizes + drops malformed + de-dupes via EmbedPolicy.NormalizeList, then busts the /embed CSP cache key (SuperAdminController.cs:139-149). |
| SA1.13 | PASS | Off staging `!_stageMirror.Available` -> 400 "not available in this environment" (SuperAdminController.cs:171-174). Actual staging job progress is runtime but the gate is verified. |

## Super-admin (money: service charge, payouts, refunds)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SA2.1 | PASS | UpdateServiceCharge persists bps + cap; audit `tenant.serviceCharge.update` with new values (SuperAdminController.cs:689-711). Ledger rows are snapshotted at sale time (per-sale rider bps), so existing entries are unaffected. |
| SA2.2 | PASS | Creates pending payout, AttachUnpaidEntries in range, RefreshTotals, audits attached count + net (SuperAdminController.cs:810-827). |
| SA2.3 | PASS | `PeriodEndUtc <= PeriodStartUtc` -> 400 (SuperAdminController.cs:801-804). |
| SA2.4 | NEEDS-LIVE | Requires Connect active (verified, see SA2.5), creates Stripe Transfer with idempotency `payout-{id}`, marks paid immediately, notifies tenant admins (SuperAdminController.cs:862-905). Actual Stripe Transfer is runtime. Note: the XML doc-comment says "marks processing" but the code marks `paid` (890) - doc drift only, behavior matches the plan. |
| SA2.5 | PASS | No account or status != "active" -> 400 "doesn't have an active Stripe Connect account"; no transfer attempted (SuperAdminController.cs:853-857). |
| SA2.6 | PASS | Non-pending -> 400 "only 'pending' payouts can be sent" (842-845); NetPaidCents <= 0 -> 400 "zero or negative" (846-849). |
| SA2.7 | PASS | status paid without PayoutDateUtc -> 400 (917-920); first transition to paid notifies tenant admins once via `existing.Status != "paid"` guard (937-945). |
| SA2.8 | PASS | First transition to failed emits `payout_failed` to super admins (SuperAdminController.cs:948-957). |
| SA2.9 | PASS | Void requires pending else 400 "Only pending payouts can be voided"; Void releases entries (SuperAdminController.cs:962-976). |
| SA2.10 | NEEDS-LIVE | Refundable cents via RefundCalculator withholds rider-paid service charge (RefundCalculator.cs:17-22); marks refunded, writes negative mirror ledger entry, notifies tenant admins (SuperAdminController.cs:597-618, 631-650). Actual Stripe refund call is runtime. |
| SA2.11 | PASS | refundCents <= 0 -> 400 "Nothing to refund"; no Stripe call (SuperAdminController.cs:601-604). |
| SA2.12 | PASS | Empty StripePaymentIntentId -> 400 "no Stripe payment_intent to refund" (SuperAdminController.cs:592-595). |
| SA2.13 | PASS | Reconciliation excludes non-Stripe rows: SumForPeriod filters `payment_method = 'stripe'`, so `stripe_connect` (and cash/voucher) rows are out of the ledger total compared to the platform Stripe balance (TenantLedgerRepository.cs:82-96; gap math SuperAdminController.cs:1005-1018). Actual Stripe balance-transaction figures are runtime. |
| SA2.14 | PASS | Balances returns per-tenant summaries (682-687); Analytics returns totals + daily + breakdown with `toUtc <= fromUtc` -> 400 (1041-1046). |
| SA2.15 | PASS | CSV built from the payout's entries; filename from PayoutCsvBuilder.FilenameFor(payout, subdomain) (SuperAdminController.cs:988-999). |

## Stripe Connect onboarding (tenant side, settings.manage)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SA3.1 | NEEDS-LIVE | First time creates a Standard account, stores id + status "pending", returns hosted onboarding URL with return/refresh links to /Admin/Settings/Payments (TenantController.cs:85-116). Live Stripe account creation is runtime; logic verified. |
| SA3.2 | PASS | Onboard reuses existing `StripeConnectAccountId` when present; no second account created (TenantController.cs:93-105). |
| SA3.3 | NEEDS-LIVE | Refresh re-polls status, persists, busts cache, reflected in GetBranding (TenantController.cs:123-136; 406-407). Actual KYC/status from Stripe is runtime. |
| SA3.4 | PASS | Test catches StripeException -> readable 400, other Exception -> 400, never 500 (TenantController.cs:157-179). |
| SA3.5 | PASS | DELETE clears the link on our side only (ClearStripeConnect), does not delete the Stripe account (TenantController.cs:143-150). |

## Promotion / sync (stage -> prod)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SA4.1 | PASS | StageTenants proxies staging's unpublished list via machine-auth; `!_client.IsConfigured` -> 400 (TenantPromotionController.cs:29-45). Source side returns only `!IsPublished` tenants (TenantSyncController.cs:39-53). |
| SA4.2 | PASS | confirm=false returns Status "preview", Mode "create" for a tenant not on prod, with headline counts events/tiers/add-ons/passes/images; no write (TenantPromotionService.cs:85-109, HeadlineCounts 240-252). |
| SA4.3 | PASS | confirm=true imports transactionally (ImportTables), then ApplyTenantResets nulls all NullKeys, forces is_published=false, custom_domain_verified=false, embed_enabled=false, sms_enabled=false, client_type='hosted', embed_event_target='external'; images copied to prod bucket with URLs rewritten (TenantPromotionService.cs:111-166, 125-148). |
| SA4.4 | PASS | Schema version compared exactly -> Block "Schema mismatch ... Deploy prod first" (TenantPromotionService.cs:71-75). |
| SA4.5 | PASS | Subdomain owned by a different prod tenant -> Block "already belongs to a different tenant on prod" (TenantPromotionService.cs:78-82). |
| SA4.6 | PASS | existing.FirstPublishedAt not null -> Block "has been published on prod before" (TenantPromotionService.cs:92-95). |
| SA4.7 | PASS | CountLiveOrders > 0 -> Block "has N live order(s)" (TenantPromotionService.cs:96-100). |
| SA4.8 | PASS | Export of a published tenant -> 400 "Only unpublished tenants can be exported" (TenantSyncController.cs:66). |

## Edge / authorization / isolation

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| SA5.1 | PASS | Every SuperAdminController endpoint (except [AllowAnonymous] Bootstrap) carries `[Authorize(Policy = SuperAdminRequirement.PolicyName)]`; handler requires claim role == "super_admin" (SuperAdminRequirement.cs:12-19). Tenant admin -> 403 on Tenants/Balances/PUT. |
| SA5.2 | PASS | GetById(payoutId, tenantId) scopes `WHERE id = @id AND tenant_id = @tenantId` (TenantPayoutRepository.cs:46-52); a payout under the wrong tenant returns null -> NotFound. SendViaStripe/Status/Void/CSV/Get all go through GetById first (SuperAdminController.cs:840, 922, 966, 982, 992). |
| SA5.3 | PASS | Ledger ListByTenant(tenantId, ...) is tenant-parameterized (SuperAdminController.cs:783-785); each tenant returns only its rows. |
| SA5.4 | PASS | TenantSyncController is `[TenantSyncAuth]` only (no [Authorize] JWT); requires X-Tenant-Sync-Key constant-time match + prod IP allowlist; unset key -> 404 (TenantSyncAuthAttribute.cs:20-52). A super-admin JWT alone is rejected (401). |
| SA5.5 | PASS | TenantPayoutController `[Authorize(Policy = ReportsView)]`, all reads scoped to resolved tenant (TenantPayoutController.cs:17, 47-79). reports.view is in Admin/Manager/Accountant sets but not Cashier/Scanner (TenantPermissions.cs:107-120) -> cashier/scanner 403. |
| SA5.6 | PASS | Audit written for create (246), serviceCharge.update (700), refund.process (613) with refund money metadata, payout.create (825), payout.stripeTransferSent with money metadata (894). AuditLog endpoint exposes them with tenant id + actor (1023-1037). |
| SA5.7 | PASS | CouponShares: tenantId filter returns one tenant's recipients; unfiltered spans ListAll (SuperAdminController.cs:1141-1164). Super-admin gated. |
| SA5.8 | PASS | Reconciliation `toUtc <= fromUtc` -> 400 (1005); Analytics `toUtc <= fromUtc` -> 400 (1043-1046). |
