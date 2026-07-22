import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

test('reward program create then delete', async ({ page }) => {
    await page.goto('/Admin/Rewards')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Reward ${Date.now()}`
    await page.getByRole('button', { name: 'New Program' }).click()
    const dlg = page.getByRole('dialog')
    // openCreate() defaults rewardKind to 'percent_off' with valid requirement/reward values,
    // so save() has nothing else to validate; only Name is needed here to find the row.
    await dlg.getByRole('textbox', { name: 'Name' }).fill(name)
    await dlg.getByRole('button', { name: 'Save', exact: true }).click()

    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)
    await expect(page.locator('tr', { hasText: name })).toHaveCount(0)
})
