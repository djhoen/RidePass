import { test, expect } from '@playwright/test'

// Counter Sale (Counter.vue) is a 5-step stepper: Customer -> Cart -> Waiver -> Payment -> Receipt.
// This exercises the integration-meaningful, config-independent slice: look up a real seeded
// customer by email (a real lookup API call, no hardware/charge) and confirm it advances to the
// cart step. What's purchasable at the counter (memberships, event day-passes, extras) depends on
// tenant config and event context, and steps 3-5 need a signed waiver, a live Stripe Payment
// Element, or a completed cash tender, so this flow stops at the cart step rather than mutate a
// real, non-reversible counter sale.

test('counter sale: customer lookup advances to cart', async ({ page }) => {
    await page.goto('/Admin/Counter')
    await expect(page).not.toHaveURL(/\/Login/)

    // Step 1: look up a real seeded customer by email.
    await page.getByRole('textbox', { name: 'Email' }).fill('danh@prohoods.com')
    await page.getByRole('button', { name: 'Find' }).click()

    const continueToCart = page.getByRole('button', { name: 'Continue to cart' })
    await expect(continueToCart).toBeVisible()
    await continueToCart.click()

    // Step 2: the cart step renders; Continue stays gated until an item is added.
    await expect(page.getByText('Cart is empty.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Continue', exact: true })).toBeDisabled()
})
