import { test, expect } from '@playwright/test'

// BikeShop > Rentals (BikeShopRentals.vue) has no catalog CRUD of its own to give a self-cleaning
// lifecycle test:
//  - "New rental" books a rental TRANSACTION (a Bookings-tab order) against products that are
//    already flagged rentable elsewhere, on the Bike Shop inventory page (ProductDialog's
//    isRentable flag) — it doesn't add a rental PRODUCT to the catalog.
//  - The "Rental products" (fleet) tab is a read-only availability/schedule view of those already-
//    rentable products for a chosen date window; there's no add/edit/delete control on it.
//  - "Settings" only edits tenant-wide fee-split/tax settings, which isn't a creatable/deletable
//    entity either.
// So per the assignment, this is a render-only smoke test: it confirms the page loads and its three
// tabs and their key controls are reachable, without creating anything to clean up.

test('rentals page renders its tabs and controls', async ({ page }) => {
    await page.goto('/Admin/BikeShop/Rentals')
    await expect(page).not.toHaveURL(/\/Login/)

    await expect(page.getByRole('heading', { name: 'Rentals' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'New rental' })).toBeVisible()

    await expect(page.getByRole('tab', { name: 'Bookings' })).toBeVisible()
    await expect(page.getByRole('tab', { name: 'Rental products' })).toBeVisible()
    await expect(page.getByRole('tab', { name: 'Settings' })).toBeVisible()

    // Bookings is the default tab; "Active only" only renders while it's active.
    await expect(page.getByText('Active only')).toBeVisible()

    await page.getByRole('tab', { name: 'Rental products' }).click()
    await expect(page.getByRole('columnheader', { name: 'Rental product' })).toBeVisible()

    await page.getByRole('tab', { name: 'Settings' }).click()
    await expect(page.getByText('Service fee', { exact: true })).toBeVisible()
    await expect(page.getByRole('spinbutton', { name: 'Rental tax rate' })).toBeVisible()
})
