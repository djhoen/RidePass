import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Newsletter subscriber lifecycle: create -> appears in the list -> delete -> gone. Self-cleaning,
// so it leaves no stage residue. The email is uniquely tagged so a failed run is easy to spot and
// remove.

test('subscriber create then delete', async ({ page }) => {
    await page.goto('/Admin/Subscribers')
    await expect(page).not.toHaveURL(/\/Login/)

    const email = `pwtest.${Date.now()}@example.com`

    // Create: only email is required by submitAdd (name is optional and left blank). The
    // dialog's own confirm button is also labelled "Add", same as the page-level button that
    // opens it, so scope to the dialog to disambiguate.
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Email' }).fill(email)
    await dlg.getByRole('button', { name: 'Add', exact: true }).click()

    // New subscribers are active by default, so they show up under the default "Active" tab
    // without switching tabs.
    const row = page.locator('tr', { hasText: email })
    await expect(row).toBeVisible()

    // Delete it and confirm the shared themed prompt.
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: email })).toHaveCount(0)
})
