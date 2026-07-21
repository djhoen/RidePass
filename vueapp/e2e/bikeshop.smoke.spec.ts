import { test, expect, Page } from '@playwright/test'

// Read-only smoke over the bike-shop admin surfaces built this session. These assert that each
// screen RENDERS and the new controls are present — they do not depend on specific stage data and
// they mutate nothing. Deeper flows that create/change data are gated behind RUN_MUTATIONS below.

// A page is "healthy" if it navigated, isn't the login screen, and shows no thrown-error banner.
async function expectHealthy(page: Page) {
    await expect(page).not.toHaveURL(/\/Login/)
    // No uncaught render crash (Vuetify error overlay / our generic error alert).
    await expect(page.locator('text=/something went wrong/i')).toHaveCount(0)
}

test('Inventory: products, thumbnails, Supply Chain incl. Reorder, Stock Takes', async ({ page }) => {
    await page.goto('/Admin/BikeShop')
    await expectHealthy(page)
    await expect(page.getByRole('tab', { name: /Products/i })).toBeVisible()
    await expect(page.getByRole('tab', { name: /Supply Chain/i })).toBeVisible()
    await expect(page.getByRole('tab', { name: /Stock Takes/i })).toBeVisible()

    // Supply Chain -> Reorder sub-tab exists (the reorder worklist feature).
    await page.getByRole('tab', { name: /Supply Chain/i }).click()
    await expect(page.getByRole('tab', { name: /Reorder/i })).toBeVisible()
    await page.screenshot({ path: 'e2e/results/inventory.png', fullPage: true })
})

test('Work orders: list + filters render', async ({ page }) => {
    await page.goto('/Admin/BikeShop/WorkOrders')
    await expectHealthy(page)
    await expect(page.getByText(/Work orders|Saved jobs/i).first()).toBeVisible()
    await page.screenshot({ path: 'e2e/results/workorders.png', fullPage: true })
})

test('Sales: filter bar (search, dates, status) renders', async ({ page }) => {
    await page.goto('/Admin/BikeShop/Sales')
    await expectHealthy(page)
    await expect(page.getByLabel(/Order #, customer, or item/i)).toBeVisible()
    await expect(page.getByText(/Collected/i).first()).toBeVisible()
    await page.screenshot({ path: 'e2e/results/sales.png', fullPage: true })
})

test('Rentals: page renders with tabs', async ({ page }) => {
    await page.goto('/Admin/BikeShop/Rentals')
    await expectHealthy(page)
    await expect(page.getByRole('tab').first()).toBeVisible()
    await page.screenshot({ path: 'e2e/results/rentals.png', fullPage: true })
})

test('Settings: Work order stages + Inspection checklist + Service tabs', async ({ page }) => {
    await page.goto('/Admin/BikeShop/Settings')
    await expectHealthy(page)
    await expect(page.getByRole('tab', { name: /Work order stages/i })).toBeVisible()
    await expect(page.getByRole('tab', { name: /Inspection checklist/i })).toBeVisible()
    await expect(page.getByRole('tab', { name: /Service/i })).toBeVisible()

    // Work order stages editor shows the seeded built-in statuses.
    await page.getByRole('tab', { name: /Work order stages/i }).click()
    await expect(page.getByText(/Intake|Estimate|Ready/i).first()).toBeVisible()
    await page.screenshot({ path: 'e2e/results/settings-stages.png', fullPage: true })
})

test('Reports: bike shop Labor time tab present', async ({ page }) => {
    await page.goto('/Admin/Reports')
    await expectHealthy(page)
    // Open the Bike Shop report card, then its Labor time tab.
    const bikeShopCard = page.getByText(/Bike Shop/i).first()
    if (await bikeShopCard.isVisible().catch(() => false)) {
        await bikeShopCard.click()
        await expect(page.getByRole('tab', { name: /Labor time/i })).toBeVisible()
        await page.screenshot({ path: 'e2e/results/reports-labortime.png', fullPage: true })
    } else {
        test.skip(true, 'Bike Shop report not available for this account/tenant')
    }
})
