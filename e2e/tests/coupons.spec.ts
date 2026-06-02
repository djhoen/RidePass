import { test, expect } from '@playwright/test'
import { loginViaApi } from './helpers/api'
import { ensureCoupon } from './helpers/data'

// Demonstrates the recommended pattern: set state up through the API (fast,
// deterministic), then assert it through the UI. The coupon is reused across runs
// if it already exists, so this never accumulates duplicates and needs no seed.
test('a coupon created via the API appears in the admin list', async ({ page, request }) => {
  const { token } = await loginViaApi(request)
  const code = 'E2EALWAYS10'
  await ensureCoupon(request, token, code)

  await page.goto('/Admin/Coupons')
  await expect(page.getByRole('heading', { name: 'Coupons' })).toBeVisible()
  await expect(page.getByText(code).first()).toBeVisible()
})
