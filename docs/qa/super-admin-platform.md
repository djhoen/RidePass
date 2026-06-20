# QA Test Plan: Super Admin & Platform

> Scope: platform bootstrap, tenant provisioning + type-defaults seeding, super-admin user management + impersonation, platform settings, Stripe Connect onboarding state, tenant payouts / billing / ledger / reconciliation, refunds + disputes across tenants, and stage-to-prod tenant promotion/sync. Last updated: 2026-06-20.

## Surface map
- **SuperAdminController** (every endpoint gated by `SuperAdminRequirement.PolicyName`, except `Bootstrap`):
  - Bootstrap: `POST /api/SuperAdmin/Bootstrap` (`[AllowAnonymous]`, one-time).
  - Tenants: `GET /Tenants`, `POST /Tenants` (provision), `PUT /Tenants/{id}` (full edit incl. publish + features + address + service charge), `PUT /Tenants/{id}/ServiceCharge`, `PUT /Tenants/{id}/ConcessionsEnabled`.
  - Users: `GET /Users`, `GET /Users/{id}`, `PUT /Users/{id}`, `POST /SuperAdmins`, `POST /Impersonate/{userId}`.
  - Money: `GET /Balances`, `GET /Tenants/{id}/Ledger`, `GET|POST /Tenants/{id}/Payouts`, `POST /Tenants/{id}/Payouts/{pid}/SendViaStripe`, `PUT /Tenants/{id}/Payouts/{pid}/Status`, `DELETE /Tenants/{id}/Payouts/{pid}` (void), `GET .../{pid}`, `GET .../{pid}/Csv`, `GET /Refunds`, `POST /Refunds/Ticket/{id}/Process`, `GET /Disputes`, `GET /Reconciliation`, `GET /Analytics`.
  - Platform: `GET|PUT /Settings/Misc` (global embed allow-list - Script0123), `GET /StageMirror/Status`, `POST /StageMirror/Refresh` (staging-only), `GET /AuditLog`, `GET /Marketing/CouponShares`.
- **Stripe Connect onboarding** (on `TenantController`, `settings.manage`): `POST /api/Tenant/StripeConnect/Onboard`, `/Refresh`, `/Test`, `DELETE /api/Tenant/StripeConnect` (Script0036). Statuses: `pending` / `active` / `restricted`.
- **Tenant-facing billing read** (`TenantPayoutController`, `reports.view`): `GET /api/TenantPayout/{Balance,Ledger,Payouts,Payouts/{id},Payouts/{id}/Csv}`.
- **Promotion / sync:** `TenantSyncController` (SOURCE, on staging, `[TenantSyncAuth]` machine key + IP allowlist, read-only) `GET /Tenants`, `GET /Export/{id}`; `TenantPromotionController` (DEST, on prod, super-admin) `GET /StageTenants`, `POST /Promote/{stageTenantId}?confirm=`. Orchestrated by `webapi/Sync/TenantPromotionService.cs`.
- **Migrations:** Script0078/0125 (tenant type + venue category + seed triggers), Script0016 (fee schedule / ledger / payouts), Script0036 (Stripe Connect), Script0084/0085 (tenant billing events -> ledger netting), Script0123 (platform_setting).

## Concepts under test
- **Bootstrap is one-shot:** `POST /Bootstrap` creates the first `super_admin` (TenantId=null) and refuses once any super admin exists. Anonymous because there is no one to authenticate as yet.
- **Provisioning seeds by type:** `POST /Tenants` inserts the tenant; DB AFTER-INSERT triggers `seed_default_event_types`, `seed_initial_waiver`, `seed_default_pass_products`, `seed_default_extra_products`, and `set_tenant_type_membership_name` branch on `tenant_type` (and `venue_category` for MTB) to seed type-appropriate defaults. Branding row also auto-seeds (Script0002). `venue_category` is MTB-only and is nulled for MX so a stray value can't stick.
- **Global vs tenant identity:** super admins and riders live in the global user pool (`tenant_id` NULL, email unique globally); tenant staff live in the tenant pool (email unique per tenant). `UpdateUser` enforces that a tenant user cannot become super_admin and a global super_admin cannot be demoted into a tenant role here.
- **Impersonation:** issues a 1-hour JWT for the target with an `impersonatedBy` claim; cannot target another super admin.
- **Service charge is super-admin owned:** `service_charge_bps` (default 300 = 3%) + optional `monthly_service_charge_cap_cents` (Script0026) are set per tenant by the super admin; each sellable item carries the rider-paid share bps.
- **Ledger + payouts:** every sale writes one immutable `sale` ledger entry (unique per source); refunds/adjustments write negative mirror rows. A payout attaches unpaid entries in a period, refreshes totals, and is sent either manually (status flip) or via Stripe Transfer. `SendViaStripe` requires `stripe_connect_status='active'` and marks `paid` immediately (Transfer is settlement-synchronous), with `payout-{id}` idempotency.
- **Reconciliation:** compares platform Stripe balance-transaction totals to ledger sums; Connect-routed charges (`payment_method='stripe_connect'`) land in the tenant's own bank and are excluded from the platform comparison.
- **Promotion guard rails:** schema versions must match exactly; subdomain must not belong to a different prod tenant; a tenant that has ever been published (`first_published_at`) or has live orders cannot be replaced. On import, environment-specific columns (`NullKeys`: Stripe/Twilio/domain/daily-status) are reset, `is_published=false`, embed/sms disabled, `client_type='hosted'`.

## Preconditions / test data
- A platform with exactly one existing super admin (so Bootstrap re-run is exercised against the refusal path).
- Two MX tenants and one MTB tenant (one MTB `bike_park`, to verify category-specific seed names like "Trail Day"/"Park Membership").
- A tenant with a completed Stripe Connect account (`active`), one `pending`, and one with none.
- A tenant with unpaid ledger entries spanning a known period (for payout drafting) and at least one cancelled ticket with a Stripe PaymentIntent (for the refund queue).
- Staging reachable from prod with `TenantSync:SourceBaseUrl` + key configured, and at least one unpublished stage tenant and one stage tenant that already exists published on prod (to hit the block path).
- A tenant_admin token (non-super-admin) for authorization-boundary checks.

---

## Super-admin (provisioning + users)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SA1.1 [NN] | Bootstrap refuses when admin exists | `POST /Bootstrap` while a super admin exists | 400 "A super admin already exists". No user created. |
| SA1.2 [NN] | Provision MX tenant + seed defaults | `POST /Tenants` with `tenantType=motocross`, valid subdomain + timezone | Tenant created; seeded with the 6 MX event types, placeholder waiver, inactive Day Pass, MX extras (Gate Fee/Camping/Parking/Pit Vehicle), branding row. Audit `tenant.create` written. |
| SA1.3 [NN] | Provision MTB tenant by category | `POST /Tenants` `tenantType=mountain_bike`, `venueCategory=bike_park` | Seeded with MTB event types (access day "Trail Day", race, practice, clinic), MTB extras (Day Pass/Parking/Camping), membership renamed "Park Membership". |
| SA1.4 [NN] | venue_category ignored for MX | `POST /Tenants` MX with a `venueCategory` set | Stored `venue_category` is NULL (MTB-only). |
| SA1.5 [NN] | Subdomain uniqueness | `POST /Tenants` reusing an existing subdomain | 400 "already taken". No insert. |
| SA1.6 [NN] | Timezone validation on create | `POST /Tenants` `timezone="Nowhere/Bogus"` | 400 "Unknown IANA timezone". |
| SA1.7 [NN] | Provision with first admin | `POST /Tenants` with `adminEmail`+first/last | tenant_admin created with a generated temp password returned once; welcome email sent (deep link to subdomain login + reset). Missing first/last with email -> 400. |
| SA1.8 [NN] | Create additional super admin | `POST /SuperAdmins` new email | Created global super_admin (tenant_id NULL); duplicate email -> 400. |
| SA1.9 [NN] | UpdateUser role/scope guards | `PUT /Users/{id}` try to set a tenant user to `super_admin`; try to demote a global super_admin to a tenant role | Both 400. Unknown role/status -> 400. Email collision within the same scope -> 400. |
| SA1.10 [NN] | Impersonate a tenant user | `POST /Impersonate/{userId}` for a tenant_admin | Returns a 1-hour token carrying `impersonatedBy` = caller; can act as that tenant. |
| SA1.11 [NN] | Cannot impersonate a super admin | `POST /Impersonate/{superAdminId}` | 400 "Cannot impersonate another super admin." |
| SA1.12 [R] | Platform Misc settings | `PUT /Settings/Misc` global embed origins (mix valid + malformed) | Malformed dropped, normalized + de-duped; `/embed` CSP cache busted so change is live. |
| SA1.13 [R] | Stage mirror gating | `POST /StageMirror/Refresh` off staging | 400 "not available in this environment"; on staging it starts the job and `Status` reflects progress. |

---

## Super-admin (money: service charge, payouts, refunds)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SA2.1 [NN] | Set per-tenant service charge | `PUT /Tenants/{id}/ServiceCharge` bps + monthly cap | Persisted; audit `tenant.serviceCharge.update` with old/new. New sales price at the new bps; existing ledger rows unchanged (snapshotted). |
| SA2.2 [NN] | Draft a payout | `POST /Tenants/{id}/Payouts` for a period with unpaid entries | Payout `pending`; unpaid entries in range attached; totals refreshed; audit records attached count + net. |
| SA2.3 [NN] | Period validation | `POST .../Payouts` with end <= start | 400. |
| SA2.4 [NN] | Send payout via Stripe (active) | `POST .../Payouts/{pid}/SendViaStripe` on an `active` Connect tenant | Stripe Transfer created with idempotency `payout-{id}`; payout flips `paid` immediately; tenant admins notified. |
| SA2.5 [NN] | Send blocked without active Connect | `SendViaStripe` on a `pending`/no-Connect tenant | 400 "doesn't have an active Stripe Connect account". No transfer. |
| SA2.6 [NN] | Send blocked for non-pending / zero | `SendViaStripe` on a `paid` payout, then on a zero-net payout | 400 "only 'pending' can be sent"; 400 "net amount is zero or negative". |
| SA2.7 [NN] | Manual payout status to paid | `PUT .../Payouts/{pid}/Status` paid without `payoutDateUtc` | 400 (date required). With date: flips paid, notifies tenant admins once (only on first transition to paid). |
| SA2.8 [NN] | Payout failed notifies super admins | `PUT .../Status` -> failed | Super admins get a `payout_failed` notification for investigation. |
| SA2.9 [NN] | Void only-pending payout | `DELETE .../Payouts/{pid}` on pending, then on paid | Pending voids (entries released); paid -> 400 "Only pending payouts can be voided". |
| SA2.10 [NN] | Process ticket refund | `POST /Refunds/Ticket/{id}/Process` on a cancelled ticket with a PaymentIntent | Refund = refundable cents (rider-paid service charge withheld via `RefundCalculator`); ticket marked refunded; **negative mirror ledger entry** written; tenant admins notified. |
| SA2.11 [NN] | Refund nothing-to-refund | Process a ticket whose refundable cents <= 0 | 400 "Nothing to refund". No Stripe call. |
| SA2.12 [NN] | Refund without PaymentIntent | Process a cancelled cash/voucher ticket | 400 "no Stripe payment_intent to refund". |
| SA2.13 [R] | Reconciliation gaps | `GET /Reconciliation?fromUtc&toUtc` | Returns Stripe vs ledger gross/fee/net gaps; Connect-routed (`stripe_connect`) sales excluded from the platform Stripe comparison so they don't show as a phantom gap. |
| SA2.14 [R] | Balances + analytics | `GET /Balances`; `GET /Analytics` for a range | Per-tenant balance summaries; platform totals + daily + per-tenant breakdown; `toUtc<=fromUtc` -> 400. |
| SA2.15 [R] | Payout CSV | `GET /Tenants/{id}/Payouts/{pid}/Csv` | CSV with the payout's entries; filename includes subdomain. |

---

## Stripe Connect onboarding (tenant side, settings.manage)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SA3.1 [NN] | Start onboarding (first time) | `POST /api/Tenant/StripeConnect/Onboard` with no account | Creates a Standard account, stores id + status `pending`, returns a Stripe-hosted onboarding URL with return/refresh links to the Payments settings page. |
| SA3.2 [NN] | Onboarding reuses account | Call Onboard again before completing | Reuses the existing account id; no second account created. |
| SA3.3 [NN] | Refresh status | Complete KYC on Stripe, then `POST .../Refresh` | Status repolled and persisted (`active`/`restricted`); cache busted; reflected in `GetBranding`. |
| SA3.4 [NN] | Test connection | `POST .../Test` | Success returns the no-op result; a Stripe error returns a readable 400 (not a 500). |
| SA3.5 [R] | Disconnect | `DELETE /api/Tenant/StripeConnect` | Clears the link on our side (does not delete the tenant's Stripe account). |

---

## Promotion / sync (stage -> prod)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SA4.1 [NN] | List stage tenants | `GET /api/TenantPromotion/StageTenants` (prod, super admin) | Returns staging's unpublished tenants (proxied via machine-auth to staging's `TenantSync/Tenants`). Unconfigured -> 400. |
| SA4.2 [NN] | Preview create | `POST /Promote/{stageId}?confirm=false` for a tenant not on prod | Status `preview`, mode `create`, headline counts (events/tiers/add-ons/passes/images). No write. |
| SA4.3 [NN] | Confirm create + resets | `POST /Promote/{stageId}?confirm=true` | Imports transactionally; tenant lands `is_published=false`, embed/sms disabled, `client_type='hosted'`, all `NullKeys` (Stripe/Twilio/domain/daily-status) nulled; image bytes copied into the prod bucket with URLs rewritten. |
| SA4.4 [NN] | Schema mismatch blocks | Promote when stage schema != prod schema | Status `blocked`, reason "Schema mismatch ... Deploy prod first". |
| SA4.5 [NN] | Subdomain owned by other tenant | Promote a stage tenant whose subdomain belongs to a different prod tenant | `blocked` "already belongs to a different tenant on prod". |
| SA4.6 [NN] | Ever-published cannot be replaced | Promote over a prod tenant with `first_published_at` set | `blocked` "has been published on prod before". |
| SA4.7 [NN] | Live orders block replace | Promote over a prod tenant with live orders | `blocked` "has N live order(s)". |
| SA4.8 [NN] | Source export requires unpublished | `GET /api/TenantSync/Export/{id}` for a published stage tenant | 400 "Only unpublished tenants can be exported." |

---

## Edge / authorization / isolation

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| SA5.1 [NN] | Tenant admin blocked from SuperAdmin API | As tenant_admin call `GET /api/SuperAdmin/Tenants`, `/Balances`, `PUT /Tenants/{id}` | 403 on every SuperAdmin endpoint. The policy is the only gate; none may leak. |
| SA5.2 [NN] | Payout cross-tenant id mismatch | `GET /Tenants/{tenantA}/Payouts/{payoutOfTenantB}` | NotFound - `GetById(payoutId, tenantId)` scopes by tenant; a valid payout id under the wrong tenant must not resolve. Repeat for SendViaStripe / Status / void / CSV. |
| SA5.3 [NN] | Ledger scoped per tenant | `GET /Tenants/{id}/Ledger` for two tenants | Each returns only its own entries; no bleed. |
| SA5.4 [NN] | TenantSync rejects browser auth | Call `GET /api/TenantSync/Tenants` with a super-admin JWT (no machine key) | Rejected - `[TenantSyncAuth]` requires the shared key + prod IP allowlist, not a JWT. |
| SA5.5 [NN] | Tenant billing read scope | As tenant_admin, `GET /api/TenantPayout/Balance` and `/Ledger` | Returns only the resolved tenant's data (requires `reports.view`); cashier/scanner without reports.view -> 403. |
| SA5.6 [R] | Audit trail coverage | After provisioning, service-charge change, refund, payout send | `GET /AuditLog` shows each action with tenant id + actor; refunds and payouts carry money metadata. |
| SA5.7 [R] | Marketing coupon shares scope | `GET /Marketing/CouponShares?tenantId=` and unfiltered | Filtered returns one tenant's recipient emails; unfiltered spans all (super-admin only). |
| SA5.8 [NN] | Reconciliation/analytics range guard | Call with `toUtc<=fromUtc` | 400 on both. |

## Known risks / watch-items
- **Authorization boundary is the whole game:** every SuperAdmin, PlatformBranding-write, and TenantPromotion endpoint relies on `SuperAdminRequirement.PolicyName`; a single missing `[Authorize]` exposes cross-tenant money/PII. `Bootstrap` is intentionally anonymous but self-disables after the first super admin (SA1.1) - verify it can never re-open.
- **Multi-tenant isolation in money paths:** payout reads/writes pass `(payoutId, tenantId)`; the refund queue and disputes deliberately span tenants (super-admin views) but per-tenant ledger/payout/CSV must stay scoped (SA5.2/SA5.3). A payout id is a GUID, but never trust it without the tenant predicate.
- **Money correctness:** `SendViaStripe` marks `paid` immediately and relies on `payout-{id}` idempotency to avoid a double Transfer on retry; refunds withhold the rider-paid service charge and must write the negative mirror ledger row or balances drift; reconciliation must exclude `stripe_connect` charges from the platform comparison.
- **Connect status gating:** payouts via Stripe require `stripe_connect_status='active'`; `restricted` falls back to the platform account for charges but must not be treated as payable.
- **Provisioning trigger drift:** seed defaults live in DB triggers (Script0078/0125), not C#; a schema change that drops/renames a trigger silently breaks new-tenant seeding without a code error. Verify event types / extras / waiver / membership name after every migration touching those functions.
- **Promotion safety:** the create/replace/blocked guard rails (schema match, subdomain ownership, ever-published, live orders) are the only thing preventing a stage import from clobbering live prod data. `NullKeys` + forced `is_published=false` must reset every environment-specific column; a new sensitive column added to `tenant` should be added to `NullKeys` or it will carry stage values into prod.
- **Impersonation:** the issued token must carry `impersonatedBy` and a short (1h) expiry so impersonated actions are attributable and time-boxed; confirm it cannot target a super admin.
- **Cache + publish:** `UpdateTenant` busts `tenant:{subdomain}` so a publish flip is immediate; a missed eviction lags discovery visibility up to 5 minutes.
