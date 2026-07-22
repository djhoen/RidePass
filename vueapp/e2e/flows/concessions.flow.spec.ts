import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Concession menu item lifecycle: create -> appears in the Items tab -> delete -> gone.
// The "Items" tab is the default tab on load, so no tab navigation is needed.

test('concession item create then delete', async ({ page }) => {
    await page.goto('/Admin/Concessions')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Item ${Date.now()}`

    // Create: only Name is required (saveProduct only validates a non-empty name);
    // price defaults to $0 and category/station/etc. are all optional.
    await page.getByRole('button', { name: 'Add item', exact: true }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Name' }).fill(name)
    await dlg.getByRole('button', { name: 'Save', exact: true }).click()

    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()

    // Row has trailing "Edit" and "Delete" text buttons; Delete pops the shared ConfirmDialog.
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: name })).toHaveCount(0)
})
