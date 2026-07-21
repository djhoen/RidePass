import { test as setup } from '@playwright/test'
import fs from 'node:fs'

// Logs in through the real UI once and saves the session (bearer token in localStorage) so the
// smoke tests reuse it. Credentials come from the environment — never commit real ones.
const ADMIN_STATE = 'e2e/.auth/admin.json'

setup('authenticate as admin', async ({ page }) => {
    const email = process.env.STAGE_ADMIN_EMAIL
    const password = process.env.STAGE_ADMIN_PASSWORD
    if (!email || !password) {
        throw new Error('Set STAGE_ADMIN_EMAIL and STAGE_ADMIN_PASSWORD to the stage QA admin login.')
    }

    await page.goto('/Login')
    // Vuetify inputs: target by type to avoid label-association flakiness.
    await page.locator('input[type="email"]').fill(email)
    await page.locator('input[type="password"]').first().fill(password)
    await page.getByRole('button', { name: 'Sign in' }).click()

    // The app persists its bearer token on success; that's the reliable "logged in" signal.
    await page.waitForFunction(() => !!localStorage.getItem('token'), null, { timeout: 20_000 })

    fs.mkdirSync('e2e/.auth', { recursive: true })
    await page.context().storageState({ path: ADMIN_STATE })
})
