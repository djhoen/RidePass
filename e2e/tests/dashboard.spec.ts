import { test, expect } from '@playwright/test'

// Uses the reused admin session from auth.setup.ts. Confirms the dashboard route
// loads and its revenue snapshot renders (the cards show a "$" figure).
test('admin dashboard loads its snapshot', async ({ page }) => {
  await page.goto('/Admin/Dashboard')

  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  // Revenue cards render dollar amounts once the snapshot resolves.
  await expect(page.getByText('$').first()).toBeVisible()
})
