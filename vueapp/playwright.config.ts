import { defineConfig } from '@playwright/test'

// Points at a DEPLOYED stage tenant, e.g. STAGE_BASE_URL=https://motoland.stage.ridepass.io
// Fails loudly rather than silently testing nothing.
const baseURL = process.env.STAGE_BASE_URL
if (!baseURL) {
    throw new Error(
        'STAGE_BASE_URL is required. Example:\n' +
        '  STAGE_BASE_URL=https://<tenant>.stage.ridepass.io \\\n' +
        '  STAGE_ADMIN_EMAIL=... STAGE_ADMIN_PASSWORD=... npx playwright test')
}

export default defineConfig({
    testDir: './e2e',
    // Deterministic ordering; these hit a shared remote env, so no parallel races.
    fullyParallel: false,
    workers: 1,
    retries: 1,
    timeout: 45_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { open: 'never', outputFolder: 'e2e/report' }]],
    outputDir: 'e2e/results',
    use: {
        baseURL,
        headless: true,
        screenshot: 'only-on-failure',
        trace: 'retain-on-failure',
        video: 'off',
        ignoreHTTPSErrors: process.env.PW_IGNORE_HTTPS_ERRORS === '1',
    },
    projects: [
        // Logs in once and saves the session; everything else reuses it.
        { name: 'setup', testMatch: /auth\.setup\.ts/ },
        {
            name: 'smoke',
            testMatch: /.*\.smoke\.spec\.ts/,
            dependencies: ['setup'],
            use: { storageState: 'e2e/.auth/admin.json' },
        },
    ],
})
