import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Custom event-type lifecycle: create -> appears -> delete -> gone. System types can't be deleted,
// so this only touches the custom one it creates.

test('custom event type create then delete', async ({ page }) => {
    await page.goto('/Admin/EventTypes')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Type ${Date.now()}`
    await page.getByRole('button', { name: 'Add Custom Type' }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Name' }).fill(name)
    await dlg.getByRole('button', { name: 'Save', exact: true }).click()

    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)
    await expect(page.locator('tr', { hasText: name })).toHaveCount(0)
})
