# Stage UI smoke tests (Playwright)

Headless-Chromium UI tests that run against a **deployed** stage tenant. They log in through the
real UI, navigate the admin screens built recently, assert the new controls render, and capture
screenshots. Read-only by default; write flows are gated.

## Prerequisites

Already installed in this repo: `@playwright/test` + the Chromium browser binary. If a fresh
checkout is missing the browser: `npx playwright install chromium`.

## Run

```bash
# from vueapp/
STAGE_BASE_URL=https://<tenant>.stage.ridepass.io \
STAGE_ADMIN_EMAIL=<qa admin email> \
STAGE_ADMIN_PASSWORD=<password> \
npx playwright test --project=smoke
```

The `setup` project logs in once and saves the session to `e2e/.auth/admin.json`; the `smoke`
project reuses it. Screenshots land in `e2e/results/`; the HTML report in `e2e/report/`
(`npx playwright show-report e2e/report`).

### Environment variables

| Var | Required | Purpose |
|-----|----------|---------|
| `STAGE_BASE_URL` | yes | Full origin of the stage tenant (subdomain matters). |
| `STAGE_ADMIN_EMAIL` / `STAGE_ADMIN_PASSWORD` | yes | Stage QA admin login (e.g. the seeded `qa.admin`). |
| `RUN_MUTATIONS` | no | `1` enables the write-flow tests (they CREATE data). Off by default. |
| `PW_IGNORE_HTTPS_ERRORS` | no | `1` to relax TLS (only if stage uses a non-trusted cert). |

## What's covered

- **`bikeshop.smoke.spec.ts`** (read-only): Inventory (products/thumbnails, Supply Chain → Reorder,
  Stock Takes), Work Orders, Sales (filter bar), Rentals, Settings (Work order stages, Inspection
  checklist, Service), Reports → Bike Shop → Labor time. Asserts each screen renders and the new
  controls are present; depends on no specific data.
- **`editors.smoke.spec.ts`** (read-only): the RichTextEditor link button opens the themed dialog
  (fails/hangs if it regresses to a native `window.prompt`); the EventRiders CSV export is an authed
  button, not a bare `<a href>`.
- **`mutations.smoke.spec.ts`** (gated by `RUN_MUTATIONS=1`): creates a `[PW-TEST]`-tagged work
  order, adds an estimated labor line, and runs the timer start/stop round-trip.

## Guardrails

- **No payment or checkout flows** are exercised (no Stripe, test-mode or otherwise).
- **No outward comms** (ready notifications, campaigns, emails/SMS) are triggered.
- Write-flow tests are **off unless `RUN_MUTATIONS=1`** and tag their data `[PW-TEST]` for easy cleanup.

## Notes / first-run

- Selectors target Vuetify by role/label/type. If the login or a control was renamed, the failing
  test's selector is the one to adjust — start with `e2e/auth.setup.ts`.
- These are not part of the app build or the Vite bundle; `@playwright/test` is a devDependency only.
