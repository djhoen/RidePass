import { test, expect } from '@playwright/test'

// Customers admin (Customers.vue -> CustomerDetail.vue) is a read/navigation flow: customer
// accounts are created by riders themselves (or at the Counter), not from this admin page, so
// there's nothing to create/delete here. This confirms the search box actually filters the list
// and that clicking a row navigates to a working detail page for a real seeded customer
// (danh@prohoods.com).

test('customers list search filters and opens customer detail', async ({ page }) => {
    await page.goto('/Admin/Customers')
    await expect(page).not.toHaveURL(/\/Login/)

    // Search is debounced client-side (300ms) then re-queries the server; no minimum length.
    await page.getByRole('textbox', { name: 'Search by name or email' }).fill('danh@prohoods.com')

    const row = page.locator('tr', { hasText: 'danh@prohoods.com' })
    await expect(row).toBeVisible()
    await row.click()

    // The whole <tr> is the click target (router.push to AdminCustomerDetail with the userId param).
    await expect(page).toHaveURL(/\/Admin\/Customers\/[^/]+/)
    await expect(page.getByText('danh@prohoods.com')).toBeVisible()
})
