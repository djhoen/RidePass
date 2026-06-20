# QA Results: Tenant Settings, Branding, Home & Nav

Verified against current code by tracing each Expected result. No product code modified.
Counts: 28 PASS, 2 FAIL, 4 NEEDS-LIVE, 0 N/A (34 cases).

## Admin (tenant_admin, settings.manage)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| TS1.1 | PASS | `UpdateBranding` upserts primary/secondary/accent/tagline/themeMode then returns `GetBranding` which busts cache (TenantController.cs:466-482, 362-365). Store applies themeMode (stores/branding.ts:231-244). Branding row keyed by tenant_id so persistence survives reopen (TenantBrandingRepository.cs:54-77). |
| TS1.2 | PASS | Home/interior split implemented exactly: interior uses `restBg/restFg`; home route uses `homeBg ?? restBg` so NULL home values inherit interior (NavBar.vue:188-194, isHomeRoute 171). Columns persisted via UpdateMetadata (TenantBrandingRepository.cs:54-77). |
| TS1.3 | FAIL | Text falls back to white `#FFFFFF` and no raw NULL leaks (concrete hex strings) - both correct (NavBar.vue:200, 199). BUT the background NULL fallback is a hardcoded brand orange literal `'#FF6B1A'` (NavBar.vue:199), not the tenant's theme primary `branding.primaryColor`. The code comment claims "falls back to the theme primary" (NavBar.vue:168-169) but does not reference primaryColor; a tenant with a customized primary + NULL navBarColor shows orange, not their theme primary. Low severity / cosmetic, but deviates from the stated expectation. |
| TS1.4 | PASS | All six kinds allowed and mapped to columns: logo/logoWhite/favicon/hero/secondaryHero/benefits (TenantController.cs:19-22; TenantBrandingRepository.cs:9-17). Upload stores URL and returns GetBranding (TenantController.cs:522-530). |
| TS1.5 | PASS | New URL stored, then old object deleted (TenantController.cs:523-528); DELETE path clears column then deletes file (554-558). DeleteAsync only removes objects under our bucket prefix (SpacesImageStorage.cs:67-76). |
| TS1.6 | PASS | >5 MB -> 400 "File exceeds 5 MB limit" (TenantController.cs:499-502); unknown content type -> 400 "Unsupported content type" (504-507). Validated before any persist. |
| TS1.7 | PASS | Unknown kind -> 400 "Invalid image kind" (TenantController.cs:489-491). |
| TS1.8 | PASS | UpdateHomeContent persists about/hours/next-up title/whitelist/benefits/sections (TenantController.cs:296-312); GetBranding echoes back (412-415). |
| TS1.9 | PASS | Empty/absent array persisted as NULL: `request.HomeNextUpEventTypeIds is { Length: > 0 } ? ... : null` (TenantController.cs:302-303). "show none" can never be expressed. |
| TS1.10 | PASS | UpdateDailyStatus sets `daily_status_updated_at = CASE WHEN @open IS NULL THEN NULL ELSE now() END` (TenantRepository.cs:351-359); open=true sets the timestamp. 24h fade is a frontend concern. |
| TS1.11 | PASS | UpdateFooter saves all links + refund HTML; blanks normalized to NULL via `Trim` helper (TenantController.cs:322-334, 294). |
| TS1.12 | PASS | lat outside [-90,90] -> 400; lng outside [-180,180] -> 400 (TenantController.cs:267-274). |
| TS1.13 | PASS | XOR check: `request.Latitude.HasValue != request.Longitude.HasValue` -> 400 "must both be provided or both empty" (TenantController.cs:275-279). |
| TS1.14 | NEEDS-LIVE | Logic verified: EnsureTerminalLocation reuses stored id (idempotent), else provisions from address and writes id back (CounterController.cs:926-960); idempotency on retry guaranteed by early-return on stored id (929-932). Actual Stripe Location provisioning requires a live card-present charge. |
| TS1.15 | PASS | Missing line/city/country/postal -> EnsureTerminalLocation returns null (CounterController.cs:934-940); callers degrade to a 400 message, no crash (850-855, 884-888). |
| TS1.16 | PASS | `TimeZoneInfo.FindSystemTimeZoneById` in try/catch -> 400 "Unknown IANA timezone" on TimeZoneNotFoundException; valid id saved and echoed (TenantController.cs:186-200). |
| TS1.17 | PASS | Gift card bounds enforced: min < $1 -> 400, max < min -> 400, max > $10,000 -> 400 (TenantController.cs:207-209). All feature toggles persist and round-trip through GetBranding (432-440). |
| TS1.18 | PASS | Gallery + track-graphic add/edit/delete/reorder all tenant-scoped; delete looks up the row and removes the image file (TenantController.cs:592-621, 653-682; HomePageRepository.cs). Sort orders persist via unnest bulk update (HomePageRepository.cs:50-65, 104-119). |
| TS1.19 | PASS | Every mutation `return await GetBranding()`, which calls InvalidateTenantCache on non-GET (TenantController.cs:362-365); StripeConnect writes call it directly (100, 134, 148). Next request reads fresh value. |

## Super-admin (apex copy + publish + front door)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| TS2.1 | PASS | All discovery queries require `is_published` (DiscoverRepository.cs:49, 116, 147). Unpublished tenant absent from featured/map/search/events feed. |
| TS2.2 | PASS | is_published is discovery-only; subdomain resolution does not consult it (no is_published reference in webapi/Multitenancy). Script0096 documents this as intentional (Script0096_TenantIsPublished.sql:1-12). |
| TS2.3 | PASS | UpdateTenant flips is_published and evicts `tenant:{subdomain}` cache immediately (SuperAdminController.cs:755-771). |
| TS2.4 | PASS | Save preserves image URLs + ForTracks/benefits fields (PlatformBrandingController.cs:79-117); SaveForTracks only touches ForTracks hero + benefits, cannot clobber home fields (132-145). Split is intentional and bidirectional. |
| TS2.5 | NEEDS-LIVE | Front-door fields persist via UpdateAdminDetails and surface on GetBranding (SuperAdminController.cs:751-762; TenantController.cs:445-450). custom_domain_verified defaults false and gates the redirect by design (Script0121_TenantFrontDoor.sql:4-10); actual redirect behavior is a runtime check. |
| TS2.6 | PASS | Testimonials CRUD + reorder all super-admin gated; public Get requests `includeInactive:false` so inactive excluded (PlatformBrandingController.cs:61-62, 228-322). Reorder respected (286-294). |

## Edge / authorization / isolation

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| TS3.1 | PASS | `settings.manage` is in AdminSet only; ManagerSet/CashierSet/ScannerSet/AccountantSet omit it (TenantPermissions.cs:98-120). Every TenantController write requires `Policy.SettingsManage`, so manager/cashier/scanner/accountant get 403. |
| TS3.2 | PASS | Rider role maps to EmptySet (TenantPermissions.cs:60); PUT /Branding requires SettingsManage (TenantController.cs:465-467) -> forbidden. |
| TS3.3 | PASS | GET /Branding has no [Authorize] (TenantController.cs:351-352); returns public projection. |
| TS3.4 | PASS | GetBranding guards `!IsResolved` -> 400 "No tenant resolved" (TenantController.cs:354-357); writes also guard IsResolved or operate on resolved TenantId. |
| TS3.5 | PASS | UpdateGalleryImage/DeleteGalleryImage/UpdateTrackGraphic/DeleteTrackGraphic and bulk reorders all carry `WHERE id = @id AND tenant_id = @tenantId` (HomePageRepository.cs:35-48, 89-102, 50-65, 104-119). Cross-tenant id no-ops; gamma row untouched. |
| TS3.6 | PASS | Branding keyed by tenant_id PK; UpdateMetadata scoped `WHERE tenant_id = @tenantId` (TenantBrandingRepository.cs:54-77). One row per tenant, naturally isolated. |
| TS3.7 | PASS | PlatformBranding PUT/ForTracks/image/testimonials all `[Authorize(Policy = SuperAdminRequirement.PolicyName)]` (PlatformBrandingController.cs:69, 132, 152, 220+). Tenant admin -> 403. |
| TS3.8 | PASS | Storage key is `uploads/{tenantId}/{kind}-{guid}{ext}` (SpacesImageStorage.cs:40-41); SaveAsync called with resolved `_tenantContext.TenantId` (TenantController.cs:522). |
| TS3.9 | FAIL | Inconsistent sanitization. Home.vue sanitizes about/benefits via DOMPurify before v-html (Home.vue:421-422); Footer renders refund HTML through RichTextView which sanitizes (RichTextView.vue:2,13). BUT Event.vue renders `branding.aboutHtml` via `v-html` with NO sanitization (Event.vue:99, 154). Server stores HTML raw (only Trim, TenantController.cs:304-310). A malicious/compromised tenant admin can inject script that executes on the event page. Flagging the unsanitized path. |
