import { test, expect } from '@playwright/test'

// General settings (Admin > Settings > General, General.vue) is a single settings form, not list
// CRUD, so this is a reversible-edit test: capture the current "Shipping Name" value, overwrite it
// with a unique PWTEST value, Save, reload to prove it persisted server-side, then restore the
// original value and Save again so the tenant's real config ends unchanged.
//
// "Shipping Name" is the edit target: it's only the recipient name printed on shipped packages
// (form.shippingName) — never shown to riders, unlike the Identity/Location/Contact/Social/Checkout
// blocks that drive the Discover map, geocoding, or the public home page and checkout. Editing it
// alone doesn't touch addressLine/city/region/postalCode, so the auto-geocode watcher (which only
// fires when those specific fields change AND lat/lng are still empty) never triggers, and the
// timezone field is left untouched entirely. Payment-adjacent fields are not on this page at all
// (they live under Settings > Payments) so there's no risk of touching those.
//
// The restore runs in a `finally` block so it still executes even if the mid-test assertions fail
// (e.g. the reload check) — the only way this test leaves the tenant changed is if the restore
// Save itself fails (network error, page crash), which is an accepted residual risk of any
// reversible-edit flow test.

test('shipping name edits persist and restore', async ({ page }) => {
    await page.goto('/Admin/Settings/General')
    await expect(page).not.toHaveURL(/\/Login/)

    const field = page.getByRole('textbox', { name: 'Shipping Name' })
    const saveButton = page.getByRole('button', { name: 'Save changes' })
    await expect(field).toBeVisible()

    const original = await field.inputValue()
    const testValue = `PWTEST ${Date.now()}`

    try {
        await field.fill(testValue)
        await saveButton.click()
        await expect(page.getByText('General settings saved.')).toBeVisible()

        await page.reload()
        await expect(page).not.toHaveURL(/\/Login/)
        await expect(field).toHaveValue(testValue)
    } finally {
        await field.fill(original)
        await saveButton.click()
        await expect(page.getByText('General settings saved.')).toBeVisible()
    }
})
