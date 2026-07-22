import { test, expect } from '@playwright/test'

// Store credit flow: create a fresh throwaway credit account -> grant $1 -> confirm it lands in the
// ledger -> reverse it with an offsetting -$1 so the balance nets to $0.
//
// Self-contained: the "New account" dialog is free-text (not a customer picker), so this creates its
// own PWTEST account rather than touching a real customer. Residue: neither the account nor its
// ledger entries can be deleted from this view (only balance adjustments exist), so one PWTEST
// account with a $0 balance and two audit-trail rows remain. That's the minimum this feature allows.

test('store credit account create, grant, then reverse', async ({ page }) => {
    await page.goto('/Admin/StoreCredit')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Credit ${Date.now()}`
    const note = `${name} grant`
    const reverseNote = `${name} reversal`

    // Create the account (Customer name + Email; email/phone is the only hard requirement).
    await page.getByRole('button', { name: 'New account' }).click()
    const createDlg = page.getByRole('dialog')
    await createDlg.getByRole('textbox', { name: 'Customer name' }).fill(name)
    await createDlg.getByRole('textbox', { name: 'Email', exact: true }).fill(`pwtest.${Date.now()}@example.com`)
    await createDlg.getByRole('button', { name: 'Create', exact: true }).click()

    // Find it via the search box, then open its history/adjust dialog (the row's only button).
    await page.getByRole('textbox', { name: 'Search by name, email, or phone' }).fill(name)
    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()
    await row.getByRole('button').click()

    const dlg = page.getByRole('dialog')
    await expect(dlg).toBeVisible()

    // Grant $1.
    await dlg.getByRole('spinbutton', { name: 'Adjust (+ grant / - correct)' }).fill('1')
    await dlg.getByRole('textbox', { name: 'Note', exact: true }).fill(note)
    await dlg.getByRole('button', { name: 'Apply', exact: true }).click()
    await expect(dlg.locator('tr', { hasText: note })).toContainText('+$1.00')

    // Reverse it via the same field so the balance returns to $0.
    await dlg.getByRole('spinbutton', { name: 'Adjust (+ grant / - correct)' }).fill('-1')
    await dlg.getByRole('textbox', { name: 'Note', exact: true }).fill(reverseNote)
    await dlg.getByRole('button', { name: 'Apply', exact: true }).click()
    await expect(dlg.locator('tr', { hasText: reverseNote })).toContainText('-$1.00')
})
