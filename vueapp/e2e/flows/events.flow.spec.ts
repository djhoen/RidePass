import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Event lifecycle: create -> appears in the list -> delete -> gone. Self-cleaning.
//
// EventDialog (src/components/EventDialog.vue) requires more than title/type/date to save:
// save() also rejects with "Add at least one purchasable item" unless there's at least one
// race class, gate fee ("Pass"), or add-on. The Passes editor (TicketTiersList, kind="gate_fee")
// is always rendered on the Entry & Add-ons tab regardless of event type, so this test adds one
// minimal Pass to satisfy that check. Everything else (start/end datetime, status, audience,
// rider-waiver-required) already carries a valid default from EventDialog's create-mode seed, so
// only Title, Type, and one Pass are touched.
//
// Removal: the events LIST ROW only has Share/Edit buttons — there is no row-level Delete.
// Deleting requires reopening the event in the editor and using its "Delete" action (a true hard
// delete via DELETE /events/{id}, not a "cancel"), confirmed through the shared ConfirmDialog.

test('event create then delete', async ({ page }) => {
    await page.goto('/Admin/Events')
    await expect(page).not.toHaveURL(/\/Login/)

    const title = `PWTEST Event ${Date.now()}`

    await page.getByRole('button', { name: 'Add Event' }).click()
    const dlg = page.getByRole('dialog')

    // Details tab (default active tab): Title is blank and required. Type already defaults to a
    // valid event type on open (e.g. "Open Ride"), and clicking the v-select is intercepted by
    // Vuetify's field overlay, so we leave the default in place rather than reselecting.
    await dlg.getByRole('textbox', { name: 'Title' }).fill(title)
    // Starts/Ends datetime-local inputs already default to "now + 1h" / "+3h" (future), set by
    // EventDialog's create-mode seed — left untouched.

    // Entry & Add-ons tab: save() rejects unless there's at least one purchasable item (race
    // class, pass, or add-on). Enabling an existing add-on (the first add-on checkbox) is the
    // simplest way to satisfy that — no nested pass dialog needed.
    await dlg.getByRole('tab', { name: 'Entry & Add-ons' }).click()
    // Enable the seeded add-on by its label (the inactive Details tab keeps an "All day" checkbox
    // in the DOM, so target the add-on specifically rather than "the first checkbox").
    await dlg.getByRole('checkbox', { name: /Tent Camping Spot/i }).check()

    // Create-mode's primary action reads "Create event", not "Save".
    await dlg.getByRole('button', { name: 'Create event' }).click()

    // The list reloads from the server after save; make sure we're looking at the fresh list.
    await expect(dlg).toBeHidden()

    const row = page.locator('tr', { hasText: title })
    await expect(row).toBeVisible()

    // No row-level delete — reopen the editor and use its Delete action.
    await row.getByRole('button', { name: 'Edit', exact: true }).click()
    const editDlg = page.getByRole('dialog')
    await editDlg.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: title })).toHaveCount(0)
})
