import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Blackout lifecycle: create -> appears in the list -> delete -> gone. Self-cleaning.
//
// openCreate() in Blackouts.vue defaults to an all-day blackout starting/ending today, which is
// already valid for save() (start/end date both required, end >= start). Reason is technically
// optional, but the list has no name/title column, so it's the only field that can carry a
// unique tag to find the row afterward — filled here for that purpose, not because save()
// requires it.

test('blackout create then delete', async ({ page }) => {
    await page.goto('/Admin/Blackouts')
    await expect(page).not.toHaveURL(/\/Login/)

    const reason = `PWTEST Blackout ${Date.now()}`

    await page.getByRole('button', { name: 'Add Blackout' }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Reason (optional)' }).fill(reason)
    await dlg.getByRole('button', { name: 'Save', exact: true }).click()

    const row = page.locator('tr', { hasText: reason })
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: reason })).toHaveCount(0)
})
