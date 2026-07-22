import { test, expect } from '@playwright/test'

// Coupon lifecycle: create -> appears in the list -> delete -> gone. Self-cleaning, so it leaves no
// stage residue. The code is uniquely tagged so a failed run is easy to spot and remove.

test('coupon create then delete', async ({ page }) => {
    await page.goto('/Admin/Coupons')
    await expect(page).not.toHaveURL(/\/Login/)

    const code = `PWTEST${Date.now()}`

    // Create: only the code is required; the dialog defaults to 10% off, all scope, active.
    await page.getByRole('button', { name: 'Add Coupon' }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Code' }).fill(code)
    await dlg.getByRole('button', { name: 'Save', exact: true }).click()

    // It should now be a row in the table.
    const row = page.locator('tr', { hasText: code })
    await expect(row).toBeVisible()

    // Delete it and confirm the themed prompt (its confirm button also reads "Delete").
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await page.getByRole('dialog').filter({ hasText: /Delete coupon/i })
        .getByRole('button', { name: 'Delete', exact: true }).click()

    await expect(page.locator('tr', { hasText: code })).toHaveCount(0)
})
