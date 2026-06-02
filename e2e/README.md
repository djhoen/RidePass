# RidePass UI smoke tests (Playwright)

A minimal Playwright suite proving out the three things that make RidePass tricky to
test: tenant-by-subdomain routing, the Vuetify login form, and the Stripe Payment
Element (which lives in a cross-origin iframe).

## Prerequisites

1. **App running locally.** Start the webapi (`:5070`) and the Vite dev server
   (`vueapp`, `:3000`). The suite assumes both are up; see the commented `webServer`
   block in `playwright.config.ts` to have Playwright start Vite for you.

2. **Tenant subdomain resolves to localhost.** RidePass reads the tenant from the
   subdomain, so add this line to your hosts file
   (`C:\Windows\System32\drivers\etc\hosts`, edit as Administrator):

   ```
   127.0.0.1 acme.ridepass.local
   ```

   Vite already allows `.ridepass.local` hosts (`vueapp/vite.config.ts`).

3. **Seeded data is NOT required.** The suite provisions what it needs through the
   API (see "How data setup works" below). You still need the `acme` tenant to exist
   with at least one event type and one active pass product, which the app's normal
   setup provides. `seed-acme.sql` is still handy for a richer dataset, but the core
   tests no longer depend on a fresh seed each run.

4. **A working admin password.** `admin@acme.test` is the seeded tenant admin, but the
   seed leaves password hashes as placeholders. Set a known password (via the app's
   `/ResetPassword` flow or by updating the dev DB hash), then export it:

   ```powershell
   $env:E2E_ADMIN_EMAIL = "admin@acme.test"
   $env:E2E_ADMIN_PASSWORD = "your-dev-password"
   ```

## Install and run

```powershell
cd C:\Users\djhoe\source\repos\RidePass\e2e
npm install
npx playwright install chromium   # downloads the pinned browser binary

npm test                # headless
npm run test:headed     # watch it drive the browser
npm run test:ui         # interactive UI mode (great for learning)
npm run report          # open the HTML report (with trace viewer) after a run
```

## Learning Playwright fast

`npm run codegen` opens the acme site and records your clicks into runnable test code.
It is the quickest way to see how locators map to the Vuetify UI.

## How data setup works (no seeding)

The suite follows the standard Playwright pattern: **set up state through the API,
assert through the UI.** That keeps tests fast and deterministic and removes the
"re-seed before every run" chore.

- **Auth is reused.** `auth.setup.ts` logs in once via the API, seeds the JWT into
  localStorage, and saves the browser session to `tests/.auth/admin.json`. The
  `chromium` project depends on it and starts every test already signed in, so no
  spec drives the login form.
- **Data is "ensure"-style** (`tests/helpers/data.ts`): each helper reuses existing
  rows and only creates or adjusts when nothing suitable exists. For example,
  `ensureFuturePurchasableEvent` reuses a future event with an active pass, or bumps
  a stale event's dates into the future, or creates one as a last resort. Re-running
  reuses what the previous run made, so nothing piles up.
- **Tenant + auth on direct API calls** are replicated from what the SPA sends:
  `X-Tenant-Subdomain: acme` plus a `Bearer` token (`tests/helpers/api.ts`). Point
  `E2E_API_BASE` at your API if it isn't `http://localhost:5070/api`.

## What's covered

| Spec | What it checks |
|------|----------------|
| `smoke.spec.ts` | Tenant resolves from the subdomain; app shell loads |
| `dashboard.spec.ts` | Admin dashboard loads its revenue snapshot |
| `buy-pass.spec.ts` | Full buy flow to the Stripe Payment Element (self-provisions an event) |
| `coupons.spec.ts` | Coupon created via API appears in the admin list |
| `customers.spec.ts` | Customers view loads; debounced search queries the API |

## Notes / next steps

- **Tenant override:** set `E2E_BASE_URL` to point at staging/prod
  (e.g. `https://acme.ridepass.io`).
- **Stripe:** the buy-flow test fills the test card (`4242...`) but stops short of
  clicking Pay, so it creates no charge or purchase row. The commented lines in
  `smoke.spec.ts` show how to assert a full successful purchase once you want that.
- **QR / camera views** (`Redeem`, `PassCheckIn`, `PhotoCapture`): the config already
  launches Chrome with fake-media flags. To actually drive a QR scan, add
  `--use-file-for-fake-video-capture=<your.y4m>` in `playwright.config.ts`.
- **Data isolation:** the suite runs single-worker against shared `acme` data. Before
  scaling to many tests, give them per-test seeding/teardown so they stay deterministic.
