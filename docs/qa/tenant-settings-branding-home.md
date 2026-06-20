# QA Test Plan: Tenant Settings, Branding, Home & Nav

> Scope: tenant-level settings (timezone, feature toggles, location, service charge), branding + theme + nav colors, public home page content + "Next Up" config, published vs unpublished discovery visibility, front-door / embed config, and the permission policies that gate every admin surface. Last updated: 2026-06-20.

## Surface map
- **Admin (tenant):** `TenantController` (all writes require `TenantPerm:settings.manage`):
  - Settings: `PUT /api/Tenant` (timezone, require-reservation, require-emergency-contact, allow-event-subscriptions, require-id-at-checkin); feature toggles `PUT /api/Tenant/{GiftCardSettings,RentalsEnabled,ExtrasEnabled,SeasonPassesEnabled,ConcessionsEnabled,BlogEnabled,CancellationPolicy}`.
  - Location: `PUT /api/Tenant/Location` (Script0011) - drives Stripe Terminal location + discovery geo.
  - Branding: `PUT /api/Tenant/Branding` (colors, tagline, theme mode, nav colors - Script0002/0092/0093); `POST|DELETE /api/Tenant/Branding/Image/{kind}` (kinds: `logo`, `logoWhite`, `favicon`, `hero`, `secondaryHero`, `benefits`).
  - Home: `PUT /api/Tenant/Home/Content` (about, hours, next-up title + type whitelist, benefits, sections - Script0039/0040); `PUT /api/Tenant/Home/DailyStatus`; `PUT /api/Tenant/Home/Footer`; gallery + track-graphics CRUD + reorder (`/api/Tenant/Home/Gallery`, `/api/Tenant/Home/TrackGraphics`, `sort_order`).
- **Public / read:** `GET /api/Tenant/Branding` (anonymous; returns the entire branding + settings projection used by the SPA shell); `GET /api/Tenant/Home/Gallery`, `GET /api/Tenant/Home/TrackGraphics` (anonymous, tenant-scoped via subdomain).
- **Apex / operator copy:** `PlatformBrandingController` (`GET /api/PlatformBranding` anonymous; `PUT`, `PUT /ForTracks`, image + testimonials writes are super-admin only - Script0095).
- **Published gate:** `is_published` (Script0096) gates public discovery only; flipped from the super-admin Tenants editor, not by the tenant. Front-door fields `external_home_url` / `external_events_url` / `custom_domain_verified` / `client_type` / `embed_event_target` (Script0121).
- **Authorization:** `webapi/AuthPolicies/TenantPermissions.cs`. `settings.manage` is in `AdminSet` only (tenant_admin). Tenant scope resolved by `TenantResolutionMiddleware` into `ITenantContext`; controllers check `IsResolved` first and cache-bust `tenant:{subdomain}` on every write.

## Concepts under test
- **Branding row is auto-seeded:** Script0002 trigger `ensure_tenant_branding` creates one `tenant_branding` row per tenant on insert, with theme defaults (`#1976D2` / light). Writes are upserts onto that row.
- **Nav color split:** `nav_bar_color` / `nav_bar_text_color` apply to interior pages; `nav_bar_home_color` / `nav_bar_home_text_color` override only the home/landing route, inheriting the rest-of-site value when NULL. NULL nav color falls back to theme primary; NULL text color falls back to white at render time.
- **Next-up whitelist NULL semantics:** `home_next_up_event_type_ids` NULL or empty means "show all event types"; an empty posted array is deliberately persisted as NULL by the controller so "show none" can never be expressed accidentally.
- **Location drives two systems:** discovery distance search needs lat/lng (idx_tenant_geo partial index); Stripe Terminal `EnsureTerminalLocation` needs `address_line` + `city` + `country` + `postal_code` (no lat/lng) and is provisioned lazily on first card-present charge.
- **Published is discovery-only:** `is_published=false` hides the tenant from the apex map / featured / `/Discover` / apex events feed but does NOT block subdomain resolution - the admin can always reach `{subdomain}.ridepass.io` to set up. New tenants default unpublished.
- **Settings cache:** the resolution middleware caches the resolved tenant for ~5 min under `tenant:{subdomain}`; every mutating endpoint here calls `InvalidateTenantCache()` (or `return await GetBranding()`, which busts it on non-GET) so a change takes effect on the next request.
- **Permission gating:** every write under `TenantController` requires `settings.manage`, held only by `tenant_admin`. `tenant_manager`, `tenant_cashier`, `tenant_scanner`, `tenant_accountant` must be rejected. The SPA mirrors the policy list but the server re-checks.

## Preconditions / test data
- One published tenant (`alpha`) and one unpublished tenant (`beta`), both reachable by subdomain.
- Per tenant, one user of each role: tenant_admin, tenant_manager, tenant_cashier, tenant_scanner, tenant_accountant, plus a rider.
- A second tenant (`gamma`) for cross-tenant isolation checks (gallery / track-graphic ids).
- Prepared upload assets: a valid PNG/JPEG/WebP/SVG/ICO under 5 MB, a >5 MB image, and a disallowed type (e.g. PDF / GIF).
- A tenant with a full street address (for Terminal provisioning) and one missing postal code / city.
- At least 2 event types per tenant so the next-up whitelist can include a subset.

---

## Admin (tenant_admin, settings.manage)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| TS1.1 [NN] | Branding colors + theme save | `PUT /api/Tenant/Branding` with new primary/secondary/accent, tagline, `themeMode=dark` | Saved; `GET /api/Tenant/Branding` reflects values; theme switches dark in the SPA. Reopen confirms persistence. |
| TS1.2 [NN] | Nav color home/interior split | Set `navBarColor` + `navBarTextColor` (interior) different from `navBarHomeColor` + `navBarHomeTextColor` | Interior pages use interior pair; home/landing route uses the home pair. Leaving home values NULL inherits the interior pair. |
| TS1.3 [NN] | Nav color render fallbacks | Clear `navBarColor` to NULL; clear `navBarTextColor` to NULL | Nav background falls back to theme primary; nav text falls back to white. No raw NULL leaks to CSS. |
| TS1.4 [NN] | Upload each branding image kind | `POST /api/Tenant/Branding/Image/{kind}` for logo, logoWhite, favicon, hero, secondaryHero, benefits with a valid PNG | Each returns a URL stored on the right column; `GetBranding` shows it. |
| TS1.5 [NN] | Replace image deletes the old file | Upload a new logo over an existing one | New URL stored; old object deleted from storage (verify no orphan). Same on `DELETE`. |
| TS1.6 [NN] | Reject oversized / wrong-type upload | Upload a >5 MB file; then a PDF | 400 "exceeds 5 MB limit"; 400 "Unsupported content type". Nothing persisted. |
| TS1.7 [NN] | Reject unknown image kind | `POST /api/Tenant/Branding/Image/banner` | 400 "Invalid image kind". |
| TS1.8 [NN] | Home content save + next-up whitelist | `PUT /api/Tenant/Home/Content` with about HTML, hours JSON, next-up title, and a 1-type `homeNextUpEventTypeIds` | Saved; home "Next Up" row shows only that type. Public home reflects the title. |
| TS1.9 [NN] | Empty next-up array means show-all | Save `homeNextUpEventTypeIds: []` | Persisted as **NULL** (not empty); next-up shows all types, never "none". |
| TS1.10 [NN] | Daily status post | `PUT /api/Tenant/Home/DailyStatus` open=true, message "tacky after rain" | Badge shows on public home with timestamp; `daily_status_updated_at` set (used to fade after ~24h). |
| TS1.11 [NN] | Footer + social links | `PUT /api/Tenant/Home/Footer` with contact email, phone, FB/IG/TikTok/YT, refund policy HTML | All saved (blanks normalized to NULL); footer renders links present only when set. |
| TS1.12 [NN] | Location save validation | `PUT /api/Tenant/Location` with lat=200 | 400 "Latitude must be between -90 and 90". Repeat lng=999 -> 400. |
| TS1.13 [NN] | Location lat/lng both-or-neither | Provide latitude only, longitude empty | 400 "Latitude and longitude must both be provided or both empty." |
| TS1.14 [NN] | Address drives Terminal location | Save a full address (line/city/country/postal), then run first card-present charge | `EnsureTerminalLocation` provisions a Stripe Terminal location and stores the id (idempotent on retry). |
| TS1.15 [NN] | Incomplete address blocks Terminal | Save address missing postal code; attempt card-present charge | `EnsureTerminalLocation` returns null (no location provisioned); flow degrades, no crash. |
| TS1.16 [NN] | Timezone validation | `PUT /api/Tenant` with `timezone="Mars/Phobos"` | 400 "Unknown IANA timezone". Valid IANA id saves and is echoed back. |
| TS1.17 [R] | Feature toggles round-trip | Toggle gift cards (with min/max), rentals, extras, season passes, concessions, blog, cancellation policy | Each persists and is reflected in `GetBranding`; gift-card min/max validation enforced ($1 floor, max >= min, <= $10,000). |
| TS1.18 [R] | Gallery + track-graphic CRUD/reorder | Add, edit caption, reorder, delete a gallery image and a track graphic | Sort order persists; delete removes the underlying image file. |
| TS1.19 [NN] | Cache busts immediately | Save any setting, then immediately hit a public endpoint that reads the tenant (e.g. blog gating) | New value visible on the next request, not lagging the 5-min cache. |

---

## Super-admin (apex copy + publish + front door)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| TS2.1 [NN] | Publish gate hides from discovery | As super admin set `beta` unpublished; query `/Discover`, apex map, apex events feed | `beta` absent from all public discovery surfaces. |
| TS2.2 [NN] | Unpublished still resolves by subdomain | Visit `beta.ridepass.io` while unpublished | Site loads and admin can configure it; only public discovery is suppressed. |
| TS2.3 [NN] | Publish flips visibility | Super admin sets `beta` published via the Tenants editor (`PUT /api/SuperAdmin/Tenants/{id}`) | `beta` now appears in discovery; cached tenant evicted so it shows immediately. |
| TS2.4 [NN] | Apex / operator copy edit | `PUT /api/PlatformBranding` (hero, stats, CTA banner) and `PUT /api/PlatformBranding/ForTracks` | Saved; public apex `GET /api/PlatformBranding` reflects new copy. ForTracks save does not clobber home-page fields and vice versa (split is intentional). |
| TS2.5 [R] | Front-door / embed fields | Super admin sets `external_home_url`, `external_events_url`, `custom_domain`, `custom_domain_verified`, `client_type`, `embed_event_target` | Persisted and surfaced on `GetBranding`; `custom_domain_verified=false` means entering a domain string changes no redirect behavior. |
| TS2.6 [R] | Platform testimonials CRUD/reorder | Create, edit, photo-upload, reorder, delete testimonials | Each persists; inactive excluded from the public payload; reorder respected. |

---

## Edge / authorization / isolation

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| TS3.1 [NN] | Non-admin role blocked from settings | As tenant_manager, then cashier/scanner/accountant, call any `PUT /api/Tenant/*` write | 403 for all. `settings.manage` is in AdminSet only; managers can run catalog/sales but never branding/settings. |
| TS3.2 [NN] | Rider blocked from settings | As a rider, call `PUT /api/Tenant/Branding` | 403 / forbidden. |
| TS3.3 [NN] | Public branding read needs no auth | `GET /api/Tenant/Branding` anonymously on `alpha` | 200 with the public projection; no auth required. |
| TS3.4 [NN] | No tenant resolved | Hit `GET /api/Tenant/Branding` with no/unknown subdomain | 400 "No tenant resolved" (every write also guards `IsResolved`). |
| TS3.5 [NN] | Cross-tenant gallery id rejected | As `alpha` admin, `PUT`/`DELETE` a `gamma` gallery image id | Not updated/deleted - repo calls are scoped by `tenant_id`; `gamma`'s row untouched. Repeat for track graphics. |
| TS3.6 [NN] | Cross-tenant branding isolation | Change `alpha` colors; load `gamma` | `gamma` branding unchanged (one branding row per tenant_id). |
| TS3.7 [R] | Apex copy not editable by tenant admin | As tenant_admin call `PUT /api/PlatformBranding` | 403 (super-admin policy). Tenant admins can never edit apex copy. |
| TS3.8 [NN] | Image upload tenant-scoped storage | Upload a hero as `alpha`; inspect stored key/url | Stored under `alpha`'s tenant id; not reachable as another tenant's asset. |
| TS3.9 [R] | HTML content escaping | Save about / benefits / refund HTML containing a `<script>` | Confirm the render path sanitizes or safely contains it (document current behavior; flag if raw HTML is trusted). |

## Known risks / watch-items
- **Multi-tenant isolation:** gallery/track-graphic update/delete take an `id` plus `tenant_id`; the scoping predicate is the only thing preventing a cross-tenant edit (TS3.5). Branding is keyed by `tenant_id` PK so it is naturally isolated, but image uploads must land under the resolving tenant's storage prefix (TS3.8).
- **Authorization:** all tenant settings/branding/home writes hang off `settings.manage`, which only `tenant_admin` holds; verify no endpoint accidentally drops the `[Authorize]` (the public `GetBranding`, `ListGallery`, `ListTrackGraphics` GETs are intentionally anonymous). Apex/platform copy is super-admin only.
- **Stored HTML (XSS):** `about_html`, `home_benefits_html`, `refund_policy_html`, `home_sections_json` accept admin-authored HTML rendered on public pages. Confirm sanitization; a malicious or compromised tenant admin could inject script into their own public site (TS3.9).
- **Published vs reachable:** `is_published` is discovery-only by design (Script0096). Do not regress it into a subdomain access gate - an unpublished tenant must still configure its site.
- **Next-up NULL footgun:** empty whitelist must persist as NULL ("show all"), never empty ("show none") (TS1.9).
- **Cache staleness:** every write must bust `tenant:{subdomain}`; a missed invalidation makes a setting (especially publish or feature toggles) lag up to 5 minutes.
- **Money-adjacent:** gift-card min/max bounds are enforced in `UpdateGiftCardSettings`; service charge itself is super-admin owned (see the Super Admin / Platform plan), not editable from this tenant surface.
- **Orphaned images:** replace/delete must clean up the prior storage object (TS1.5); a missed delete leaves orphaned files in the bucket.
