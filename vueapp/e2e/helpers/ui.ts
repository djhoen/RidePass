import { Page } from '@playwright/test'

// Clicks the primary (confirm) action of the app's shared ConfirmDialog. Its actions are
// [Cancel, Confirm], so the confirm button is the last one in the most-recently-opened dialog.
export async function confirmPrompt(page: Page) {
    const dlg = page.getByRole('dialog').last()
    await dlg.locator('.v-card-actions').getByRole('button').last().click()
}
