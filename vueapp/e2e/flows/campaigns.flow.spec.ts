import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Email campaign lifecycle: create a DRAFT -> appears in the list -> delete -> gone. Self-cleaning,
// so it leaves no stage residue. The subject is uniquely tagged so a failed run is easy to spot and
// remove.
//
// IMPORTANT: this must never send email. Campaigns.vue's compose dialog exposes two distinct save
// paths — "Save Draft" (calls campaignService.create/update only) and "Save & Send" / "Save &
// Schedule" (calls campaignService.send, which actually delivers to every active subscriber, gated
// behind its own themed confirm). This test clicks ONLY "Save Draft" and never touches the
// send/schedule button, so no email is ever queued.

test('campaign create draft then delete', async ({ page }) => {
    await page.goto('/Admin/Campaigns')
    await expect(page).not.toHaveURL(/\/Login/)

    const subject = `PWTEST Campaign ${Date.now()}`

    // Create: validate() requires both subject and a non-empty body. Subject is a plain text
    // field; the body is the tiptap RichTextEditor, a contenteditable ProseMirror div with no
    // accessible textbox role, so it's targeted by its class and typed into directly.
    await page.getByRole('button', { name: 'New Campaign' }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Subject' }).fill(subject)
    await dlg.locator('.ProseMirror').click()
    await dlg.locator('.ProseMirror').pressSequentially('PWTEST automated draft body — safe to delete.')

    // Save as a draft only. Do NOT click "Save & Send" / "Save & Schedule" — those deliver email.
    await dlg.getByRole('button', { name: 'Save Draft', exact: true }).click()

    // Saving a draft closes the dialog in place (no navigation) and reloads the list.
    const row = page.locator('tr', { hasText: subject })
    await expect(row).toBeVisible()
    await expect(row).toContainText('draft')

    // Delete it and confirm the shared themed prompt.
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: subject })).toHaveCount(0)
})
