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

        // Open the new-work-order editor.
        await page.getByRole('button', { name: 'New work order' }).click()
        const dlg = page.getByRole('dialog')

        // Intake requires a customer name + a bike description (or linked bike record).
        const name = `${TAG} Rider ${Date.now()}`
        await dlg.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
        await dlg.getByRole('textbox', { name: 'Bike (make, model, color)' }).fill(`${TAG} YZ250F`)
        await dlg.getByRole('button', { name: 'Save', exact: true }).click()

        // On create, the editor reopens on the saved order; the Labor time panel + timer appear.
        await expect(dlg.getByText('Labor time').first()).toBeVisible({ timeout: 15_000 })

        // Add a labor line with a 60-minute estimate. Number fields expose the spinbutton role.
        await dlg.getByRole('textbox', { name: 'Work performed' }).fill(`${TAG} Fork service`)
        await dlg.getByRole('spinbutton', { name: 'Est. min' }).fill('60')
        await dlg.getByRole('spinbutton', { name: 'Price', exact: true }).fill('90')
        await dlg.getByRole('button', { name: 'Add line' }).click()
        await expect(dlg.getByText(/est 60m/i)).toBeVisible()

        // Start then stop the timer; it should flip to "running" then back to a Start button.
        await dlg.getByRole('button', { name: 'Start', exact: true }).click()
        await expect(dlg.getByText(/running/i)).toBeVisible()
        await dlg.getByRole('button', { name: 'Stop', exact: true }).click()
        await expect(dlg.getByRole('button', { name: 'Start', exact: true })).toBeVisible()

        await page.screenshot({ path: 'e2e/results/mutation-workorder-timer.png', fullPage: true })
    })
})
