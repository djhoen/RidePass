import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Suppression lifecycle: add an address -> appears in the list -> re-enable -> gone. Self-cleaning,
// so it leaves no stage residue. The email is uniquely tagged so a failed run is easy to spot and
// remove. There's no separate "Delete" action on this screen — "Re-enable" is the one control that
// un-suppresses an address (SuppressionService.remove), which both fixes it going forward and takes
// the row out of the list, so it doubles as the cleanup step here.

test('suppression add then re-enable', async ({ page }) => {
    await page.goto('/Admin/Suppression')
    await expect(page).not.toHaveURL(/\/Login/)

    const email = `pwtest.${Date.now()}@example.com`

    // Add: only the email is required (note is optional).
    await page.getByRole('button', { name: 'Suppress address' }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Email' }).fill(email)
    await dlg.getByRole('button', { name: 'Suppress', exact: true }).click()

    const row = page.locator('tr', { hasText: email })
    await expect(row).toBeVisible()

    // Re-enable asks for confirmation via the shared themed prompt, then removes the row.
    await row.getByRole('button', { name: 'Re-enable', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: email })).toHaveCount(0)
})
