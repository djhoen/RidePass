import { test, expect } from '@playwright/test'

// Waiver create form (Waiver.vue). Waivers have NO delete endpoint (WaiverService only
// creates/updates) and are legally sensitive, so a full create would leave a permanent,
// un-removable row on every run. Instead this verifies the create form works end-to-end up to the
// point of persistence: the dialog opens, required fields (name + rich-text body; title defaults)
// can be filled, Save becomes enabled, then Cancel closes it without writing anything. This covers
// the create UI (including the Tiptap body editor) with zero residue.

test('waiver create form fills and enables save', async ({ page }) => {
    await page.goto('/Admin/Waiver')
    await expect(page).not.toHaveURL(/\/Login/)

    await page.getByRole('button', { name: 'Add Waiver' }).click()
    const dlg = page.getByRole('dialog')

    await dlg.getByRole('textbox', { name: 'Name (admin label)' }).fill(`PWTEST Waiver ${Date.now()}`)
    // Body is a Tiptap contenteditable; click in and type so ProseMirror's model updates (a plain
    // .fill() can desync and leave the model empty, which would keep Save disabled).
    await dlg.locator('.ProseMirror').click()
    await page.keyboard.type('PWTEST body text.')

    // Title defaults on open, so with name + body present the form is complete and Save enables.
    await expect(dlg.getByRole('button', { name: 'Save', exact: true })).toBeEnabled()

    // Cancel without persisting (no delete exists, so we never commit a permanent test row).
    await dlg.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(dlg).not.toBeVisible()
})
