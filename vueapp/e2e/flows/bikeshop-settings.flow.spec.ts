import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Work order stage lifecycle (BikeShop > Settings > "Work order stages" tab, WorkOrderStatusesTab.vue):
// add a custom stage -> it appears in the list -> delete it -> it's gone. Built-in stages have no
// delete button (v-if="!s.isBuiltin"), so this can only ever touch the custom stage it creates.
//
// The stage rows are plain <div>s (not a <table>) and the name is an inline-editable v-text-field,
// not static row text. Vue/Vuetify set that field's current value as a live DOM property, not the
// "value" HTML attribute, so a `[value="..."]`/hasText row lookup (the coupon-style convention)
// can't find it. Instead: rows are matched by their stable class combo, "appears" is proven by the
// row count growing by exactly one (a custom "Add stage" always appends to the end) plus a
// toHaveValue() check (a live-property assertion, unlike a text/attribute selector) on the last
// row's name input, and "gone" is proven by the count dropping back to the original.

test('work order stage create then delete', async ({ page }) => {
    await page.goto('/Admin/BikeShop/Settings')
    await expect(page).not.toHaveURL(/\/Login/)

    await page.getByRole('tab', { name: 'Work order stages' }).click()

    const name = `PWTEST Stage ${Date.now()}`

    // Each stage row carries base classes "d-flex align-center ga-2 pa-2 flex-wrap" (plus a
    // conditional "border-b"); the "add a stage" control row below the list uses "mt-3" instead of
    // "pa-2", so this selector matches only the existing stage rows, never the add-row.
    const rows = page.locator('.d-flex.align-center.ga-2.pa-2.flex-wrap')
    // Wait for the seeded stages to load before baselining the count (else `before` is 0).
    await expect(rows.first()).toBeVisible()
    const before = await rows.count()

    await page.getByRole('textbox', { name: 'New stage name' }).fill(name)
    await page.getByRole('button', { name: 'Add stage' }).click()

    await expect(rows).toHaveCount(before + 1)
    const row = rows.last()
    await expect(row.locator('input').first()).toHaveValue(name)

    // The row's trailing icon button is Delete (color-swatch button, name field, chips, notify
    // bell, move up/down, [save], make-default flag, toggle-active eye, then delete last) — same
    // "last button in the row" convention used for icon-only row actions elsewhere in this suite.
    await row.getByRole('button').last().click()
    await confirmPrompt(page)

    await expect(rows).toHaveCount(before)
})
