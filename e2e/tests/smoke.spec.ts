import { test, expect } from '@playwright/test'

// Login is handled once in auth.setup.ts and the session is reused, so it is no
// longer a test here. The buy flow moved to buy-pass.spec.ts. What remains is the
// most basic check: the tenant resolves from its subdomain and the app shell loads.
test('tenant home page loads on the acme subdomain', async ({ page }) => {
  await page.goto('/')

  // The nav bar renders once branding resolves for the tenant.
  await expect(page.locator('header, nav').first()).toBeVisible()

  // A failed tenant resolution surfaces an error rather than the normal app shell.
  await expect(page.getByText(/tenant not found/i)).toHaveCount(0)
})
