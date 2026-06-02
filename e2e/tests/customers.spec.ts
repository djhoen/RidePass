import { test, expect } from '@playwright/test'

// Read-only check against whatever customers the tenant already has, so it needs
// no seed. Confirms the list view loads and that typing in the (debounced) search
// box actually queries the API.
test('customers view loads and search queries the API', async ({ page }) => {
  await page.goto('/Admin/Customers')
  await expect(page.getByRole('heading', { name: 'Customers' })).toBeVisible()

  const search = page.getByLabel('Search by name or email')
  await expect(search).toBeVisible()

  // The next /Customer GET after we type is the debounced search request.
  const searchRequest = page.waitForResponse(
    r => /\/Customer(\?|$)/.test(r.url()) && r.request().method() === 'GET',
  )
  await search.fill('rivera')
  const resp = await searchRequest
  expect(resp.ok()).toBeTruthy()
})
