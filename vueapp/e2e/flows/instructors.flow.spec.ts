import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Instructor lifecycle: create -> appears -> delete -> gone.

test('instructor create then delete', async ({ page }) => {
    await page.goto('/Admin/Instructors')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Coach ${Date.now()}`
    await page.getByRole('button', { name: 'Add Instructor' }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Name' }).fill(name)
    await dlg.getByRole('button', { name: 'Add', exact: true }).click()

    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()

    // The delete control is the row's trailing icon button.
    await row.getByRole('button').last().click()
    await confirmPrompt(page)
    await expect(page.locator('tr', { hasText: name })).toHaveCount(0)
})
