import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

test('season pass create then delete', async ({ page }) => {
    await page.goto('/Admin/SeasonPasses')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Pass ${Date.now()}`
    await page.getByRole('button', { name: 'New Pass' }).click()
    const dlg = page.getByRole('dialog')
    // Every other field (price, valid dates, kind) already has a usable default set by
    // openCreate(); only Name is required by save() and needed here to find the row.
    await dlg.getByRole('textbox', { name: 'Name' }).fill(name)
    await dlg.getByRole('button', { name: 'Save', exact: true }).click()

    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)
    await expect(page.locator('tr', { hasText: name })).toHaveCount(0)
})
