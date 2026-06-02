import { test as setup, expect } from '@playwright/test'
import { loginViaApi, ADMIN_STATE } from './helpers/api'

// Runs once before the main project (wired via `dependencies` in the config) and
// saves a signed-in browser session to disk. Every other test then starts already
// authenticated, so we don't drive the login form on every spec.

setup('authenticate as admin', async ({ page, request }) => {
  const { token, userId, role } = await loginViaApi(request)

  // The app keeps its JWT in localStorage (token / userId / role, per AuthHelper),
  // so seed those on the acme origin and persist the storage state.
  await page.goto('/')
  await page.evaluate(
    ({ token, userId, role }) => {
      localStorage.setItem('token', token)
      if (userId) localStorage.setItem('userId', userId)
      if (role) localStorage.setItem('role', role)
    },
    { token, userId, role },
  )

  // Sanity-check the session actually lands on an admin screen.
  await page.goto('/Admin/Dashboard')
  await expect(page).toHaveURL(/\/Admin\/Dashboard/)

  await page.context().storageState({ path: ADMIN_STATE })
})
