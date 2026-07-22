import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Extras (event add-on) product lifecycle: create -> appears -> delete -> gone.
// Unlike other admin lists, the delete control lives inside the edit dialog (not the row),
// so this reopens the item via its row's "Edit" button before deleting.

test('extra product create then delete', async ({ page }) => {
    await page.goto('/Admin/Extras')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Extra ${Date.now()}`

    // Create: only Name is required (canSave = name non-empty && valid kind). openCreate()
    // defaults kind to 'camping', which already passes kindError, so no chip click is needed.
    await page.getByRole('button', { name: 'Add Product', exact: true }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Name' }).fill(name)
    await dlg.getByRole('button', { name: 'Save', exact: true }).click()

    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()

    // Reopen the row for editing — the Delete button only appears in the edit dialog, not on the row.
    await row.getByRole('button', { name: 'Edit', exact: true }).click()
    await dlg.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: name })).toHaveCount(0)
})
