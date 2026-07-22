import { test, expect } from '@playwright/test'

// Membership settings (Admin > Settings > Membership, Membership.vue) is a single settings form,
// not list CRUD, so this is a reversible-edit test: capture the current "Membership name" value,
// overwrite it with a unique PWTEST value, Save, reload to prove it persisted server-side, then
// restore the original value and Save again so the tenant's real config ends unchanged.
//
// "Membership name" (not Price/Duration) is the edit target: it's a plain display string shown to
// riders on the /Membership page, with no side effects on save (unlike Price/Duration, which drive
// real Stripe pricing on future purchases). canSave only requires the name to be non-empty, so any
// PWTEST string is accepted and the Save button stays enabled throughout.
//
// The restore runs in a `finally` block so it still executes even if the mid-test assertions fail
// (e.g. the reload check) — the only way this test leaves the tenant changed is if the restore
// Save itself fails (network error, page crash), which is an accepted residual risk of any
// reversible-edit flow test.

test('membership name edits persist and restore', async ({ page }) => {
    await page.goto('/Admin/Settings/Membership')
    await expect(page).not.toHaveURL(/\/Login/)

    const field = page.getByRole('textbox', { name: 'Membership name' })
    const saveButton = page.getByRole('button', { name: 'Save' })
    await expect(field).toBeVisible()

    const original = await field.inputValue()
    const testValue = `PWTEST ${Date.now()}`

    try {
        await field.fill(testValue)
        await saveButton.click()
        await expect(page.getByText('Saved.')).toBeVisible()

        await page.reload()
        await expect(page).not.toHaveURL(/\/Login/)
        await expect(field).toHaveValue(testValue)
    } finally {
        await field.fill(original)
        await saveButton.click()
        await expect(page.getByText('Saved.')).toBeVisible()
    }
})
