import { defineConfig, devices } from '@playwright/test'
import { ADMIN_STATE } from './tests/helpers/api'

// RidePass resolves the tenant from the request subdomain (TenantResolutionMiddleware),
// so the base URL must be a real tenant host, not bare localhost. The Vite dev server
// already allows ".ridepass.local" subdomains (see vueapp/vite.config.ts), so we point
// the suite at the seeded "acme" tenant. Override with E2E_BASE_URL for staging/prod.
const BASE_URL = process.env.E2E_BASE_URL || 'http://acme.ridepass.local:3000'

export default defineConfig({
  testDir: './tests',
  // One worker keeps the shared "acme" tenant data deterministic. Bump this once
  // tests seed and tear down their own isolated data.
  workers: 1,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['html', { open: 'never' }], ['list']],
  use: {
    baseURL: BASE_URL,
    // Trace viewer is the big Playwright win: on a failed step you get a
    // time-travel timeline with DOM snapshots, network, and console. Open with
    // `npm run report` after a failing run.
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    // Grant camera up front so QR-scan / PhotoCapture views do not block on a
    // permission prompt. Pair with the fake-device launch flags below.
    permissions: ['camera'],
    // Lets direct API setup calls hit a self-signed https dev endpoint if you
    // point E2E_API_BASE at one (e.g. https://localhost:7293/api).
    ignoreHTTPSErrors: true,
  },
  projects: [
    // Authenticates once and saves the session; everything else depends on it.
    { name: 'setup', testMatch: /auth\.setup\.ts/ },
    {
      name: 'chromium',
      testIgnore: /auth\.setup\.ts/,
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        // Start every test already signed in as the admin.
        storageState: ADMIN_STATE,
        launchOptions: {
          // Feed a fake camera so html5-qrcode / PhotoCapture views have a stream
          // instead of hanging. Swap in --use-file-for-fake-video-capture=<y4m>
          // to actually drive QR-scan tests later.
          args: [
            '--use-fake-ui-for-media-stream',
            '--use-fake-device-for-media-stream',
          ],
        },
      },
    },
  ],

  // The app (webapi on :5070 + Vite on :3000) is expected to be running already.
  // To have Playwright start Vite for you, uncomment and adjust:
  // webServer: {
  //   command: 'npm --prefix ../vueapp run dev',
  //   url: 'http://localhost:3000',
  //   reuseExistingServer: !process.env.CI,
  //   timeout: 120_000,
  // },
})
