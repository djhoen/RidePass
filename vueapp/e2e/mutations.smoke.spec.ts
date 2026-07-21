import { test, expect } from '@playwright/test'

// WRITE-FLOW tests. These CREATE data on stage, so they are skipped unless RUN_MUTATIONS=1.
// They never touch payment or outward-comms flows. Test data is tagged [PW-TEST] so it's easy to
// spot and remove afterwards.
const RUN = process.env.RUN_MUTATIONS === '1'
const TAG = '[PW-TEST]'

test.describe('write flows (gated by RUN_MUTATIONS=1)', () => {
    test.skip(!RUN, 'Set RUN_MUTATIONS=1 to run write-flow tests against stage.')

    test('create a work order, add an estimated labor line, run + stop the timer', async ({ page }) => {
        await page.goto('/Admin/BikeShop/WorkOrders')
        await expect(page).not.toHaveURL(/\/Login/)

        // Open the new-work-order editor. Button label may be "New work order" / "New".
        await page.getByRole('button', { name: /New work order|New/i }).first().click()

        const name = `${TAG} Rider ${Date.now()}`
        await page.getByLabel(/Customer name/i).fill(name)
        // Bike description satisfies the intake requirement without needing a bike record.
        await page.getByLabel(/Bike \(make, model, color\)|Bike/i).first().fill(`${TAG} YZ250F`)
        await page.getByRole('button', { name: /^Save$|Create/i }).first().click()

        // Editor reopens on the saved order; the Labor time panel + timer are present.
        await expect(page.getByText(/Labor time/i)).toBeVisible()

        // Add a labor line with an estimate.
        await page.getByLabel(/Work performed/i).fill(`${TAG} Fork service`)
        await page.getByLabel(/Est\. min/i).fill('60')
        await page.getByLabel(/^Price$/i).fill('90')
        await page.getByRole('button', { name: /Add line/i }).click()
        await expect(page.getByText(/est 60m/i)).toBeVisible()

        // Start then stop the timer; actual should be recorded (>= 0m shown, timer returns to Start).
        await page.getByRole('button', { name: /^Start$/i }).click()
        await expect(page.getByText(/running/i)).toBeVisible()
        await page.getByRole('button', { name: /^Stop$/i }).click()
        await expect(page.getByRole('button', { name: /^Start$/i })).toBeVisible()

        await page.screenshot({ path: 'e2e/results/mutation-workorder-timer.png', fullPage: true })
    })
})
