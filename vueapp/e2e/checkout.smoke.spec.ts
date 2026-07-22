import { test, expect } from '@playwright/test'
import { fillStripeCard } from './helpers/stripe'

// Real checkout flows against Stripe TEST mode (verified: the tenant serves a pk_test_ key).
// Mutating — creates test-mode orders and may email a test recipient — so gated behind
// RUN_CHECKOUT=1. Uses a non-deliverable test email so nothing reaches a real customer.
const RUN = process.env.RUN_CHECKOUT === '1'
const TEST_EMAIL = 'pw-checkout-test@example.com'

test.describe('checkout (Stripe TEST mode; gated by RUN_CHECKOUT=1)', () => {
    test.skip(!RUN, 'Set RUN_CHECKOUT=1 to run the checkout flows (test-mode charges).')
    // Each flow does real Stripe + stage round-trips (mount the Payment Element, fill the card,
    // confirm, poll). That runs 30-50s, so the default per-test timeout is too tight — give them room.
    test.describe.configure({ timeout: 120_000 })

    test('gift card', async ({ page }) => {
        // Capture the create-intent response (status + body) unconditionally to see the real reason.
        let buyInfo = 'NO POST /Purchase/GiftCard was made'
        page.on('response', async (res) => {
            if (res.url().includes('/Purchase/GiftCard') && res.request().method() === 'POST') {
                buyInfo = `HTTP ${res.status()} :: ${(await res.text().catch(() => '<no body>')).slice(0, 400)}`
            }
        })

        await page.goto('/GiftCard')
        await expect(page).not.toHaveURL(/\/Login/)

        const preset = page.getByRole('button', { name: /^\$\d+$/ }).first()
        if (!(await preset.isVisible().catch(() => false))) {
            test.skip(true, 'Gift cards not available on this tenant'); return
        }
        await preset.click()
        await page.getByRole('textbox', { name: 'Recipient name' }).fill('PW Checkout Test')
        await page.getByRole('textbox', { name: 'Recipient email' }).fill(TEST_EMAIL)

        const cont = page.getByRole('button', { name: /Continue to Payment/i })
        if (await cont.isDisabled()) { test.skip(true, 'Gift card purchase disabled'); return }
        await cont.click()
        // Wait for the Stripe payment step (the "Pay $" button). If it never appears, surface the
        // create-intent response so a server-side failure reports its real reason.
        const payBtn = page.getByRole('button', { name: /^Pay \$/ })
        try {
            await payBtn.waitFor({ state: 'visible', timeout: 15_000 })
        } catch {
            throw new Error(`Did not reach payment step. createIntent → ${buyInfo}`)
        }

        await fillStripeCard(page)
        await page.getByRole('button', { name: /^Pay \$/ }).click()

        // Success: the app confirms it emailed the recipient their code.
        await expect(page.getByText(/emailed|their code/i).first()).toBeVisible({ timeout: 35_000 })
        await page.screenshot({ path: 'e2e/results/checkout-giftcard.png', fullPage: true })
    })

    test('event ticket (spectator gate)', async ({ page }) => {
        // Seeded "Upcoming Practice" event. A spectator gate needs no waiver, so the flow ends at
        // the success screen with no post-payment registration to automate. If the seed changed,
        // the tier won't be present and we skip rather than fail.
        const EVENT_ID = 'f6b0692d-e6ec-442f-80bb-7c20771c4a01'
        await page.goto(`/Event/${EVENT_ID}`)
        await expect(page).not.toHaveURL(/\/Login/)

        const specLine = page.locator('.evt-line', { hasText: 'Spectator Gate' }).first()
        if (!(await specLine.isVisible().catch(() => false))) {
            test.skip(true, 'Seeded spectator gate tier not present on this event'); return
        }
        // Bump the spectator gate to 1 (the "+" is the last button on the line).
        await specLine.getByRole('button').last().click()
        await page.getByRole('button', { name: 'Continue', exact: true }).click()

        // Details step is prefilled for the signed-in buyer; make sure a name is present.
        const nameField = page.getByRole('textbox', { name: 'Full name' })
        if ((await nameField.inputValue().catch(() => '')).trim().length < 2) {
            await nameField.fill('PW Checkout Test')
        }
        await page.getByRole('button', { name: 'Pay Now' }).click()

        // A repeat run trips the "you already have entries" guard (a lookup precedes it, so it can
        // appear a second or two later). Wait for whichever comes first — the guard or the pay step —
        // confirm the guard if present, then proceed to payment.
        const buyMore = page.getByRole('button', { name: /Yes, continue/i })
        const payBtn = page.getByRole('button', { name: /^Pay \$/ })
        await expect(buyMore.or(payBtn).first()).toBeVisible({ timeout: 20_000 })
        if (await buyMore.isVisible().catch(() => false)) await buyMore.click()
        await payBtn.waitFor({ state: 'visible', timeout: 20_000 })
        await fillStripeCard(page)
        await payBtn.click()

        await expect(page.getByText(/You're all set|entry QR/i).first()).toBeVisible({ timeout: 40_000 })
        await page.screenshot({ path: 'e2e/results/checkout-eventticket.png', fullPage: true })
    })

    test('concession / food order', async ({ page }) => {
        await page.goto('/Order')
        await expect(page).not.toHaveURL(/\/Login/)

        // The menu loads async. Wait for a concrete signal (a product card or an info alert) rather
        // than networkidle — /Order polls order status, so it never goes idle.
        const hat = page.locator('.of-card', { hasText: 'Motoland Hat' }).first()
        await hat.or(page.locator('.v-alert').first()).first()
            .waitFor({ state: 'visible', timeout: 20_000 }).catch(() => {})

        // Online ordering may be closed by the tenant's hours; if so, there's nothing to buy — skip.
        if (!(await hat.isVisible().catch(() => false))) {
            test.skip(true, 'Ordering closed or seeded product absent'); return
        }
        // The Hat has no variants/modifiers, so its "+" adds straight to the cart (no add dialog).
        // The add button has no aria-label, so target it by class.
        await hat.locator('.of-card__add').click()

        // "Place order & pay" appears in the order panel once the cart has an item. Two copies exist
        // (desktop sidebar first in DOM, mobile bar second and hidden on desktop); the first is visible.
        const placeBtn = page.getByRole('button', { name: /Place order/i }).first()
        await expect(placeBtn).toBeVisible({ timeout: 10_000 })
        await placeBtn.click()

        // The pay dialog mounts a Stripe Payment Element; fill the test card and pay.
        const payBtn = page.getByRole('button', { name: 'Pay', exact: true })
        await payBtn.waitFor({ state: 'visible', timeout: 15_000 })
        await fillStripeCard(page)
        await payBtn.click()

        // Success: the order-number confirmation dialog appears.
        await expect(page.getByText(/Order #/i).first()).toBeVisible({ timeout: 40_000 })
        await page.screenshot({ path: 'e2e/results/checkout-foodorder.png', fullPage: true })
    })

    test('season pass', async ({ page }) => {
        await page.goto('/SeasonPasses')
        await expect(page).not.toHaveURL(/\/Login/)

        const firstLine = page.locator('.sp-line').first()
        if (!(await firstLine.isVisible().catch(() => false))) {
            test.skip(true, 'No season pass products on this tenant'); return
        }
        // Add one pass: the "+" is the last button on the product line.
        await firstLine.locator('button').last().click()
        await page.getByRole('button', { name: 'Continue', exact: true }).click()

        // Details step is prefilled for the logged-in account; go straight to payment.
        await page.getByRole('button', { name: /Pay Now/i }).click()

        await fillStripeCard(page)
        await page.getByRole('button', { name: /^Pay \$/ }).click()

        // Payment success drops into the post-payment holder-registration step.
        await expect(page.getByText(/Payment received|Who are these passes for/i).first())
            .toBeVisible({ timeout: 40_000 })
        await page.screenshot({ path: 'e2e/results/checkout-seasonpass.png', fullPage: true })
    })
})
